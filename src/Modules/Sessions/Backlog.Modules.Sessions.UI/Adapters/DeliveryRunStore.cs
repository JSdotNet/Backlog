using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UI.Adapters;

/// <summary>
/// The run files this product writes, in the layout
/// <see cref="DeliveryRunReader"/> reads: <c>&lt;home&gt;/&lt;dashboard&gt;/&lt;worktree
/// key&gt;/runs/&lt;id&gt;.json</c>.
/// <para>
/// The counterpart of the reader, and the narrower of the two on purpose. The reader
/// crosses every dashboard folder on the profile because it is reporting what other
/// tools left behind; this one writes into exactly one folder —
/// <see cref="DeliveryRunReader.BacklogDashboard"/> — because a second writer in another
/// tool's folder is how two processes come to edit one file with nothing arbitrating
/// between them.
/// </para>
/// <para>
/// Documents are carried as <see cref="JsonObject"/> rather than mapped to a record and
/// back. A run file has fields this product never sets — the collector's telemetry, the
/// handoff marker, whatever a later generation adds — and a read-modify-write through a
/// typed model would silently drop every one of them on the next update. The node model
/// changes what it was asked to change and leaves the rest exactly as it found it.
/// </para>
/// </summary>
internal sealed class DeliveryRunStore
{
    private static readonly JsonSerializerOptions Layout = new() { WriteIndented = true };

    /// <summary>One gate per run file, shared by every store in the process — see
    /// <see cref="LockAsync"/>.</summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _home;

    internal DeliveryRunStore(string home)
    {
        _home = home;
    }

    /// <summary>The folder one worktree's runs are filed in, whether or not it exists
    /// yet.</summary>
    internal string FolderFor(string worktree)
    {
        var key = DeliveryRunWorktrees.KeyOf(worktree)
            ?? throw new ArgumentException("A run has to be about a folder.", nameof(worktree));

        return Path.Combine(_home, DeliveryRunReader.BacklogDashboard, key, "runs");
    }

    /// <summary>
    /// Holds one run file for a read-modify-write, until the returned handle is
    /// disposed.
    /// <para>
    /// Two writers share these files inside one process: the lifecycle operations a flow
    /// calls, and the hook telemetry arriving beside them — often for the same run in
    /// the same second, since a flow's <c>update_stage</c> is itself a tool call the
    /// hooks report on. Each reads the whole document, changes its part and writes the
    /// whole document back, so without a gate the later write silently drops the
    /// earlier one's change. Keyed by path and static, because the two writers each
    /// own a store of their own.
    /// </para>
    /// </summary>
    internal async Task<IDisposable> LockAsync(string worktree, string runId, CancellationToken cancellationToken)
    {
        var gate = Gates.GetOrAdd(PathFor(worktree, runId), _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        return new Release(gate);
    }

    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
        }
    }

    /// <summary>One run's file, whether or not it exists.</summary>
    internal string PathFor(string worktree, string runId) =>
        Path.Combine(FolderFor(worktree), $"{runId}.json");

    /// <summary>One run as it stands, or null where there is no such file or it is not
    /// a JSON object. Null rather than a throw for the reason the reader gives: the
    /// file may be being written by another process as this one reads it.</summary>
    internal async Task<JsonObject?> ReadAsync(string worktree, string runId, CancellationToken cancellationToken)
    {
        var path = PathFor(worktree, runId);

        if (!File.Exists(path)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

            return JsonNode.Parse(json) as JsonObject;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Every run filed under one worktree, each with the id its file is named
    /// for. Unreadable files are left out on the reader's terms — one lost run rather
    /// than a failed listing.</summary>
    internal async Task<IReadOnlyList<JsonObject>> ReadAllAsync(string worktree, CancellationToken cancellationToken)
    {
        var folder = new DirectoryInfo(FolderFor(worktree));

        if (!folder.Exists) return [];

        FileInfo[] files;

        try
        {
            files = folder.GetFiles("*.json");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var read = new List<JsonObject>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var id = Path.GetFileNameWithoutExtension(file.Name);

            if (await ReadAsync(worktree, id, cancellationToken).ConfigureAwait(false) is { } run) read.Add(run);
        }

        return read;
    }

    /// <summary>
    /// Write one run, so that nothing ever reads half of it.
    /// <para>
    /// The temp-file-then-move that <c>DeviceIdentityStore</c> spells out for the
    /// device identity, for a sharper version of the same reason: writing in place
    /// truncates first, and the pane re-reads these files on every refresh while a
    /// live run is updating them several times a minute. The reader survives a
    /// half-written file by dropping that run, so the failure would not be a crash —
    /// it would be a row that flickers out of the list and back, which is worse for
    /// being plausible.
    /// </para>
    /// </summary>
    internal async Task WriteAsync(string worktree, string runId, JsonObject run, CancellationToken cancellationToken)
    {
        var path = PathFor(worktree, runId);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temp = $"{path}.{Guid.NewGuid():n}.tmp";
        var written = false;

        try
        {
            await File.WriteAllTextAsync(temp, run.ToJsonString(Layout), cancellationToken).ConfigureAwait(false);

            File.Move(temp, path, overwrite: true);
            written = true;
        }
        finally
        {
            if (!written)
            {
                try
                {
                    File.Delete(temp);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // A leftover temp file is litter, not a failure: it is named for a
                    // Guid, so it collides with nothing, and the reader only looks at
                    // *.json.
                }
            }
        }
    }
}
