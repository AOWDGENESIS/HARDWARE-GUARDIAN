using WindowsMaintenanceCenter.Core.Security;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// The guard that keeps secrets out of the technical log and the audit file (chapter 44, M38-S-001
/// "no passwords" and M38-S-002 "no document contents"; the same check chapter 45/M39-S-005 wants for
/// the diagnostics export).
///
/// Two kinds of assertion matter here, and both are present:
///
/// * A secret shape **must** be removed - otherwise the log leaks it.
/// * Ordinary text **must not** be mangled - otherwise the evidence becomes unreadable and the filter
///   gets switched off in practice. The second half is the one that is usually forgotten.
///
/// What the tests deliberately do not claim: that the guard recognises a secret written as a normal
/// sentence. It cannot, and no test here pretends otherwise.
/// </summary>
public sealed class SensitiveDataGuardTests
{
    [Theory]
    [InlineData("password=hunter2secret")]
    [InlineData("password: hunter2secret")]
    [InlineData("Password = \"hunter2secret\"")]
    [InlineData("passwort: hunter2secret")]
    [InlineData("{\"Password\":\"hunter2secret\"}")]
    [InlineData("token=abcd1234efgh")]
    [InlineData("apiKey: 0123456789abcdef")]
    public void A_password_value_is_removed(string text)
    {
        var redacted = SensitiveDataGuard.Redact(text);

        Assert.DoesNotContain("hunter2secret", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("abcd1234efgh", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("0123456789abcdef", redacted, StringComparison.Ordinal);
        Assert.Contains(SensitiveDataGuard.Marker, redacted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("runas /user:admin --password hunter2secret")]
    [InlineData("tool.exe -Password hunter2secret")]
    [InlineData("cmd /c setup.exe /password:hunter2secret")]
    [InlineData("manage-bde -protectors -add -Password hunter2secret")]
    public void A_password_on_a_command_line_is_removed(string text)
    {
        var redacted = SensitiveDataGuard.Redact(text);

        Assert.DoesNotContain("hunter2secret", redacted, StringComparison.Ordinal);
        // The switch stays, only the value is gone: a reader has to be able to see what was run.
        Assert.Contains("password", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(SensitiveDataGuard.Marker, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bitlocker_recovery_password_is_removed_grouped_and_ungrouped()
    {
        const string grouped = "Recovery key: 123456-234567-345678-456789-567890-678901-789012-890123";
        const string ungrouped = "id 123456234567345678456789567890678901789012890123 saved";

        var first = SensitiveDataGuard.Redact(grouped);
        var second = SensitiveDataGuard.Redact(ungrouped);

        Assert.DoesNotContain("123456-234567", first, StringComparison.Ordinal);
        Assert.DoesNotContain("123456234567", second, StringComparison.Ordinal);
        // The sentence around it survives - only the secret is gone.
        Assert.StartsWith("Recovery key:", first, StringComparison.Ordinal);
        Assert.EndsWith("saved", second, StringComparison.Ordinal);
    }

    [Fact]
    public void A_private_key_block_is_removed_as_a_whole()
    {
        const string pem = "-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA\n-----END RSA PRIVATE KEY-----";

        var redacted = SensitiveDataGuard.Redact(pem);

        Assert.DoesNotContain("MIIEowIBAAKCAQEA", redacted, StringComparison.Ordinal);
        Assert.Contains(SensitiveDataGuard.Marker, redacted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Volume C: was checked; free space 120 GiB")]
    [InlineData("The volume password policy was read")]
    [InlineData("path=C:\\Windows\\Temp\\wmc-2026.log")]
    [InlineData("Service TokenBroker is running (StartType Automatic)")]
    [InlineData("DISM /ScanHealth returned exit code 0 after 453.4 s")]
    [InlineData("--password-hint \"the usual one\"")]
    public void Ordinary_evidence_text_stays_readable(string text)
    {
        var redacted = SensitiveDataGuard.Redact(text);

        Assert.Equal(text, redacted);
        Assert.False(SensitiveDataGuard.ContainsSensitiveData(text));
    }

    [Fact]
    public void A_short_value_is_not_a_secret_and_stays()
    {
        // "pwd=n/a" is a placeholder, not a password. Eating it would hide the fact that a value was
        // missing, which is exactly the information a reader of the log needs.
        const string text = "pwd=n/a; token=-";

        Assert.Equal(text, SensitiveDataGuard.Redact(text));
    }

    [Fact]
    public void Redaction_is_idempotent()
    {
        const string text = "password=hunter2secret and 123456-234567-345678-456789-567890-678901-789012-890123";

        var once = SensitiveDataGuard.Redact(text);
        var twice = SensitiveDataGuard.Redact(once);

        Assert.Equal(once, twice);
        Assert.DoesNotContain("hunter2secret", once, StringComparison.Ordinal);
    }

    [Fact]
    public void The_scan_names_the_rule_and_never_carries_the_secret()
    {
        const string text = "verbindung: password=hunter2secret";

        var findings = SensitiveDataGuard.Scan(text);

        var finding = Assert.Single(findings);
        Assert.Equal(SensitiveDataGuard.KeywordRule, finding.Rule);
        Assert.DoesNotContain("hunter2secret", finding.Excerpt, StringComparison.Ordinal);
        Assert.Contains(SensitiveDataGuard.Marker, finding.Excerpt, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_places_are_reported_in_the_order_they_appear()
    {
        const string text = "password=firstsecret then 123456-234567-345678-456789-567890-678901-789012-890123";

        var findings = SensitiveDataGuard.Scan(text);

        Assert.Equal(2, findings.Count);
        Assert.True(findings[0].Index < findings[1].Index);
        Assert.Equal(SensitiveDataGuard.KeywordRule, findings[0].Rule);
        Assert.Equal(SensitiveDataGuard.RecoveryPasswordRule, findings[1].Rule);
    }

    [Fact]
    public void An_empty_text_is_handled_without_a_finding()
    {
        Assert.Empty(SensitiveDataGuard.Scan(null));
        Assert.Empty(SensitiveDataGuard.Scan(string.Empty));
        Assert.Equal(string.Empty, SensitiveDataGuard.Redact(null));
        Assert.False(SensitiveDataGuard.ContainsSensitiveData(" "));
    }
}
