using System.Text.Json;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Services;
using WindowsMaintenanceCenter.Infrastructure.Serialization;

namespace WindowsMaintenanceCenter.Infrastructure.Persistence;

/// <summary>
/// Append-only state journal as JSON lines (spec section 40, M34-F-001).
///
/// Why a file and not only memory: the state machine exists to make an interrupted run recognisable
/// after a restart (M34-R-001). That only works if the changes survive the process, so every
/// transition is appended to disk while the state changes - not at shutdown, which a crash never
/// reaches.
///
/// Why JSON lines: a crash can truncate the last line. Every earlier line stays readable, so the
/// journal still shows what happened up to the interruption instead of becoming an unreadable
/// document at the worst possible moment.
///
/// The file lives in the history directory, which follows the portable switch: portable keeps its
/// data next to the executable, an installed copy below the program data directory (chapter 82).
/// </summary>
public sealed class FileStateJournal : IStateJournal
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _filePath;

    // Tail of the chain as it was last seen, so appending does not mean reading the whole file again.
    // It is filled on first use and after every write.
    private long _lastSequence;
    private string? _lastHash;
    private bool _tailLoaded;

    public FileStateJournal(IPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _filePath = Path.Combine(paths.HistoryDirectory, "state-journal.jsonl");
    }

    public string Location => _filePath;

    public void Record(StateJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _gate.Wait();
        try
        {
            // The entry is sealed inside the lock: sequence number and predecessor hash come from the
            // last entry on disk, so two writers can never produce two entries with the same position.
            if (!_tailLoaded)
            {
                var existing = ReadCore();
                _lastSequence = existing.Count == 0 ? 0 : existing[^1].Sequence;
                _lastHash = existing.Count == 0 ? null : existing[^1].Hash;
                _tailLoaded = true;
            }

            var sealedEntry = entry.Sealed(_lastSequence + 1, _lastHash);
            var line = JsonSerializer.Serialize(sealedEntry, JsonOptions.Compact);

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Append and flush before returning: the entry has to be on disk before the caller may
            // believe the state was stored.
            using var stream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read, 8 * 1024);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(line);
            writer.Flush();
            stream.Flush(flushToDisk: true);

            _lastSequence = sealedEntry.Sequence;
            _lastHash = sealedEntry.Hash;
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<StateJournalEntry> Read()
    {
        _gate.Wait();
        try
        {
            return ReadCore();
        }
        finally
        {
            _gate.Release();
        }
    }

    public StateJournalVerification Verify() => StateJournalChain.Verify(Read());

    /// <summary>Reads the file; the caller already holds the lock.</summary>
    private List<StateJournalEntry> ReadCore()
    {
        var entries = new List<StateJournalEntry>();
        if (!File.Exists(_filePath))
        {
            return entries;
        }

        foreach (var line in File.ReadLines(_filePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var entry = JsonSerializer.Deserialize<StateJournalEntry>(line, JsonOptions.Compact);
                    if (entry is not null)
                    {
                        entries.Add(entry);
                    }
                }
                catch (JsonException)
                {
                    // A truncated last line (crash during the write) is expected with this format. It is
                    // skipped, and everything before it stays usable - which is the point of the format.
                    // A *middle* line that does not parse is skipped as well, because the run has to
                    // continue; it cannot go unnoticed any more, though: the chain check
                    // (StateJournalChain.Verify, SEC-12) reports the break, and the caller has to show
                    // that finding instead of presenting a shortened journal as the whole truth.
                    continue;
                }
            }
        }

        return entries;
    }
}
