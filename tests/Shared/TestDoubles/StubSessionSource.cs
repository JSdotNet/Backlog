using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Tests;

/// <summary>
/// A session list that answers the sessions it holds, every reading, whatever the
/// query asks: none when it was given none. A test changes what the next reading
/// answers by setting <see cref="Sessions"/>.
/// </summary>
/// <param name="sessions">What every reading answers until a test sets another list.</param>
internal sealed class StubSessionSource(params IReadOnlyList<AgentSession> sessions) : IAgentSessionSource
{
    /// <summary>What the next reading answers.</summary>
    public IReadOnlyList<AgentSession> Sessions { get; set; } = sessions;

    /// <summary>The agents whose folders every reading says it could not read.</summary>
    public IReadOnlyList<string> Unreadable { get; set; } = [];

    /// <summary>How many sessions every reading says it found; the sessions held when
    /// unset, so a reading reads as uncapped.</summary>
    public int? Discovered { get; init; }

    /// <summary>How many times the list was read.</summary>
    public int Reads { get; private set; }

    /// <summary>What each reading asked for, in order; the inventory's
    /// <see cref="AgentSessionQuery.Newest"/> for a reading that named none.</summary>
    public List<AgentSessionQuery> Queries { get; } = [];

    public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
        GetSessionsAsync(AgentSessionQuery.Newest, cancellationToken);

    public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default)
    {
        // A pane may read from the pool while another reading is out.
        lock (Queries)
        {
            Reads++;
            Queries.Add(query);
        }

        var held = Sessions;
        return Task.FromResult(new AgentSessionCatalog(held, Unreadable, Discovered ?? held.Count));
    }

    /// <summary>A local Claude session; with <paramref name="desktopId"/> it has a desktop
    /// record, archived when <paramref name="archived"/>.</summary>
    public static AgentSession Claude(string id, string? desktopId, bool archived = false, AgentSessionState state = AgentSessionState.Finished) =>
        new AgentSession(
            id,
            AgentSessionKind.Claude,
            "env-1",
            "PC",
            "A session",
            @"D:\Repos\Backlog",
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            state,
            null,
            AgentSessionOrigin.Local)
        {
            DesktopSessionId = desktopId,
            Archived = desktopId is null ? null : archived
        };
}
