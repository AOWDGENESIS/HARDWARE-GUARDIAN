using System.Diagnostics;
using System.Text;
using HardwareGuardian.Core.Abstractions;

namespace HardwareGuardian.Infrastructure.Platform;

/// <summary>
/// Runs external tools with an argument array (never a shell string), a hard timeout and
/// bounded output. There is no shell interpretation, therefore no quoting or injection risk.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        ProcessRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        arguments ??= Array.Empty<string>();
        options ??= new ProcessRunOptions();

        if (!File.Exists(executable) && !Path.IsPathRooted(executable))
        {
            // Rely on PATH resolution for well known tools such as Dism.exe or powercfg.exe.
        }

        var info = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrWhiteSpace(options.WorkingDirectory))
        {
            info.WorkingDirectory = options.WorkingDirectory!;
        }

        if (options.Environment is not null)
        {
            foreach (var pair in options.Environment)
            {
                info.Environment[pair.Key] = pair.Value;
            }
        }

        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = info };

        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();

        try
        {
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null)
                {
                    return;
                }

                lock (standardOutput)
                {
                    if (standardOutput.Length < options.MaxOutputCharacters)
                    {
                        standardOutput.AppendLine(e.Data);
                    }
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null)
                {
                    return;
                }

                lock (standardError)
                {
                    if (standardError.Length < options.MaxOutputCharacters)
                    {
                        standardError.AppendLine(e.Data);
                    }
                }
            };

            if (!process.Start())
            {
                return ProcessResult.Failure(executable, "process could not be started");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(options.Timeout);

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                var timedOut = !cancellationToken.IsCancellationRequested;
                TryKill(process);
                stopwatch.Stop();
                string outText, errText;
                lock (standardOutput)
                {
                    outText = standardOutput.ToString();
                }

                lock (standardError)
                {
                    errText = standardError.ToString();
                }

                return new ProcessResult
                {
                    Executable = executable,
                    Arguments = arguments,
                    ExitCode = -1,
                    StandardOutput = outText,
                    StandardError = errText,
                    Duration = stopwatch.Elapsed,
                    TimedOut = timedOut,
                    ErrorDetail = timedOut
                        ? $"timeout after {options.Timeout.TotalSeconds:F0}s"
                        : "cancelled by caller",
                };
            }

            stopwatch.Stop();
            string stdout, stderr;
            lock (standardOutput)
            {
                stdout = standardOutput.ToString();
            }

            lock (standardError)
            {
                stderr = standardError.ToString();
            }

            return new ProcessResult
            {
                Executable = executable,
                Arguments = arguments,
                ExitCode = process.ExitCode,
                StandardOutput = stdout,
                StandardError = stderr,
                Duration = stopwatch.Elapsed,
            };
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            stopwatch.Stop();
            return new ProcessResult
            {
                Executable = executable,
                Arguments = arguments,
                ExitCode = -1,
                Duration = stopwatch.Elapsed,
                ErrorDetail = $"{ex.GetType().Name}: {ex.Message}",
            };
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // The process may have exited between the check and the kill.
        }
    }
}
