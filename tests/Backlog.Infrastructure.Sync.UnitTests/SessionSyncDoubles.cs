using Backlog.Infrastructure.Sync.Sessions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The session-sync progress port with no file behind it, recording every state it
/// was handed. The order of the saves is the assertion in more than one test — a
/// cursor saved after each page rather than once at the end is the whole difference
/// between a pull that resumes and one that restarts, and a watermark that advanced
/// after a failed batch is a record lost for good.
/// </summary>
internal sealed class InMemorySessionSyncStateStore : ISessionSyncStateStore
{
    public InMemorySessionSyncStateStore(SessionSyncState? initial = null) =>
        Current = initial ?? new SessionSyncState(DateTimeOffset.MinValue, null);

    public event Action? Changed;

    public SessionSyncState Current { get; private set; }

    public List<SessionSyncState> Saved { get; } = [];

    public string StorePath => "in memory";

    public void Save(SessionSyncState state)
    {
        Current = state;
        Saved.Add(state);
        Changed?.Invoke();
    }
}

/// <summary>
/// The replicated-record cache with no file behind it. It keeps every page it was
/// handed rather than merging them, because what a test asserts here is what the
/// exchange decided to keep — the merge is <c>FileReplicatedSessionStore.Merge</c>'s
/// own subject.
/// </summary>
internal sealed class InMemoryReplicatedSessionStore : IReplicatedSessionStore
{
    public InMemoryReplicatedSessionStore(params SessionRecordEntry[] initial) =>
        Current = new ReplicatedSessions([.. initial], 0);

    public event Action? Changed;

    public ReplicatedSessions Current { get; private set; }

    /// <summary>Every save, as its own page. A test asserting that the device
    /// dropped its own echo reads the page rather than the total, because a page
    /// that arrived and was entirely dropped is a save that did not happen.</summary>
    public List<IReadOnlyList<SessionRecordEntry>> Saved { get; } = [];

    public string StorePath => "in memory";

    public void Save(IReadOnlyList<SessionRecordEntry> entries)
    {
        Saved.Add(entries);
        Current = new ReplicatedSessions([.. Current.Entries, .. entries], Current.Dropped);
        Changed?.Invoke();
    }
}

/// <summary>
/// A session source that answers with whatever a test seeded, so the push can be
/// driven without two agents' folders on disk.
/// </summary>
internal sealed class StubAgentSessionSource(params AgentSession[] sessions) : IAgentSessionSource
{
    public int Reads { get; private set; }

    public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        Reads++;

        return Task.FromResult(new AgentSessionCatalog(sessions, [], sessions.Length));
    }
}

/// <summary>
/// An activity source that answers with whatever a test seeded and remembers the
/// horizon it was asked for, so the push can be driven without a transcript on disk
/// and a test can say how far back it should have looked.
/// </summary>
internal sealed class StubAgentActivitySource(params AgentSessionActivity[] sessions) : IAgentActivitySource
{
    /// <summary>Every horizon this source was asked for, in order. One entry per
    /// push is the assertion: the transcripts are the expensive read, and a push
    /// that asked per batch or per record would be paying for them several times
    /// over.</summary>
    public List<DateTimeOffset> Asked { get; } = [];

    public Task<AgentActivityLog> GetActivityAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        Asked.Add(since);

        return Task.FromResult(new AgentActivityLog(sessions, [], since, TimeSpan.FromMinutes(5)));
    }
}

/// <summary>
/// An activity source that honours the horizon the way the real one does: an
/// interval that ended on or before it is gone, one that straddles it starts at
/// it, and a session left with nothing is absent. The push's horizon arithmetic
/// can only be caught by a source that clips — a stub that answers everything
/// whatever it was asked would pass a horizon that clipped a pending session to
/// nothing.
/// </summary>
internal sealed class ClippingAgentActivitySource(params AgentSessionActivity[] sessions) : IAgentActivitySource
{
    public List<DateTimeOffset> Asked { get; } = [];

    public Task<AgentActivityLog> GetActivityAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        Asked.Add(since);

