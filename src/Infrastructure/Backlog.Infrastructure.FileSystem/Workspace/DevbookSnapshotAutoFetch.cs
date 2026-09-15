using System.Collections.Concurrent;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Keeps a branch snapshot present and reasonably current without anybody
/// pressing anything.
/// <para>
/// Resolution runs on every panel load and on every Settings render, so it may
/// never <em>wait</em> on GitHub — but it may <em>ask</em>. This is where the
/// asking is rationed: the first resolve of a branch nobody has fetched starts
/// one download in the background and reports "fetching"; every resolve after
/// that, on any thread, finds the download already in flight and reports the
/// same. Once a snapshot is on disk it is served as-is, and the branch head is
/// re-read at most once per <see cref="RecheckInterval"/> — one cheap call,
/// followed by a download only when the commit moved. A failure is remembered
/// for the same interval so a repository this machine cannot reach costs one
/// request per interval rather than one per render.
/// </para>
/// <para>
/// When a fetch lands, <c>contentChanged</c> is raised so open panels re-read the
/// folder they were told was pending — the same announcement the pane's own
/// update control makes after a pull. It is raised from the background thread;
/// every subscriber already marshals, because <c>DevbookUpdateService.PullAsync</c>
/// has been raising it from one all along.
/// </para>
/// </summary>
public sealed class DevbookSnapshotAutoFetch
{
    /// <summary>How long a snapshot is trusted before the branch head is read
    /// again. Ten minutes: long enough that browsing a devbook costs GitHub one
    /// request, short enough that a push is seen within the sitting.</summary>
    public static readonly TimeSpan DefaultRecheckInterval = TimeSpan.FromMinutes(10);

    private readonly IDevbookSnapshotCache _snapshots;
    private readonly Action _contentChanged;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public DevbookSnapshotAutoFetch(
        IDevbookSnapshotCache snapshots,
        Action contentChanged,
        TimeProvider? time = null,
        TimeSpan? recheckInterval = null)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(contentChanged);

        _snapshots = snapshots;
        _contentChanged = contentChanged;
        _time = time ?? TimeProvider.System;
        RecheckInterval = recheckInterval ?? DefaultRecheckInterval;
    }

    public TimeSpan RecheckInterval { get; }

    /// <summary>
    /// Make sure this branch is being looked after, and say where that stands.
    /// <para>
    /// Returns immediately whatever the network is doing. <paramref name="hasSnapshot"/>
    /// is passed in rather than re-read because the caller has just read it and
    /// the answer decides what "fetching" means to it: with a snapshot the fetch
    /// is a refresh the reader need not know about, without one it is the thing
    /// standing between the reader and the folder.
    /// </para>
    /// </summary>
    public DevbookSnapshotFetchStatus Ensure(GitHubRepositoryRef repository, string? branch, bool hasSnapshot)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var entry = _entries.GetOrAdd(Key(repository, branch), _ => new Entry());

        lock (entry)
        {
            if (entry.InFlight is not null) return DevbookSnapshotFetchStatus.Fetching;

            var now = _time.GetUtcNow();
            if (entry.LastAttemptUtc is { } last && now - last < RecheckInterval)
            {
                return entry.Failure is null
                    ? DevbookSnapshotFetchStatus.Current
                    : DevbookSnapshotFetchStatus.Failed(entry.Failure);
            }

            entry.LastAttemptUtc = now;
            entry.InFlight = Task.Run(() => FetchAsync(entry, repository, branch, hasSnapshot));

            return DevbookSnapshotFetchStatus.Fetching;
        }
    }

    /// <summary>
    /// Drop what was remembered, so the next resolve asks GitHub again straight
    /// away. Called when the repository settings change — a token added, a branch
    /// re-pointed — because the failure being remembered may be exactly what was
    /// just fixed, and waiting out the interval to find out would look broken.
    /// </summary>
    public void Forget()
    {
        foreach (var entry in _entries.Values)
        {
            lock (entry)
            {
                // An in-flight fetch keeps its slot; it will report when it lands.
                if (entry.InFlight is null) entry.LastAttemptUtc = null;
                entry.Failure = null;
            }
        }
    }

    /// <summary>The fetch that is running for this branch, or null. For tests,
    /// which have no other way to wait on a background download.</summary>
    public Task? InFlight(GitHubRepositoryRef repository, string? branch)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (!_entries.TryGetValue(Key(repository, branch), out var entry)) return null;

        lock (entry)
        {
            return entry.InFlight;
        }
    }

    private async Task FetchAsync(Entry entry, GitHubRepositoryRef repository, string? branch, bool hadSnapshot)
    {
        string? failure = null;
        var updated = false;

        try
        {
            var result = await _snapshots.FetchAsync(repository, branch).ConfigureAwait(false);

            updated = result.Updated;

            // A message with nothing on disk is a failure the reader must see —
            // there is no folder to fall back to. A message beside an existing
            // snapshot ("has moved on", "could not reach") is not: the copy that
            // was there is still what they are reading.
            if (result.Snapshot is null && result.Message is not null) failure = result.Message;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The cache already turns everything it expects into a message; this
            // catches what it did not expect, because an unobserved fault on a
            // fire-and-forget task is a crash nobody asked for.
            failure = exception.Message;
        }

        lock (entry)
        {
            entry.InFlight = null;
            entry.Failure = failure;
            entry.LastAttemptUtc = _time.GetUtcNow();
        }

        // Announced when a tree landed, and also when the first attempt failed:
        // a panel showing "fetching" needs to hear either answer, and with no
        // snapshot behind it the failure is the answer.
        if (updated || (!hadSnapshot && failure is not null)) _contentChanged();
    }

    private static string Key(GitHubRepositoryRef repository, string? branch) =>
        $"{repository.Owner}/{repository.Name}@{(string.IsNullOrWhiteSpace(branch) ? string.Empty : branch.Trim())}";

    private sealed class Entry
    {
        public Task? InFlight { get; set; }

        public DateTimeOffset? LastAttemptUtc { get; set; }

        public string? Failure { get; set; }
    }
}

/// <summary>Where the background fetch of a branch stands, as far as a resolver
/// needs to know: nothing to wait for, a download in flight, or a failure with
/// the adapter's reason.</summary>
public sealed record DevbookSnapshotFetchStatus(bool InFlight, string? Failure)
{
    public static readonly DevbookSnapshotFetchStatus Current = new(false, null);

    public static readonly DevbookSnapshotFetchStatus Fetching = new(true, null);

    public static DevbookSnapshotFetchStatus Failed(string message) => new(false, message);
}
