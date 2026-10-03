using System.Text.Json;
using System.Text.Json.Serialization;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// The latest devbook sync verdict per chapter, kept in one file beside the run files
/// this product writes: <c>&lt;home&gt;/backlog/sync-verdicts.json</c>.
/// <para>
/// Beside the runs because a verdict arrives on one — <c>finish_run</c> carries it — and
/// the folder is already this product's own. A file rather than a folder of them, and
/// not a subfolder: the reader lists the folders under <c>backlog/</c> as worktrees, and
/// a file there is one it never opens.
/// </para>
/// <para>
/// Read on every question rather than held, and only re-parsed when the file's write
/// time moved: a pane asks once per chapter it renders, and the file is written a few
/// times a week by sweeps another process may have recorded.
/// </para>
/// </summary>
internal sealed class DevbookSyncVerdictStore : IDevbookSyncVerdicts
{
    private static readonly JsonSerializerOptions Layout = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _cacheLock = new();
    private (DateTime Written, IReadOnlyList<DevbookSyncVerdict> Verdicts)? _cache;

    internal DevbookSyncVerdictStore(string home)
    {
        _path = Path.Combine(home, DeliveryRunReader.BacklogDashboard, "sync-verdicts.json");
    }

    public event Action? Changed;

    internal string FilePath => _path;

    public IReadOnlyList<DevbookSyncVerdict> For(
        string repository,
        string chapterPath,
        IReadOnlyList<DevbookFolderSetting>? folders = null)
    {
        if (string.IsNullOrWhiteSpace(repository) || string.IsNullOrWhiteSpace(chapterPath)) return [];

        var path = DevbookChapterKey.Canonical(chapterPath, folders);

        return
        [
            .. Load().Where(verdict =>
                string.Equals(verdict.Repository, repository.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(verdict.ChapterPath, path, StringComparison.OrdinalIgnoreCase))
        ];
    }

    public async Task RecordAsync(IReadOnlyList<DevbookSyncVerdict> verdicts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verdicts);

        if (verdicts.Count == 0) return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Latest wins per chapter, and a run replaces only the chapters it names:
            // the incoming verdicts' keys are dropped from what is stored, then the
            // incoming verdicts go in. Within one run a chapter named twice keeps its
            // last row, the order the sweep wrote them.
            var incoming = verdicts
                .GroupBy(Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();

            var replaced = incoming.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var kept = Load().Where(verdict => !replaced.Contains(Key(verdict)));

            await WriteAsync([.. kept, .. incoming], cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke();
    }

    private static string Key(DevbookSyncVerdict verdict) =>
        $"{verdict.Repository.Trim()}|{verdict.ChapterPath}#{verdict.Anchor}";

    private IReadOnlyList<DevbookSyncVerdict> Load()
    {
        var file = new FileInfo(_path);

        if (!file.Exists) return [];

        lock (_cacheLock)
        {
            if (_cache is { } cached && cached.Written == file.LastWriteTimeUtc) return cached.Verdicts;
        }

        IReadOnlyList<DevbookSyncVerdict> verdicts;

        try
        {
            verdicts = JsonSerializer.Deserialize<List<DevbookSyncVerdict>>(File.ReadAllText(_path), Layout) ?? [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable is shown as unknown, never as a failure of the pane asking:
            // a verdict is a decoration on a chapter, and a chapter reads without it.
            return [];
        }

        lock (_cacheLock)
        {
            _cache = (file.LastWriteTimeUtc, verdicts);
        }

        return verdicts;
    }

    /// <summary>Temp file then move, for the reason <see cref="DeliveryRunStore"/>
    /// gives: a pane may be reading the file while a run records into it.</summary>
    private async Task WriteAsync(IReadOnlyList<DevbookSyncVerdict> verdicts, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        var temp = $"{_path}.{Guid.NewGuid():n}.tmp";

        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(verdicts, Layout), cancellationToken).ConfigureAwait(false);
            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // Litter named for a Guid, and the store reads one file by name.
                }
            }
        }

        lock (_cacheLock)
        {
            _cache = null;
        }
    }
}
