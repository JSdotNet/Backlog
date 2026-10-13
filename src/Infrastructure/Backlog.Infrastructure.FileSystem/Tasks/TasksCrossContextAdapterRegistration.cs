using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.FileSystem.Tasks;

/// <summary>
/// What the backlog asks of the contexts beside it, answered by adapters that may see
/// both sides: the state of the agent sessions a task links to
/// (<see cref="ILinkedSessionStates"/>), read from the Sessions context's record, and
/// the inbox items a repository rename carries across
/// (<see cref="IInboxRepositoryRename"/>), asked of the Inbox's own port.
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

        // Scoped like the Inbox port it passes the call to, so a head that holds the
        // Inbox per window holds this per window too.
        services.AddScoped<IInboxRepositoryRename, InboxRepositoryRename>();

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
