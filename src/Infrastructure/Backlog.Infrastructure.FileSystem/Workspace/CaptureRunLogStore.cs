using System.Text.Json;

using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.DataTransferObjects;
using Backlog.Modules.Capture.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Keeps what past capture runs said, per source, in a JSON file next to the
/// capture sources it ran over.
/// <para>
/// A window rather than an archive: the last <see cref="MaxEntriesPerSource"/>
/// runs per source, newest first, so the file stays a few kilobytes however
/// often the button is pressed. Its own file rather than a section of
/// <c>capture-sources.json</c>, because that one is the reader's choices and
/// this one is what happened — a hand-edit of the first should never be a
/// merge with the second.
/// </para>
/// <para>
/// Recording never throws. The in-memory log is updated first, so a panel on
/// the same run shows the line even when the disk refused it; what is lost in
/// that case is the line surviving a restart, and a run is not a failed run
/// for that.
/// </para>
/// </summary>
public sealed class CaptureRunLogStore : ICaptureRunLog
{
    /// <summary>How many runs a source keeps. Fifty is a few weeks of pressing
    /// the button daily and still a file a person can read.</summary>
    public const int MaxEntriesPerSource = 50;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private readonly object _gate = new();

    /// <summary>Newest first, per kind. Only kinds a run has looked at have
    /// a key.</summary>
    private Dictionary<CaptureSourceKind, List<CaptureRunLogEntry>> _entries;

    public CaptureRunLogStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Backlog",
                "capture-runs.json"))
    {
    }

    /// <summary>Names the log file separately from the per-user location, for
    /// the reason <see cref="CaptureSourcesSettingsStore(string)"/> does.</summary>
    public CaptureRunLogStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _entries = Read();
    }

    public event Action? Changed;

    public CaptureRunLogEntry? LastRunFor(CaptureSourceKind kind)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(kind, out var entries) && entries.Count > 0 ? entries[0] : null;
        }
    }

    public IReadOnlyList<CaptureRunLogEntry> EntriesFor(CaptureSourceKind kind)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(kind, out var entries) ? [.. entries] : [];
        }
    }

    public void Record(CaptureRunResultDto run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.Sources.Count > 0)
        {
            lock (_gate)
            {
                foreach (var source in run.Sources)
                {
                    if (!_entries.TryGetValue(source.Kind, out var entries))
                    {
                        entries = [];
                        _entries[source.Kind] = entries;
                    }

                    entries.Insert(0, new CaptureRunLogEntry(run.RanAt, source.NewItems, source.Message));

                    if (entries.Count > MaxEntriesPerSource)
                    {
                        entries.RemoveRange(MaxEntriesPerSource, entries.Count - MaxEntriesPerSource);
                    }
                }

                Save();
            }
        }

        Changed?.Invoke();
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(new CaptureRunsDto
            {
                Sources = [.. _entries.Select(pair => new SourceRunsDto
                {
                    Kind = CaptureSourceKinds.Slug(pair.Key),
                    Runs = [.. pair.Value.Select(entry => new RunDto
                    {
                        RanAt = entry.RanAt,
                        NewItems = entry.NewItems,
                        Message = entry.Message
                    })]
                })]
            }, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The line is on screen for this session; only its surviving a
            // restart is lost, and that is not the run's failure to report.
        }
    }

    private Dictionary<CaptureSourceKind, List<CaptureRunLogEntry>> Read()
    {
        try
        {
            if (!File.Exists(_path)) return [];

            var dto = JsonSerializer.Deserialize<CaptureRunsDto>(File.ReadAllText(_path), JsonOptions);
            if (dto is null) return [];

            var entries = new Dictionary<CaptureSourceKind, List<CaptureRunLogEntry>>();

            foreach (var source in dto.Sources)
            {
                // A kind nobody can read is dropped rather than guessed at.
                if (!CaptureSourceKinds.TryParse(source.Kind, out var kind)) continue;
                if (entries.ContainsKey(kind)) continue;

                entries[kind] = [.. source.Runs
                    .Select(run => new CaptureRunLogEntry(run.RanAt, run.NewItems, run.Message ?? string.Empty))
                    .Take(MaxEntriesPerSource)];
            }

            return entries;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreachable log must never stop the app from
            // opening — fall back to nothing remembered.
            return [];
        }
    }

    private sealed class CaptureRunsDto
    {
        public List<SourceRunsDto> Sources { get; init; } = [];
    }

    private sealed class SourceRunsDto
    {
        public string Kind { get; init; } = string.Empty;

        public List<RunDto> Runs { get; init; } = [];
    }

    private sealed class RunDto
    {
        public DateTimeOffset RanAt { get; init; }

        public int NewItems { get; init; }

        public string? Message { get; init; }
    }
}
