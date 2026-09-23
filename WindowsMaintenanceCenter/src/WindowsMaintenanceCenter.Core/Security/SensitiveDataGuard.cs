using System.Text;
using System.Text.RegularExpressions;

namespace WindowsMaintenanceCenter.Core.Security;

/// <summary>One place where sensitive data was found, with the rule that found it.</summary>
public sealed record SensitiveDataFinding
{
    /// <summary>Name of the rule, e.g. <c>command-line password</c>. Never localised - it goes into a log.</summary>
    public string Rule { get; init; } = string.Empty;

    /// <summary>Position of the match in the scanned text, for a human who has to check it.</summary>
    public int Index { get; init; }

    /// <summary>Short, already masked excerpt - enough to recognise the place, never the secret itself.</summary>
    public string Excerpt { get; init; } = string.Empty;
}

/// <summary>
/// Keeps secrets out of the technical log and the audit file (chapter 44, M38-S-001 "no passwords",
/// M38-S-002 "no document contents", and the check chapter 45/M39-S-005 asks for on the diagnostics
/// export).
///
/// What this class can do - and what it cannot:
///
/// * It recognises **shapes**, not meaning: a password that appears as the value of a word like
///   <c>password</c>, <c>token</c> or <c>apikey</c>, a password handed over on a command line
///   (<c>--password</c>, <c>-Password</c>, <c>/password:</c>), a BitLocker recovery password
///   (48 digits in eight groups of six, the documented format) and a private key block.
/// * It **cannot** recognise a document's contents, a name, an address or a secret written as a
///   normal sentence. A tool that claims that would be lying. The honest statement is: these shapes
///   are removed, and M38-S-002 rests on the fact that the application never writes file contents
///   into a log - which is enforced by the code paths that build the messages, not by this filter.
/// * It never guesses a value back. A redacted value is replaced by <see cref="Marker"/>, so a reader
///   sees that something was there and that it was removed.
///
/// The guard is deliberately conservative: it only matches when a secret *shape* is present, so a
/// harmless message such as "the volume password policy was read" is not mangled. A filter that
/// rewrites ordinary text would make the log useless and would be switched off in practice.
/// </summary>
public static class SensitiveDataGuard
{
    /// <summary>What a removed value is replaced with. Deliberately ugly so nobody overlooks it.</summary>
    public const string Marker = "[redacted]";

    /// <summary>Name of the rule for a value that follows a keyword like <c>password</c>.</summary>
    public const string KeywordRule = "keyword assignment";

    public const string CommandLineRule = "command line password";

    public const string RecoveryPasswordRule = "BitLocker recovery password";

    public const string PrivateKeyRule = "private key material";

    /// <summary>Keywords whose value is a secret. Matched case-insensitively, with any separator.</summary>
    private static readonly string[] Keywords =
    {
        "password", "passwort", "passphrase", "pwd", "secret", "token", "apikey", "api_key", "accesskey",
    };

    // "password = value", "password: value", "password value" and the JSON/URL shape "password":"value".
    // The value must be at least four characters, otherwise a placeholder such as "n/a" is a hit too.
    private static readonly Regex KeywordAssignment = new(
        @"\b(?<key>" + string.Join("|", Keywords) + @")(?<sep>[""']?\s*[:=]\s*|[""']\s*:\s*|\s+)" +
        @"(?<quote>[""']?)(?<value>[^\s""',;]{4,})(?<trailing>[""']?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Command line forms. "-p" is deliberately not included: it is a common switch for "path" and a
    // filter that eats paths would destroy the evidence it is supposed to protect.
    private static readonly Regex CommandLinePassword = new(
        @"(?<prefix>--password|--passphrase|-Password|-Passphrase|/password:|/passphrase:)" +
        @"(?<sep>[\s:=]+)(?<quote>[""']?)(?<value>[^\s""']{4,})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // A BitLocker recovery password is 48 digits in eight groups of six. Grouped or ungrouped.
    private static readonly Regex RecoveryPassword = new(
        @"\b(?:\d{6}[-\s]){7}\d{6}\b|\b\d{48}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PrivateKeyBlock = new(
        @"-----BEGIN [A-Z ]*PRIVATE KEY-----.*?-----END [A-Z ]*PRIVATE KEY-----",
        RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Replaces everything that looks like a secret with <see cref="Marker"/>. Idempotent: running it
    /// twice changes nothing more.
    /// </summary>
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var result = PrivateKeyBlock.Replace(text, $"-----BEGIN PRIVATE KEY----- {Marker} -----END PRIVATE KEY-----");
        result = CommandLinePassword.Replace(result, match =>
            $"{match.Groups["prefix"].Value}{match.Groups["sep"].Value}{match.Groups["quote"].Value}{Marker}");
        result = KeywordAssignment.Replace(result, match =>
            $"{match.Groups["key"].Value}{match.Groups["sep"].Value}{match.Groups["quote"].Value}{Marker}{match.Groups["trailing"].Value}");
        result = RecoveryPassword.Replace(result, Marker);
        return result;
    }

    /// <summary>
    /// Lists what <see cref="Redact"/> would remove, without changing the text. Used by the check the
    /// diagnostics export runs before it is written (M39-S-005).
    /// </summary>
    public static IReadOnlyList<SensitiveDataFinding> Scan(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<SensitiveDataFinding>();
        }

        var findings = new List<SensitiveDataFinding>();
        Collect(findings, PrivateKeyBlock, text, PrivateKeyRule);
        Collect(findings, CommandLinePassword, text, CommandLineRule);
        Collect(findings, KeywordAssignment, text, KeywordRule);
        Collect(findings, RecoveryPassword, text, RecoveryPasswordRule);
        findings.Sort((left, right) => left.Index.CompareTo(right.Index));
        return findings;
    }

    /// <summary>
    /// True when redaction would change the text. A caller that has to decide between writing and
    /// refusing uses this; it is the same rule as <see cref="Scan"/>, only shorter.
    /// </summary>
    public static bool ContainsSensitiveData(string? text) => Scan(text).Count > 0;

    private static void Collect(List<SensitiveDataFinding> findings, Regex pattern, string text, string rule)
    {
        foreach (Match match in pattern.Matches(text))
        {
            findings.Add(new SensitiveDataFinding
            {
                Rule = rule,
                Index = match.Index,
                // The excerpt is built from the redacted text: a finding must never carry the secret.
                Excerpt = Excerpt(Redact(match.Value)),
            });
        }
    }

    /// <summary>A short, single line excerpt for a log or a report.</summary>
    private static string Excerpt(string value)
    {
        var flattened = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            flattened.Append(char.IsControl(character) ? ' ' : character);
        }

        var text = flattened.ToString().Trim();
        return text.Length <= 60 ? text : string.Concat(text.AsSpan(0, 57), "...");
    }
}
