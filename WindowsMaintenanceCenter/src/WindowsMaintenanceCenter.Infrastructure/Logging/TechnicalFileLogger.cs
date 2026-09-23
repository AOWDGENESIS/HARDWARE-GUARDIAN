using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WindowsMaintenanceCenter.Core.Security;

namespace WindowsMaintenanceCenter.Infrastructure.Logging;

/// <summary>
/// Structured technical log (spec section 42). This is deliberately NOT the live protocol:
/// the protocol is a curated, user facing activity log, while this file contains the full
/// technical detail needed for support. Format: one JSON object per line.
/// </summary>
public sealed class TechnicalFileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly LogLevel _minimumLevel;
    private readonly int _retentionDays;
    private readonly object _gate = new();

    public TechnicalFileLoggerProvider(string directory, LogLevel minimumLevel, int retentionDays)
    {
        _directory = directory;
        _minimumLevel = minimumLevel;
        _retentionDays = retentionDays;
        Directory.CreateDirectory(directory);
        Cleanup();
    }

    public ILogger CreateLogger(string categoryName) => new TechnicalFileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private static LogLevel ParseLevel(string? value) => value switch
    {
        "Trace" => LogLevel.Trace,
        "Debug" => LogLevel.Debug,
        "Information" => LogLevel.Information,
        "Warning" => LogLevel.Warning,
        "Error" => LogLevel.Error,
        "Critical" => LogLevel.Critical,
        "None" => LogLevel.None,
        _ => LogLevel.Information,
    };

    public static LogLevel ParseConfiguredLevel(string? value) => ParseLevel(value);

    private bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= _minimumLevel;

    private void Write(string category, LogLevel level, EventId eventId, string message, Exception? exception)
    {
        // Secrets are removed before the line is built, not after it is written (M38-S-001): a
        // credential that reached the file once is in the file, and deleting it later does not.
        // The guard recognises secret *shapes* (keyword values, command line passwords, recovery
        // passwords, key blocks) - what it cannot recognise, it cannot remove, and the log rules
        // therefore forbid writing file contents in the first place (M38-S-002).
        var safeMessage = SensitiveDataGuard.Redact(message);

        var payload = new Dictionary<string, object?>
        {
            ["ts"] = DateTimeOffset.Now,
            ["level"] = level.ToString(),
            ["category"] = category,
            ["eventId"] = eventId.Id,
            ["message"] = safeMessage,
            ["thread"] = Environment.CurrentManagedThreadId,
        };

        if (exception is not null)
        {
            payload["exception"] = exception.GetType().FullName;
            payload["exceptionMessage"] = SensitiveDataGuard.Redact(exception.Message);
            payload["stack"] = SensitiveDataGuard.Redact(exception.ToString());
        }

        if (SensitiveDataGuard.ContainsSensitiveData(message))
        {
            // Saying that something was removed is itself evidence: a reader must be able to tell a
            // quiet log from a log that had a secret in it.
            payload["redacted"] = true;
        }

        lock (_gate)
        {
            var path = Path.Combine(_directory, $"technical-{DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.jsonl");
            try
            {
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.WriteLine(JsonSerializer.Serialize(payload));
            }
            catch (IOException)
            {
                // Logging must never bring the application down.
            }
        }
    }

    private void Cleanup()
    {
        if (_retentionDays <= 0)
        {
            return;
        }

        try
        {
            var threshold = DateTime.UtcNow.AddDays(-_retentionDays);
            foreach (var file in Directory.EnumerateFiles(_directory, "technical-*.jsonl"))
            {
                if (File.GetLastWriteTimeUtc(file) < threshold)
                {
                    File.Delete(file);
                }
            }
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    private sealed class TechnicalFileLogger : ILogger
    {
        private readonly TechnicalFileLoggerProvider _provider;
        private readonly string _category;

        public TechnicalFileLogger(TechnicalFileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || formatter is null)
            {
                return;
            }

            _provider.Write(_category, logLevel, eventId, formatter(state, exception), exception);
        }
    }
}
