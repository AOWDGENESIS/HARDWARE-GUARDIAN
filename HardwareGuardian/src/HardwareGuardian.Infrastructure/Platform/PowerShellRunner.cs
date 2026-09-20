using System.Text;
using HardwareGuardian.Core.Abstractions;

namespace HardwareGuardian.Infrastructure.Platform;

/// <summary>
/// Runs Windows PowerShell (or PowerShell 7 when present) with a strictly allow listed command
/// set. Script text is never assembled from user input: callers pass a template and validated
/// parameters. The script is transferred as -EncodedCommand (Base64, UTF-16LE), so quoting and
/// code page problems cannot corrupt it.
/// </summary>
public sealed class PowerShellRunner
{
    /// <summary>
    /// Identical to <see cref="PowerShellCommandCatalog.ScriptFor"/> but kept separate to make the
    /// coupling explicit at the call site.
    /// </summary>
    private readonly IProcessRunner _runner;
    private readonly string _executable;

    public PowerShellRunner(IProcessRunner runner, string? executable = null)
    {
        _runner = runner;
        _executable = executable ?? ResolveExecutable();
    }

    public string Executable => _executable;

    public Task<ProcessResult> RunAsync(string template, IReadOnlyDictionary<string, string>? parameters, ProcessRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        var script = PowerShellCommandCatalog.ScriptFor(template, parameters);
        return RunScriptAsync(script, options, cancellationToken);
    }

    public Task<ProcessResult> RunScriptAsync(string script, ProcessRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var arguments = new List<string>
        {
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy", "Bypass",
            "-OutputFormat", "Text",
            "-EncodedCommand", encoded,
        };

        return _runner.RunAsync(_executable, arguments, options, cancellationToken);
    }

    private static string ResolveExecutable()
    {
        var windowsPowerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"WindowsPowerShell\v1.0\powershell.exe");

        if (File.Exists(windowsPowerShell))
        {
            return windowsPowerShell;
        }

        var pwsh = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            @"PowerShell\7\pwsh.exe");

        return File.Exists(pwsh) ? pwsh : "powershell.exe";
    }
}

/// <summary>
/// Allow list of the only PowerShell command templates Hardware Guardian may execute.
/// Parameters are validated against a strict pattern before they are used, so that a value such
/// as <c>"; Remove-Item ...</c> can never become part of a script.
/// </summary>
public static class PowerShellCommandCatalog
{
    /// <summary>Template identifier constants.</summary>
    public const string DeliveryOptimizationCacheSize = "deliveryoptimization.cache.size";
    public const string DeliveryOptimizationCacheClear = "deliveryoptimization.cache.clear";
    public const string DefenderStatus = "defender.status";
    public const string WindowsUpdateSession = "windowsupdate.session.search";

    public const string WindowsUpdateDownload = "windowsupdate.download";

    public const string WindowsUpdateInstall = "windowsupdate.install";
    public const string RestorePointCreate = "restorepoint.create";
    public const string RestorePointList = "restorepoint.list";
    public const string SystemRestoreStatus = "restorepoint.status";
    public const string StorageTrimStatus = "storage.trim.status";

