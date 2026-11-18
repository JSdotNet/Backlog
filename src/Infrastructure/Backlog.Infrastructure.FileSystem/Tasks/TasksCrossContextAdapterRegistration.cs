using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.FileSystem.Tasks;

/// <summary>
/// What the backlog asks of the contexts beside it, answered by adapters that may see
/// both sides: the state of the agent sessions a task links to
/// (<see cref="ILinkedSessionStates"/>), read from the Sessions context's record; the state
/// of the pull requests a task links to and the row's merge acts
/// (<see cref="ILinkedPullRequestStates"/>), through Repository Management's contract
/// (local ADR 0042 §4); and the queue icons that queue an entry session or a plan session
/// (<see cref="IEntrySessionRequests"/>, <see cref="IPlanSessionRequests"/>), over its
/// request queue.
/// </summary>
public static class TasksCrossContextAdapterRegistration
{
    /// <summary>
    /// Registers the backlog's cross-context adapters. A singleton over the session
    /// source, resolved when first asked for so <c>AddAgentSessionSource</c> may come
    /// after this call; a host that composed no session source registers nothing it
    /// can answer with, and the backlog's badges draw without a state.
    /// </summary>
    public static IServiceCollection AddTasksCrossContextAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ILinkedSessionStates>(sp =>
            sp.GetService<IAgentSessionSource>() is { } sessions
                ? new LinkedSessionStates(sessions)
                : NoLinkedSessionStates.Instance);

        // The pull requests a task links to, through Repository Management's one
        // contract, so the task row shares its cache with the Pull requests pane,
        // Progress and the side panel. A host that composed no contract answers with
        // nothing, the way the session states above do.
        services.AddSingleton<ILinkedPullRequestStates>(sp =>
            sp.GetService<IPullRequests>() is { } pullRequests
                ? new LinkedPullRequestStates(pullRequests)
                : NoLinkedPullRequestStates.Instance);

        // The queue icons that queue an entry session and queue a plan session, over
        // the Sessions context's request queue (local ADR 0038). Only when the host composed
        // the queue: without one nothing is registered and neither icon is offered. One
        // instance answers both ports, a singleton over the singleton store.
        // Without a queue both ports answer with one that is never offered, the way
        // the session states above answer with none.
        services.AddSingleton<SessionRequestActions>(sp =>
            sp.GetService<ISessionRequestStore>() is { } queue
                ? new SessionRequestActions(queue, sp.GetService<IAppFeatureSettings>(), sp.GetService<TimeProvider>(), sp.GetService<DispatcherContact>())
                : SessionRequestActions.NotOffered);
        services.AddSingleton<IEntrySessionRequests>(sp => sp.GetRequiredService<SessionRequestActions>());
        services.AddSingleton<IPlanSessionRequests>(sp => sp.GetRequiredService<SessionRequestActions>());

        return services;
    }

    private sealed class NoLinkedSessionStates : ILinkedSessionStates
    {
        public static readonly NoLinkedSessionStates Instance = new();

        public Task<IReadOnlyDictionary<string, LinkedSession>> StatesOfAsync(
            IReadOnlyCollection<string> sessionIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, LinkedSession>>(new Dictionary<string, LinkedSession>());
    }

    /// <summary>No contract composed: nothing read, nothing kept, and an act names why.</summary>
    private sealed class NoLinkedPullRequestStates : ILinkedPullRequestStates
    {
        public static readonly NoLinkedPullRequestStates Instance = new();

        public event Action<IReadOnlyCollection<LinkedPullRequestKey>>? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyDictionary<LinkedPullRequestKey, LinkedPullRequest> Kept(IReadOnlyCollection<LinkedPullRequestKey> pullRequests) =>
            new Dictionary<LinkedPullRequestKey, LinkedPullRequest>();

        public Task<LinkedPullRequestRead> StatesOfAsync(
            IReadOnlyCollection<LinkedPullRequestKey> pullRequests,
            bool fresh = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new LinkedPullRequestRead(
                new Dictionary<LinkedPullRequestKey, LinkedPullRequest>(),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)));

        public Task<Result> MergeNowAsync(LinkedPullRequestKey pullRequest, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Failure(PullRequestErrors.NotConfigured));

        public Task<Result> SetAutoMergeAsync(LinkedPullRequestKey pullRequest, bool enable, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Failure(PullRequestErrors.NotConfigured));
    }
}
