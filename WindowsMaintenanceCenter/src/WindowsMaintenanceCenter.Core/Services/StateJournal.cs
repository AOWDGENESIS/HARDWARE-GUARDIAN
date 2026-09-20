using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Core.Services;

/// <summary>
/// One state change as it is written down (spec section 40, M34-F-001/M34-F-002).
///
/// The entry carries more than from/to: the operation it belongs to and - when there is one - the
/// registered action. That is what makes the difference between "the application was in EXECUTING"
/// and "the application was executing action X of operation Y", and only the second form answers
/// M35-F-001 (the last action has to be recognised) after a crash.
///
/// Every entry is also <b>sealed</b>: it carries its position in the journal and the hash of its
/// predecessor, and a hash over its own content including that predecessor hash. A journal that was
/// changed after the fact - a line edited, a line deleted, a line inserted - no longer forms a
/// closed chain, and <see cref="IStateJournal.Verify"/> says so. Appendix SEC-12 of the security
/// matrix asks exactly for that: manipulation of the record files has to be recognisable, and a
/// record file that nobody can check is worth nothing as evidence.
/// </summary>
public sealed record StateJournalEntry
{
    /// <summary>Position in the journal, starting at 1. The chain is checked against it.</summary>
    public long Sequence { get; init; }

    public SystemState From { get; init; }

    public SystemState To { get; init; }

    public DateTimeOffset At { get; init; }

    public string? Reason { get; init; }

    /// <summary>Operation this change belongs to, or null when no operation has begun.</summary>
    public string? OperationId { get; init; }

    /// <summary>Registered action that was running, when the operation names one.</summary>
    public string? ActionId { get; init; }

    /// <summary>Hash of the entry before this one; null for the first entry.</summary>
    public string? PreviousHash { get; init; }

    /// <summary>Hash over this entry, including <see cref="PreviousHash"/>.</summary>
    public string? Hash { get; init; }

    /// <summary>True for the states a crash can interrupt in the middle (spec section 41).</summary>
    public static bool IsInterruptible(SystemState state) => state is
        SystemState.Backup or SystemState.Executing or SystemState.Validating or SystemState.Rollback or SystemState.Recovering;

    /// <summary>
    /// Seals the entry: it gets its position, the predecessor hash and its own hash. Everything that
    /// is hashed is turned into one canonical line first, so the hash does not depend on formatting,
    /// culture or property order.
    /// </summary>
    public StateJournalEntry Sealed(long sequence, string? previousHash)
    {
        var draft = this with { Sequence = sequence, PreviousHash = previousHash, Hash = null };
        return draft with { Hash = draft.ComputeHash() };
    }

