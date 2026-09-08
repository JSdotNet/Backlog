using Backlog.Infrastructure.Sync.Sessions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The read side: what another environment's record looks like once it is a row in
/// this device's list.
/// <para>
/// Every assertion here is about a claim the product would otherwise make on
/// somebody else's behalf — that a session is still running, that this device read
/// it for itself, that it has a title. None of those is true of a record that
/// arrived over a wire, and none of them fails visibly if it is got wrong.
/// </para>
/// </summary>
public sealed class ReplicatedAgentSessionSourceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Laptop = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>
    /// A record this device was told about is stamped
    /// <see cref="AgentSessionOrigin.Replicated"/> on the way out of the source, not
    /// worked out later from whether the environment id matches this device. A
    /// comparison gets the wrong answer for a record this device pushed and received
    /// back, and it would put a second definition of the same word in every surface
    /// that asked.
    /// </summary>
    [Fact]
    public async Task Every_session_is_stamped_replicated()
    {
        var source = Source(SessionRecords.Entry(Laptop, lastActivityAt: Noon));

        var catalog = await source.GetSessionsAsync(TestContext.Current.CancellationToken);

        Assert.All(catalog.Sessions, session => Assert.Equal(AgentSessionOrigin.Replicated, session.Origin));
    }

    /// <summary>
    /// The environment is keyed on the id the service stamped and labelled with the
    /// name the record carried. <c>.domain/sessions/naming.md#environment</c> keys an
    /// environment on its id and not on the name it displays, because a name can be
    /// shared by two machines and changed on one.
    /// </summary>
    [Fact]
    public async Task The_environment_is_the_machine_id_with_its_name_beside_it()
    {
        var source = Source(SessionRecords.Entry(Laptop, machineName: "Kitchen laptop"));

        var session = Assert.Single((await source.GetSessionsAsync(TestContext.Current.CancellationToken)).Sessions);

        Assert.Equal(Laptop.ToString(), session.EnvironmentId);
        Assert.Equal("Kitchen laptop", session.Environment);
    }

    /// <summary>
    /// Titles do not sync and working folders never leave the machine, so there is
    /// no title to show and no folder to show. The row carries the session id
    /// instead of a sentence this device composed about work it never saw, and the
    /// folder is blank rather than a path that would describe a disk this machine
    /// cannot see.
    /// </summary>
    [Fact]
    public async Task A_replicated_session_has_no_title_of_its_own_and_no_working_folder()
    {
        var source = Source(SessionRecords.Entry(Laptop, sessionId: "abc-123"));

        var session = Assert.Single((await source.GetSessionsAsync(TestContext.Current.CancellationToken)).Sessions);

        Assert.Equal("abc-123", session.Title);
        Assert.Equal(string.Empty, session.WorkingFolder);
    }

    /// <summary>
    /// State is derived on read, from the one piece of evidence the record carries.
    /// A state on the wire would freeze the sending machine's reading of its own
    /// clock and go on asserting "Running" for a session that ended before the last
    /// sync — which is exactly the assertion
    /// <c>.domain/sessions/domain.md</c> forbids.
    /// </summary>
    [Fact]
    public async Task State_is_derived_from_the_timestamp_and_goes_stale_on_its_own()
    {
        var source = Source(
            SessionRecords.Entry(Laptop, sessionId: "fresh", lastActivityAt: Noon.AddMinutes(-1)),
            SessionRecords.Entry(
                Laptop,
                sessionId: "quiet",
                lastActivityAt: Noon - AgentSessionStates.StaleAfter.Add(TimeSpan.FromMinutes(1))));

        var sessions = (await source.GetSessionsAsync(TestContext.Current.CancellationToken)).Sessions;

        Assert.Equal(AgentSessionState.Running, sessions.Single(session => session.Id == "fresh").State);

        // Finished, not Stalled. Stalled claims something is still there and quiet,
        // and a record cannot tell that from a session that ended — the same reason
        // CopilotSessionReader, which also has no liveness marker, reads silence as
        // Finished.
        Assert.Equal(AgentSessionState.Finished, sessions.Single(session => session.Id == "quiet").State);
    }

    /// <summary>
    /// The same record read an hour later has stopped claiming to be live, without
    /// anything having been re-pulled. That is the whole content of "a replicated
    /// record is stale rather than wrong": nothing new arrived, and the row stopped
    /// asserting liveness anyway.
    /// </summary>
    [Fact]
    public async Task The_same_record_stops_claiming_to_be_live_as_the_clock_moves()
    {
        var clock = new FakeTimeProvider(Noon);
        var source = new ReplicatedAgentSessionSource(
            new InMemoryReplicatedSessionStore(SessionRecords.Entry(Laptop, lastActivityAt: Noon)),
            new StubFeatureSettings(enabled: true),
            clock);

        Assert.Equal(
            AgentSessionState.Running,
            (await source.GetSessionsAsync(TestContext.Current.CancellationToken)).Sessions[0].State);

        clock.Advance(AgentSessionStates.StaleAfter.Add(TimeSpan.FromMinutes(1)));

        Assert.Equal(
            AgentSessionState.Finished,
            (await source.GetSessionsAsync(TestContext.Current.CancellationToken)).Sessions[0].State);
    }

    /// <summary>
    /// A turn count the sending agent never recorded arrives as null rather than as
    /// zero. Zero is a claim about what happened, and the Session Log never fills a
    /// gap the agent left.
    /// </summary>
    [Fact]
    public async Task An_unrecorded_turn_count_arrives_as_null()
    {
        var source = Source(SessionRecords.Entry(Laptop, turnCount: null));

        var session = Assert.Single((await source.GetSessionsAsync(TestContext.Current.CancellationToken)).Sessions);

        Assert.Null(session.TurnCount);
    }

    /// <summary>
    /// Switching the feature off has to take the other machines' rows off the
    /// screen, not merely stop new ones arriving — a switch that leaves what it
    /// gathered on display is a switch a person cannot see the effect of. The cache
    /// is left alone, so switching it back on shows what was already there rather
    /// than waiting five minutes for a cycle.
    /// </summary>
    [Fact]
    public async Task Nothing_is_answered_while_the_feature_is_off()
    {
        var store = new InMemoryReplicatedSessionStore(SessionRecords.Entry(Laptop));
        var features = new StubFeatureSettings(enabled: false);
        var source = new ReplicatedAgentSessionSource(store, features, new FakeTimeProvider(Noon));

        Assert.Empty((await source.GetSessionsAsync(TestContext.Current.CancellationToken)).Sessions);

        _ = features.SetEnabled("session-sync", true);

        Assert.Single((await source.GetSessionsAsync(TestContext.Current.CancellationToken)).Sessions);
    }

    /// <summary>
    /// What the cap discarded is counted back into <c>Discovered</c>, so a list the
    /// store had to truncate stays visibly truncated. A source that reported only
    /// what it still held would present a cut-back history as the whole of it, which
    /// is the one thing a capped list must not do.
    /// </summary>
    [Fact]
    public async Task What_the_cap_discarded_is_still_counted()
    {
        var source = new ReplicatedAgentSessionSource(
            new StubReplicatedStore(new ReplicatedSessions([SessionRecords.Entry(Laptop)], Dropped: 4)),
            new StubFeatureSettings(enabled: true),
            new FakeTimeProvider(Noon));

        var catalog = await source.GetSessionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(5, catalog.Discovered);
        Assert.True(catalog.Capped);
    }

    /// <summary>A source over a fixed set of records, read at
    /// <see cref="Noon"/> with the feature on.</summary>
    private static ReplicatedAgentSessionSource Source(params SessionRecordEntry[] entries) =>
        new(
            new InMemoryReplicatedSessionStore(entries),
            new StubFeatureSettings(enabled: true),
            new FakeTimeProvider(Noon));

    /// <summary>A store that answers with a fixed <see cref="ReplicatedSessions"/>,
    /// including a dropped count the in-memory one has no way to produce.</summary>
    private sealed class StubReplicatedStore(ReplicatedSessions current) : IReplicatedSessionStore
    {
        public event Action? Changed;

        public ReplicatedSessions Current { get; } = current;

        public string StorePath => "in memory";

        public void Save(IReadOnlyList<SessionRecordEntry> entries) => Changed?.Invoke();
    }
}