        var clipped = sessions
            .Select(session => session with
            {
                Runs = [.. session.Runs.Where(run => run.EndedAt > since).Select(run => run.StartedAt >= since ? run : run with { StartedAt = since })],
                Waits = [.. session.Waits.Where(wait => wait.EndedAt > since).Select(wait => wait.StartedAt >= since ? wait : wait with { StartedAt = since })]
            })
            .Where(session => session.Runs.Count > 0 || session.Waits.Count > 0)
            .ToList();

        return Task.FromResult(new AgentActivityLog(clipped, [], since, TimeSpan.FromMinutes(5)));
    }
}

/// <summary>
/// The alias lookup, from a fixed table. Empty by default, which is the shape of a
/// machine where nobody has configured a repository — the case where the recorded
/// <c>owner/name</c> has to travel instead.
/// </summary>
internal sealed class StubSessionRepositoryAliases(Dictionary<string, string>? aliases = null) : ISessionRepositoryAliases
{
    private readonly Dictionary<string, string> _aliases = aliases ?? [];

    public string? AliasFor(string repository) =>
        _aliases.TryGetValue(repository, out var alias) ? alias : null;
}

/// <summary>Sessions to push, built one field at a time so a test can say which
/// field it is about.</summary>
internal static class AgentSessions
{
    public static AgentSession Local(
        string id = "session-1",
        AgentSessionKind kind = AgentSessionKind.Claude,
        string environmentId = "11111111-1111-1111-1111-111111111111",
        string environment = "Workshop PC",
        string title = "Fix the pairing dialog",
        string workingFolder = @"C:\Users\jane\repos\backlog",
        string? repository = "jsdotnet/backlog",
        string? branch = "main",
        DateTimeOffset? startedAt = null,
        DateTimeOffset? lastActivityAt = null,
        int? turnCount = 7,
        AgentSessionOrigin origin = AgentSessionOrigin.Local) =>
        new(
            id,
            kind,
            environmentId,
            environment,
            title,
            workingFolder,
            repository,
            branch,
            startedAt,
            lastActivityAt ?? new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero),
            AgentSessionState.Running,
            turnCount,
            origin);
}

/// <summary>What a local session was doing, built the same way. The runs and waits
/// are given as <c>(start, end)</c> pairs so a test reads as the stretches it is
/// about rather than as a list of constructor calls.</summary>
internal static class AgentActivities
{
    public static AgentSessionActivity Local(
        string id = "session-1",
        AgentSessionKind kind = AgentSessionKind.Claude,
        string environmentId = "11111111-1111-1111-1111-111111111111",
        string environment = "Workshop PC",
        (DateTimeOffset Start, DateTimeOffset End)[]? runs = null,
        (DateTimeOffset Start, DateTimeOffset End)[]? waits = null,
        AgentSessionOrigin origin = AgentSessionOrigin.Local) =>
        new(
            id,
            kind,
            environmentId,
            environment,
            [.. (runs ?? []).Select(run => new AgentActivityRun(run.Start, run.End))],
            [.. (waits ?? []).Select(wait => new AgentActivityWait(wait.Start, wait.End))])
        {
            Origin = origin
        };
}

/// <summary>Wire records to pull, built the same way.</summary>
internal static class SessionRecords
{
    public static SessionRecordEntry Entry(
        Guid machineId,
        string sessionId = "remote-1",
        string agentKind = "claude",
        string machineName = "Laptop",
        string? repositoryAlias = "backlog",
        string? branch = "main",
        DateTimeOffset? startedAt = null,
        DateTimeOffset? lastActivityAt = null,
        int? turnCount = 3,
        long serverTimestamp = 1,
        IReadOnlyList<ActivityInterval>? runs = null,
        IReadOnlyList<ActivityInterval>? waits = null) =>
        new(
            new SessionRecord(
                sessionId,
                agentKind,
                machineName,
                repositoryAlias,
                branch,
                startedAt,
                lastActivityAt ?? new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero),
                turnCount,
                0,
                runs,
                waits),
            machineId,
            serverTimestamp);
}
