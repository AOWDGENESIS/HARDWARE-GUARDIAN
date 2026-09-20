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

    public FileStateJournal(IPathProvider paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _filePath = Path.Combine(paths.HistoryDirectory, "state-journal.jsonl");
    }

    public string Location => _filePath;

    public void Record(StateJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var line = JsonSerializer.Serialize(entry, JsonOptions.Compact);

        _gate.Wait();
        try
        {
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
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<StateJournalEntry> Read()
    {
        var entries = new List<StateJournalEntry>();
        if (!File.Exists(_filePath))
        {
            return entries;
        }

        _gate.Wait();
        try
        {
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
                    // A *middle* line that does not parse would also be skipped here; the journal is
                    // append-only, so a corrupt middle line means somebody changed the file, and the
                    // recovery then simply finds fewer entries and reports what it found.
                    continue;
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        return entries;
    }
}