    /// <summary>Hash over the canonical form of this entry, predecessor hash included.</summary>
    public string ComputeHash()
    {
        var canonical = string.Join(
            '\u001f',
            Sequence.ToString(CultureInfo.InvariantCulture),
            At.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            From.ToString(),
            To.ToString(),
            Reason ?? string.Empty,
            OperationId ?? string.Empty,
            ActionId ?? string.Empty,
            PreviousHash ?? string.Empty);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

/// <summary>The answer to "is this journal still the one that was written?" (SEC-12).</summary>
public sealed record StateJournalVerification
{
    /// <summary>True only when every entry is present, unchanged and correctly chained.</summary>
    public bool IsIntact { get; init; } = true;

    /// <summary>How many entries were checked.</summary>
    public int Checked { get; init; }

    /// <summary>Zero based position of the first entry that does not fit, if there is one.</summary>
    public int? FirstBrokenIndex { get; init; }

    /// <summary>Localisation key of the finding; <c>StateJournal_Chain_Intact</c> when nothing is wrong.</summary>
    public LocalizedText Summary { get; init; } = LocalizedText.Of("StateJournal_Chain_Intact");

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    public static StateJournalVerification Broken(int index, int checkedCount, string key, string evidence) => new()
    {
        IsIntact = false,
        Checked = checkedCount,
        FirstBrokenIndex = index,
        Summary = LocalizedText.Of(key),
        Evidence = new[] { evidence },
    };
}

/// <summary>
/// Where the state machine writes its changes down (M34-F-001: every state is stored).
///
/// The journal is append only and is read back after a restart: the last entry tells whether the
/// previous run ended in a finished state or was cut off in the middle. Without this file an
/// interrupted job is invisible, and an invisible job is one that nobody can recover - which is
/// exactly the defect M34-R-001 names.
/// </summary>
public interface IStateJournal
{
    /// <summary>Where the entries are kept, for the report and the protocol.</summary>
    string Location { get; }

    /// <summary>Appends one entry. A failure to write must not be swallowed silently by the caller.</summary>
    void Record(StateJournalEntry entry);

    /// <summary>All entries in the order they were written.</summary>
    IReadOnlyList<StateJournalEntry> Read();

    /// <summary>
    /// Checks the chain of the journal as it is now (SEC-12). A journal whose chain is broken is
    /// still read - the run has to continue - but it is never presented as intact.
    /// </summary>
    StateJournalVerification Verify();
}

/// <summary>
/// The chain check itself, shared by both journals so that memory and file answer identically.
/// </summary>
public static class StateJournalChain
{
    public static StateJournalVerification Verify(IReadOnlyList<StateJournalEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        string? previousHash = null;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];

            if (entry.Sequence != index + 1)
            {
                return StateJournalVerification.Broken(
                    index,
                    entries.Count,
                    "StateJournal_Chain_SequenceGap",
                    $"index={index}, expected sequence={index + 1}, found={entry.Sequence}");
            }

            if (string.IsNullOrWhiteSpace(entry.Hash))
            {
                return StateJournalVerification.Broken(
                    index,
                    entries.Count,
                    "StateJournal_Chain_MissingHash",
                    $"index={index}, sequence={entry.Sequence}, state={entry.To}");
            }

            if (!string.Equals(entry.PreviousHash, previousHash, StringComparison.Ordinal))
            {
                return StateJournalVerification.Broken(
                    index,
                    entries.Count,
                    "StateJournal_Chain_Broken",
                    $"index={index}, sequence={entry.Sequence}, the predecessor hash does not match");
            }

            var computed = entry.ComputeHash();
            if (!string.Equals(computed, entry.Hash, StringComparison.Ordinal))
            {
                return StateJournalVerification.Broken(
                    index,
                    entries.Count,
                    "StateJournal_Chain_HashMismatch",
                    $"index={index}, sequence={entry.Sequence}, stored={entry.Hash}, computed={computed}");
            }

            previousHash = entry.Hash;
        }

        return new StateJournalVerification
        {
            IsIntact = true,
            Checked = entries.Count,
            Summary = LocalizedText.Of("StateJournal_Chain_Intact"),
            Evidence = new[] { $"entries={entries.Count}, last hash={(previousHash is null ? "none" : previousHash[..12])}" },
        };
    }
}

/// <summary>
/// Journal that only lives in memory: the default for tests and for a run that deliberately keeps
/// nothing (simulation). It is never a substitute for the file journal in the application.
/// </summary>
public sealed class InMemoryStateJournal : IStateJournal
{
    private readonly List<StateJournalEntry> _entries = new();
    private readonly object _gate = new();

    public string Location => "in memory";

    public void Record(StateJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            _entries.Add(Seal(entry));
        }
    }

    public IReadOnlyList<StateJournalEntry> Read()
    {
        lock (_gate)
        {
            return _entries.ToList();
        }
    }

    public StateJournalVerification Verify()
    {
        lock (_gate)
        {
            return StateJournalChain.Verify(_entries.ToList());
        }
    }

    /// <summary>Seals against the current last entry; the caller already holds the lock.</summary>
    private StateJournalEntry Seal(StateJournalEntry entry)
    {
        var last = _entries.Count == 0 ? null : _entries[^1];
        return entry.Sealed((last?.Sequence ?? 0) + 1, last?.Hash);
    }
}
