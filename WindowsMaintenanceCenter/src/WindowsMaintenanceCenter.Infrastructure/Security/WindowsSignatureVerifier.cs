using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;
using WindowsMaintenanceCenter.Infrastructure.Platform;

namespace WindowsMaintenanceCenter.Infrastructure.Security;

/// <summary>
/// Verifies the Authenticode signature of a file (spec sections 10 and 30).
/// Primary implementation: Get-AuthenticodeSignature (exact platform semantics).
/// Fallback: WinVerifyTrust via P/Invoke when PowerShell is unavailable.
/// A file that could not be checked is reported as "not verified" - never as "unsigned" - and
/// catalog signatures are explicitly reported as out of scope for this check.
/// </summary>
public sealed class WindowsSignatureVerifier : ISignatureVerifier
{
    private readonly PowerShellRunner _powerShell;
    private readonly IClock _clock;

    public WindowsSignatureVerifier(PowerShellRunner powerShell, IClock clock)
    {
        _powerShell = powerShell;
        _clock = clock;
    }

    public SignatureResult VerifyFile(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return SignatureResult.NotChecked(path ?? string.Empty, "file does not exist", LocalizedText.Of("Signature_FileMissing"));
        }

        if (!OperatingSystem.IsWindows())
        {
            return SignatureResult.NotChecked(path, "not running on Windows", LocalizedText.Of("Signature_PlatformUnsupported"));
        }

        var viaPowerShell = VerifyWithPowerShell(path, cancellationToken);
        if (viaPowerShell is not null)
        {
            return viaPowerShell;
        }