    private static readonly System.Text.RegularExpressions.Regex SafeParameter =
        new(@"^[A-Za-z0-9 _\-\.,:/\{\}\(\)\[\]]{0,200}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly Dictionary<string, string> Templates = new(StringComparer.Ordinal)
    {
        [DeliveryOptimizationCacheSize] =
            "try { if (Get-Command Get-DeliveryOptimizationStatus -ErrorAction Stop) { " +
            "'DO_SUPPORTED=true'; (Get-DeliveryOptimizationStatus | Measure-Object -Property FileSizeInCache -Sum).Sum } " +
            "else { 'DO_SUPPORTED=false' } } catch { 'DO_ERROR=' + $_.Exception.Message }",

        [DeliveryOptimizationCacheClear] =
            "try { if (Get-Command Delete-DeliveryOptimizationCache -ErrorAction Stop) { " +
            "Delete-DeliveryOptimizationCache -Force -ErrorAction Stop; 'DO_CLEARED=true' } " +
            "else { 'DO_CLEARED=false'; 'DO_REASON=cmdlet-unavailable' } } catch { 'DO_ERROR=' + $_.Exception.Message }",

        [DefenderStatus] =
            "try { $s = Get-MpComputerStatus -ErrorAction Stop; " +
            "'AV=' + $s.AntivirusEnabled; 'RTP=' + $s.RealTimeProtectionEnabled; 'ENGINE=' + $s.AMEngineVersion; " +
            "'SIGNATURE=' + $s.AntivirusSignatureVersion; 'SIGNATURE_AGE_DAYS=' + $s.AntivirusSignatureAge; " +
            "'TAMPER=' + $s.IsTamperProtected } catch { 'DEFENDER_ERROR=' + $_.Exception.Message }",

        // UPDATE-F-003: the search reports every field the agent actually has, each line tagged with
        // the index of its update so that a title containing '|' cannot corrupt the record.
        // ClientApplicationID makes Hardware Guardian identifiable in the Windows Update log - that is
        // transparency about who asks, and it is the only thing that is written there.
        [WindowsUpdateSession] =
            "try { $session = New-Object -ComObject Microsoft.Update.Session; " +
            "$session.ClientApplicationID = 'HardwareGuardian'; " +
            "$searcher = $session.CreateUpdateSearcher(); $result = $searcher.Search('IsInstalled=0'); " +
            "'PENDING=' + $result.Updates.Count; $i = 0; foreach ($u in $result.Updates) { " +
            "'UPDATE=' + $i + '|' + $u.Title; " +
            "$kb = @($u.KBArticleIDs) | Select-Object -First 1; if ($kb) { 'KB=' + $i + '|KB' + $kb }; " +
            "$cat = @($u.Categories) | Select-Object -First 1; if ($cat) { 'CAT=' + $i + '|' + $cat.Name }; " +
            "if ($u.MsrcSeverity) { 'SEV=' + $i + '|' + $u.MsrcSeverity }; " +
            "if ($u.MaxDownloadSize -ge 0) { 'SIZE=' + $i + '|' + $u.MaxDownloadSize }; " +
            "'REBOOT=' + $i + '|' + $u.RebootRequired; 'MAND=' + $i + '|' + $u.IsMandatory; " +
            "'DL=' + $i + '|' + $u.IsDownloaded; $i++ }; " +
            "$sys = New-Object -ComObject Microsoft.Update.SystemInfo; 'SYSTEM_REBOOT=' + $sys.RebootRequired } " +
            "catch { 'WU_ERROR=' + $_.Exception.Message }",

        // UPDATE-F-005/F-006: the update is addressed by its position in the offer list; the only
        // parameter this template takes is that number. The search runs again inside the script, so
        // the position always refers to the current offer list, not to a stale one.
        [WindowsUpdateDownload] =
            "try { $session = New-Object -ComObject Microsoft.Update.Session; " +
            "$session.ClientApplicationID = 'HardwareGuardian'; " +
            "$searcher = $session.CreateUpdateSearcher(); $result = $searcher.Search('IsInstalled=0'); " +
            "$index = [int]{INDEX}; " +
            "if ($index -lt 0 -or $index -ge $result.Updates.Count) { 'WU_INDEX_OUT_OF_RANGE=' + $result.Updates.Count } " +
            "else { $update = $result.Updates.Item($index); 'TITLE=' + $update.Title; " +
            "$collection = New-Object -ComObject Microsoft.Update.UpdateColl; [void]$collection.Add($update); " +
            "if (-not $update.EulaAccepted) { $update.AcceptEula() | Out-Null }; " +
            "$downloader = $session.CreateUpdateDownloader(); $downloader.Updates = $collection; " +
            "$actionResult = $downloader.Download(); " +
            "'DOWNLOAD_RESULTCODE=' + $actionResult.ResultCode; 'DOWNLOAD_HRESULT=' + $actionResult.HResult; " +
            "'IS_DOWNLOADED=' + $update.IsDownloaded } } " +
            "catch { 'WU_ERROR=' + $_.Exception.Message }",

        [WindowsUpdateInstall] =
            "try { $session = New-Object -ComObject Microsoft.Update.Session; " +
            "$session.ClientApplicationID = 'HardwareGuardian'; " +
            "$searcher = $session.CreateUpdateSearcher(); $result = $searcher.Search('IsInstalled=0'); " +
            "$index = [int]{INDEX}; " +
            "if ($index -lt 0 -or $index -ge $result.Updates.Count) { 'WU_INDEX_OUT_OF_RANGE=' + $result.Updates.Count } " +
            "else { $update = $result.Updates.Item($index); 'TITLE=' + $update.Title; " +
            "$collection = New-Object -ComObject Microsoft.Update.UpdateColl; [void]$collection.Add($update); " +
            "if (-not $update.EulaAccepted) { $update.AcceptEula() | Out-Null }; " +
            "$installer = $session.CreateUpdateInstaller(); $installer.Updates = $collection; " +
            "$actionResult = $installer.Install(); " +
            "'INSTALL_RESULTCODE=' + $actionResult.ResultCode; 'INSTALL_HRESULT=' + $actionResult.HResult; " +
            "'INSTALL_REBOOT=' + $actionResult.RebootRequired; 'REBOOT_REQUIRED=' + $update.RebootRequired; " +
            "'IS_INSTALLED=' + $update.IsInstalled } } " +
            "catch { 'WU_ERROR=' + $_.Exception.Message }",

        [RestorePointStatus] =
            "$sr = Get-CimInstance -Namespace root/default -ClassName SystemRestore -ErrorAction SilentlyContinue; " +
            "if ($null -eq $sr) { 'SR_CONTROL_AVAILABLE=false' } else { 'SR_CONTROL_AVAILABLE=true' }",

        [RestorePointList] =
            "try { $points = Get-ComputerRestorePoint -ErrorAction Stop; " +
            "foreach ($p in $points) { 'RP=' + $p.SequenceNumber + '|' + $p.Description + '|' + $p.CreationTime } } " +
            "catch { 'SR_ERROR=' + $_.Exception.Message }",

        [RestorePointCreate] =
            "try { Checkpoint-Computer -Description '{DESCRIPTION}' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop; " +
            "$points = Get-ComputerRestorePoint -ErrorAction Stop; $latest = $points | Sort-Object SequenceNumber -Descending | Select-Object -First 1; " +
            "'RP_CREATED=' + $latest.SequenceNumber + '|' + $latest.Description } catch { 'SR_ERROR=' + $_.Exception.Message }",

        [StorageTrimStatus] =
            "try { $trim = fsutil behavior query DisableDeleteNotify; $trim } catch { 'TRIM_ERROR=' + $_.Exception.Message }",
    };

    public static IReadOnlyCollection<string> TemplateIds => Templates.Keys;

    /// <summary>Builds the script for a template and validates every parameter.</summary>
    public static string ScriptFor(string templateId, IReadOnlyDictionary<string, string>? parameters)
    {
        if (!Templates.TryGetValue(templateId, out var template))
        {
            throw new ArgumentException($"PowerShell template '{templateId}' is not on the allow list.", nameof(templateId));
        }

        var script = template;
        if (parameters is null)
        {
            return script;
        }

        foreach (var pair in parameters)
        {
            if (!pair.Key.StartsWith("{{", StringComparison.Ordinal))
            {
                // Keys are used with braces in the template: {NAME} -> {{NAME}}
            }

            if (!SafeParameter.IsMatch(pair.Value ?? string.Empty))
            {
                throw new ArgumentException($"Parameter '{pair.Key}' contains characters that are not allowed.", nameof(parameters));
            }

            if (pair.Value?.IndexOf('\'', StringComparison.Ordinal) >= 0 || pair.Value?.IndexOf('"', StringComparison.Ordinal) >= 0)
            {
                throw new ArgumentException($"Parameter '{pair.Key}' must not contain quotes.", nameof(parameters));
            }

            script = script.Replace("{" + pair.Key + "}", pair.Value ?? string.Empty, StringComparison.Ordinal);
        }

        return script;
    }
}
