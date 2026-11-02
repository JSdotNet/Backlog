using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Modules.RepositoryManagement.Features.ListMergedPullRequests;
using Backlog.Modules.RepositoryManagement.Features.ListOpenPullRequests;
using Backlog.Modules.RepositoryManagement.Features.MarkReady;
using Backlog.Modules.RepositoryManagement.Features.Merge;
using Backlog.Modules.RepositoryManagement.Features.ReadPullRequests;
using Backlog.Modules.RepositoryManagement.Features.RerunFailed;
using Backlog.Modules.RepositoryManagement.Features.SetAutoMerge;
using Backlog.Modules.RepositoryManagement.Features.UpdateBranch;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Modules.RepositoryManagement.Services;

/// <summary>
/// <see cref="IPullRequests"/> over the module's slices and its one cache (local ADR
/// 0042). One instance per process, so every reader the host composes shares the cache:
/// the pane, the session card, a task's row and side panel, and Progress.
/// </summary>
internal sealed partial class PullRequests : IPullRequests
{
    private readonly ILogger _logger;
    private readonly IRegisteredRepositories _registry;
    private readonly ListOpenPullRequestsQueryHandler _listOpen;
    private readonly ListMergedPullRequestsQueryHandler _listMerged;
    private readonly ReadPullRequestsQueryHandler _read;
    private readonly PullRequestCache _cache;
    private readonly MergeCommandHandler _merge;
    private readonly SetAutoMergeCommandHandler _autoMerge;
    private readonly UpdateBranchCommandHandler _updateBranch;
    private readonly MarkReadyCommandHandler _markReady;
    private readonly RerunFailedCommandHandler _rerunFailed;

    public PullRequests(
        IPullRequestSource source,
        IRegisteredRepositories registry,
        ISettledPullRequestStore settled,
        TimeProvider clock,
        ILogger<PullRequests>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(settled);
        ArgumentNullException.ThrowIfNull(clock);

        _logger = logger ?? (ILogger)NullLogger.Instance;
        _registry = registry;
        _cache = new PullRequestCache(source, registry, settled, clock, logger);
        _listOpen = new ListOpenPullRequestsQueryHandler(_cache, registry);
        _listMerged = new ListMergedPullRequestsQueryHandler(_cache, registry);
        _read = new ReadPullRequestsQueryHandler(_cache);

        var acts = new PullRequestActs(_cache, registry, Raise);
        _merge = new MergeCommandHandler(acts, source);
        _autoMerge = new SetAutoMergeCommandHandler(acts, source);
        _updateBranch = new UpdateBranchCommandHandler(acts, source);
        _markReady = new MarkReadyCommandHandler(acts, source);
        _rerunFailed = new RerunFailedCommandHandler(acts, source);
    }

    public bool IsConfigured => _registry.IsConfigured;

    public event Action<PullRequestsChanged>? Changed;

    public Task<PullRequestListing> ListOpenAsync(
        IReadOnlyCollection<string>? repositories = null,
        PullRequestReadOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _listOpen.Handle(new ListOpenPullRequestsQuery(repositories, MaxAge(options)), cancellationToken);

    public Task<MergedPullRequestListing> ListMergedAsync(
        IReadOnlyCollection<string>? repositories = null,
        PullRequestReadOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _listMerged.Handle(new ListMergedPullRequestsQuery(repositories, MaxAge(options)), cancellationToken);

    public Task<PullRequestListing> ReadAsync(
        IReadOnlyCollection<PullRequestReference> pullRequests,
        PullRequestReadOptions? options = null,
        CancellationToken cancellationToken = default) =>
        _read.Handle(new ReadPullRequestsQuery(pullRequests, MaxAge(options)), cancellationToken);

    public PullRequestListing Kept(IReadOnlyCollection<PullRequestReference> pullRequests) => _cache.Kept(pullRequests);

    public Task<Result> MergeAsync(PullRequestReference pullRequest, CancellationToken cancellationToken = default) =>
        _merge.Handle(new MergeCommand(pullRequest), cancellationToken);

    public Task<Result> SetAutoMergeAsync(PullRequestReference pullRequest, bool enable, CancellationToken cancellationToken = default) =>
        _autoMerge.Handle(new SetAutoMergeCommand(pullRequest, enable), cancellationToken);

    public Task<Result> UpdateBranchAsync(PullRequestReference pullRequest, CancellationToken cancellationToken = default) =>
        _updateBranch.Handle(new UpdateBranchCommand(pullRequest), cancellationToken);

    public Task<Result> MarkReadyForReviewAsync(PullRequestReference pullRequest, CancellationToken cancellationToken = default) =>
        _markReady.Handle(new MarkReadyCommand(pullRequest), cancellationToken);

    public Task<Result> RerunFailedAsync(PullRequestReference pullRequest, CancellationToken cancellationToken = default) =>
        _rerunFailed.Handle(new RerunFailedCommand(pullRequest), cancellationToken);

    private static TimeSpan MaxAge(PullRequestReadOptions? options) => (options ?? PullRequestReadOptions.Default).MaxAge;

    /// <summary>Tells each reader in turn, each on its own: a reader that cannot redraw
    /// neither turns an act GitHub took into a failure for the caller nor keeps the
    /// readers after it from hearing.</summary>
    private void Raise(PullRequestsChanged changed)
    {
        if (Changed is not { } handlers) return;

        foreach (var handler in handlers.GetInvocationList().Cast<Action<PullRequestsChanged>>())
        {
            try
            {
                handler(changed);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogReaderFailed(_logger, exception, string.Join(", ", changed.PullRequests.Select(pull => pull.Key)));
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pull requests: a reader failed on the change to {PullRequests}")]
    private static partial void LogReaderFailed(ILogger logger, Exception exception, string pullRequests);
}