        return VerifyWithWinVerifyTrust(path);
    }

    private SignatureResult? VerifyWithPowerShell(string path, CancellationToken cancellationToken)
    {
        var script =
            "$ErrorActionPreference='Stop'; " +
            "try { $s = Get-AuthenticodeSignature -LiteralPath " + Quote(path) + "; " +
            "'STATUS=' + $s.Status; " +
            "if ($s.SignerCertificate) { 'SUBJECT=' + $s.SignerCertificate.Subject; 'ISSUER=' + $s.SignerCertificate.Issuer; " +
            "'THUMBPRINT=' + $s.SignerCertificate.Thumbprint; 'NOTBEFORE=' + $s.SignerCertificate.NotBefore.ToString('o'); " +
            "'NOTAFTER=' + $s.SignerCertificate.NotAfter.ToString('o') } " +
            "if ($s.StatusMessage) { 'MESSAGE=' + $s.StatusMessage } } catch { 'ERROR=' + $_.Exception.Message }";

        ProcessResult result;
        try
        {
            result = _powerShell.RunScriptAsync(script, new ProcessRunOptions { Timeout = TimeSpan.FromSeconds(30) }, cancellationToken)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return SignatureResult.NotChecked(path, $"{ex.GetType().Name}: {ex.Message}", LocalizedText.Of("Signature_NotChecked"));
        }

        if (result.TimedOut)
        {
            return SignatureResult.NotChecked(path, "Get-AuthenticodeSignature timed out", LocalizedText.Of("Signature_Timeout"));
        }

        if (result.ErrorDetail is not null)
        {
            return null; // fall back to WinVerifyTrust
        }

        var status = Extract(result.StandardOutput, "STATUS=");
        if (status is null)
        {
            var error = Extract(result.StandardOutput, "ERROR=");
            return error is null ? null : SignatureResult.NotChecked(path, error, LocalizedText.Of("Signature_NotChecked"));
        }

        var subject = Extract(result.StandardOutput, "SUBJECT=");
        var issuer = Extract(result.StandardOutput, "ISSUER=");
        var notBefore = ParseDate(Extract(result.StandardOutput, "NOTBEFORE="));
        var notAfter = ParseDate(Extract(result.StandardOutput, "NOTAFTER="));
        var message = Extract(result.StandardOutput, "MESSAGE=");

        var (isSigned, isTrusted, verification, summaryKey) = status.Trim().ToUpperInvariant() switch
        {
            "VALID" => (true, true, VerificationLevel.SignatureVerified, "Signature_Valid"),
            "NOTSIGNED" => ((bool?)false, false, VerificationLevel.MetadataMatch, "Signature_NotSigned"),
            "UNKNOWNERROR" => ((bool?)null, false, VerificationLevel.NotVerified, "Signature_UnknownError"),
            "NOTTRUSTED" => ((bool?)true, false, VerificationLevel.NotVerified, "Signature_NotTrusted"),
            "INVALID" => ((bool?)true, false, VerificationLevel.NotVerified, "Signature_Invalid"),
            "HASHMISMATCH" => ((bool?)true, false, VerificationLevel.NotVerified, "Signature_HashMismatch"),
            _ => ((bool?)null, false, VerificationLevel.NotVerified, "Signature_NotChecked"),
        };

        var detail = message is null
            ? "source=Get-AuthenticodeSignature"
            : $"source=Get-AuthenticodeSignature; message={message}; note: catalog signatures are not covered by this check";

        return new SignatureResult
        {
            Path = path,
            IsSigned = isSigned,
            IsTrusted = isTrusted,
            Signer = string.IsNullOrWhiteSpace(subject) ? TextInfo.Unknown(ValueOrigin.WindowsApi(_clock.Now, "Get-AuthenticodeSignature")) : TextInfo.Known(subject!, ValueOrigin.WindowsApi(_clock.Now, "Get-AuthenticodeSignature")),
            Issuer = string.IsNullOrWhiteSpace(issuer) ? TextInfo.Unknown() : TextInfo.Known(issuer!, ValueOrigin.WindowsApi(_clock.Now, "Get-AuthenticodeSignature")),
            SubjectCommonName = string.IsNullOrWhiteSpace(subject) ? TextInfo.Unknown() : TextInfo.Known(CommonName(subject!), ValueOrigin.WindowsApi(_clock.Now, "Get-AuthenticodeSignature")),
            ValidFrom = notBefore,
            ValidTo = notAfter,
            Verification = verification,
            Detail = detail,
            Summary = LocalizedText.Of(summaryKey),
        };
    }

    private SignatureResult VerifyWithWinVerifyTrust(string path)
    {
        try
        {
            var fileInfo = new WinTrustFileInfo
            {
                cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                pcwszFilePath = path,
                hFile = IntPtr.Zero,
                pgKnownSubject = IntPtr.Zero,
            };

            var fileInfoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            try
            {
                Marshal.StructureToPtr(fileInfo, fileInfoPointer, fDeleteOld: false);

                var trustData = new WinTrustData
                {
                    cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                    dwUIChoice = 2,                 // WTD_UI_NONE
                    fdwRevocationChecks = 0,        // WTD_REVOKE_NONE
                    dwUnionChoice = 1,              // WTD_CHOICE_FILE
                    pFile = fileInfoPointer,
                    dwStateAction = 0,              // WTD_STATEACTION_IGNORE
                    dwProvFlags = 0x00000010 | 0x00000040, // WTD_CACHE_ONLY_URL_RETRIEVAL | WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT
                };

                var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
                var result = WinVerifyTrust(IntPtr.Zero, ref action, ref trustData);

                if (result == 0)
                {
                    return new SignatureResult
                    {
                        Path = path,
                        IsSigned = true,
                        IsTrusted = true,
                        Verification = VerificationLevel.SignatureVerified,
                        Detail = "source=WinVerifyTrust",
                        Summary = LocalizedText.Of("Signature_Valid"),
                        Signer = TextInfo.Unknown(ValueOrigin.WindowsApi(_clock.Now, "WinVerifyTrust")),
                        Issuer = TextInfo.Unknown(),
                        SubjectCommonName = TextInfo.Unknown(),
                    };
                }

                // TRUST_E_NOSIGNATURE (0x800B0100) means the file carries no embedded signature.
                var unsigned = unchecked((uint)result) == 0x800B0100;
                return new SignatureResult
                {
                    Path = path,
                    IsSigned = unsigned ? false : null,
                    IsTrusted = false,
                    Verification = unsigned ? VerificationLevel.MetadataMatch : VerificationLevel.NotVerified,
                    Detail = $"source=WinVerifyTrust; hresult=0x{unchecked((uint)result):X8}",
                    Summary = LocalizedText.Of(unsigned ? "Signature_NotSigned" : "Signature_NotChecked"),
                    Signer = TextInfo.Unknown(),
                    Issuer = TextInfo.Unknown(),
                    SubjectCommonName = TextInfo.Unknown(),
                };
            }
            finally
            {
                Marshal.FreeHGlobal(fileInfoPointer);
            }
        }
        catch (Exception ex)
        {
            return SignatureResult.NotChecked(path, $"{ex.GetType().Name}: {ex.Message}", LocalizedText.Of("Signature_NotChecked"));
        }
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string? Extract(string output, string prefix)
    {
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[prefix.Length..].Trim();
            }
        }

        return null;
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static string CommonName(string subject)
    {
        var match = Regex.Match(subject, @"CN=([^,]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : subject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WinTrustData pWVTData);
}
