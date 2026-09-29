using Backlog.Modules.Roadmap.Abstractions.Services;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// Answers Roadmap's <see cref="IRoadmapCatchUp"/> with the task loop's first cycle:
/// the roadmap's first read follows a pull when this device is paired and syncing
/// (local ADR 0018, Consequences — the re-projection a load can save would otherwise
/// overwrite an edit the other PC made that morning).
/// <para>
/// Nothing about the roadmap is decided here. The plan and the pace arrive through
/// the merge like any other document; this only holds the first read back until the
/// cycle carrying them has run, and for no longer than <see cref="Timeout"/>, so a
/// device that is offline or slow draws what it holds rather than a blank roadmap.
/// </para>
/// </summary>
public sealed class TaskSyncRoadmapCatchUp(TaskSyncWorker worker) : IRoadmapCatchUp
{
    /// <summary>
    /// The longest the roadmap's first read waits for a pull.
    /// <para>
    /// A cycle against a reachable service is two requests and takes well under a
    /// second; three is room for a slow one without a person watching a roadmap that
    /// will not draw. A cycle that fails — no network — finishes long before this.
    /// </para>
    /// </summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private readonly TaskSyncWorker _worker = worker ?? throw new ArgumentNullException(nameof(worker));

    public Task CatchUpAsync(CancellationToken cancellationToken = default) =>
        _worker.CatchUpAsync(Timeout, cancellationToken);
}
