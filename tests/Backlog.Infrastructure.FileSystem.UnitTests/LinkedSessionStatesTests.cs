using Backlog.Infrastructure.FileSystem.Tasks;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The join between a task's linked sessions and the Sessions context's record of
/// them. A mapping and nothing else: the state is the one Sessions decided, keyed by
/// the id the task asked with, and an id the record does not hold is left out rather
/// than guessed.
/// </summary>
public class LinkedSessionStatesTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(AgentSessionState.Running, LinkedSessionState.Running)]
    [InlineData(AgentSessionState.Stalled, LinkedSessionState.Stalled)]
    [InlineData(AgentSessionState.Finished, LinkedSessionState.Finished)]
    public async Task Each_session_state_crosses_as_the_same_state(AgentSessionState state, LinkedSessionState expected)
    {
        var adapter = new LinkedSessionStates(new StubSessions(Session("e711d47d-3e09", state)));

        var states = await adapter.StatesOfAsync(["e711d47d-3e09"], TestContext.Current.CancellationToken);

        Assert.Equal(expected, states["e711d47d-3e09"].State);
    }

    /// <summary>
    /// Whether the Claude desktop app still holds the session open or has archived it
    /// crosses as the Sessions record has it: archived, open, or — with no desktop
    /// record at all (a CLI-only or Copilot session, one from another machine) —
    /// unknown, which is not open.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public async Task Whether_the_desktop_app_archived_a_session_crosses_unchanged(bool? archived)
    {
        var adapter = new LinkedSessionStates(new StubSessions(Session("e711d47d-3e09", AgentSessionState.Finished) with { Archived = archived }));

        var states = await adapter.StatesOfAsync(["e711d47d-3e09"], TestContext.Current.CancellationToken);

        Assert.Equal(new LinkedSession(LinkedSessionState.Finished, archived), states["e711d47d-3e09"]);
    }

    /// <summary>Keyed by the id as the task asked, matched without regard to case,
    /// because the task keeps the string it was linked with.</summary>
    [Fact]
    public async Task An_id_is_matched_regardless_of_case_and_answered_as_asked()
    {
        var adapter = new LinkedSessionStates(new StubSessions(Session("E711D47D-3E09", AgentSessionState.Stalled)));

        var states = await adapter.StatesOfAsync(["e711d47d-3e09"], TestContext.Current.CancellationToken);

        var (id, session) = Assert.Single(states);
        Assert.Equal("e711d47d-3e09", id);
        Assert.Equal(LinkedSessionState.Stalled, session.State);
    }

    [Fact]
    public async Task A_session_the_record_does_not_hold_is_absent()
    {
        var adapter = new LinkedSessionStates(new StubSessions(Session("other", AgentSessionState.Running)));

        var states = await adapter.StatesOfAsync(["gone"], TestContext.Current.CancellationToken);

        Assert.Empty(states);
    }

    /// <summary>No ids, no read: a backlog that links no session never touches the
    /// session files.</summary>
    [Fact]
    public async Task No_ids_reads_nothing()
    {
        var sessions = new StubSessions(Session("one", AgentSessionState.Running));
        var adapter = new LinkedSessionStates(sessions);

        var states = await adapter.StatesOfAsync([], TestContext.Current.CancellationToken);

        Assert.Empty(states);
        Assert.Equal(0, sessions.Reads);
    }

    /// <summary>A source that throws answers nothing rather than failing the list
    /// that asked: the badge simply has no state to show.</summary>
    [Fact]
    public async Task A_source_that_fails_answers_nothing()
    {
        var adapter = new LinkedSessionStates(new StubSessions(failure: new IOException("locked")));

        var states = await adapter.StatesOfAsync(["one"], TestContext.Current.CancellationToken);

        Assert.Empty(states);
    }

    /// <summary>A badge needs a state and nothing a transcript is opened for, so the
    /// inventory is asked for at the timestamps depth — the reading that opens no
    /// transcript on a list load.</summary>
    [Fact]
    public async Task The_states_are_read_from_the_inventory_at_the_timestamps_depth()
    {
        var sessions = new StubSessions(Session("one", AgentSessionState.Running));
        var adapter = new LinkedSessionStates(sessions);

        await adapter.StatesOfAsync(["one"], TestContext.Current.CancellationToken);

        var query = Assert.Single(sessions.Queries);
        Assert.True(query.IsNewest);
        Assert.Equal(AgentSessionDetail.Timestamps, query.Detail);
    }

    private static AgentSession Session(string id, AgentSessionState state) =>
        new(
            Id: id,
            Kind: AgentSessionKind.Claude,
            EnvironmentId: "tower",
            Environment: "DEV-TOWER",
            Title: id,
            WorkingFolder: @"D:\Repos\Backlog",
            Repository: null,
            Branch: null,
            StartedAt: Noon.AddHours(-2),
            LastActivityAt: Noon.AddHours(-1),
            State: state,
            TurnCount: null,
            Origin: AgentSessionOrigin.Local);

    private sealed class StubSessions(params AgentSession[] sessions) : IAgentSessionSource
    {
        private readonly Exception? _failure;

        public StubSessions(Exception failure) : this() => _failure = failure;

        public int Reads { get; private set; }

        public List<AgentSessionQuery> Queries { get; } = [];

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            GetSessionsAsync(AgentSessionQuery.Newest, cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default)
        {
            Reads++;
            Queries.Add(query);
            if (_failure is not null) throw _failure;
            return Task.FromResult(new AgentSessionCatalog(sessions, [], sessions.Length));
        }
    }
}
