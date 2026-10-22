using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.FileSystem.Tasks;

/// <summary>
/// What the backlog asks of the contexts beside it, answered by adapters that may see
/// both sides: the state of the agent sessions a task links to
/// (<see cref="ILinkedSessionStates"/>), read from the Sessions context's record, and the Start session actions (<see cref="IEntrySessionRequests"/>, <see cref="IPlanSessionRequests"/>), over its request queue.
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

        // The Start session and Start plan session actions, over the Sessions
        // context's request queue (local ADR 0037). Only when the host composed the
        // queue: without one nothing is registered and neither action is offered. One
        // instance answers both ports, a singleton over the singleton store.
        // Without a queue both ports answer with one that is never offered, the way
        // the session states above answer with none.
        services.AddSingleton<SessionRequestActions>(sp =>
            sp.GetService<ISessionRequestStore>() is { } queue
                ? new SessionRequestActions(queue, sp.GetService<IAppFeatureSettings>(), sp.GetService<TimeProvider>())
                : SessionRequestActions.NotOffered);
        services.AddSingleton<IEntrySessionRequests>(sp => sp.GetRequiredService<SessionRequestActions>());
        services.AddSingleton<IPlanSessionRequests>(sp => sp.GetRequiredService<SessionRequestActions>());

        return services;
    }

    private sealed class NoLinkedSessionStates : ILinkedSessionStates
    {
        public static readonly NoLinkedSessionStates Instance = new();

        public Task<IReadOnlyDictionary<string, LinkedSessionState>> StatesOfAsync(
            IReadOnlyCollection<string> sessionIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, LinkedSessionState>>(new Dictionary<string, LinkedSessionState>());
    }
}
