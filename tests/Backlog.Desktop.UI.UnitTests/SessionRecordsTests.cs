using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI.Adapters;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// This machine's session records: how a reading amends one, how a record answers once
/// the files are gone, and the keeper that writes them.
/// </summary>
public sealed class SessionRecordsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    // --- Amend -----------------------------------------------------------------

    /// <summary>A value the reading lacks keeps what the record held; a value it has
    /// wins. Nothing the record knew is lost to a reading that knows less.</summary>
    [Fact]
    public void A_reading_that_knows_less_takes_nothing_from_the_record()
    {
        var stored = Record(Session("s-1") with { Title = "Rewrite the pairing dialog copy", Branch = "main", TurnCount = 12, StartedAt = Noon.AddHours(-5) });
        var reading = Record(Session("s-1") with { Title = "", Branch = null, TurnCount = 4, StartedAt = null, LastActivityAt = Noon.AddHours(1) });

        var amended = AgentSessionRecords.Amend(stored, reading).Session;

        Assert.Equal("Rewrite the pairing dialog copy", amended.Title);
        Assert.Equal("main", amended.Branch);
        Assert.Equal(12, amended.TurnCount);
        Assert.Equal(Noon.AddHours(-5), amended.StartedAt);
        Assert.Equal(Noon.AddHours(1), amended.LastActivityAt);
    }

    /// <summary>The reading's runs replace the record's only from the reading's own first
    /// instant on — the same lines folded again — and the record's earlier runs, which a
    /// shortened transcript no longer holds, are kept. Limit hits are a union.</summary>
    [Fact]
    public void Activity_is_kept_before_the_reading_and_hits_are_a_union()
    {
        var stored = Record(Session("s-1"), Activity("s-1",
            runs: [(Noon.AddHours(-6), Noon.AddHours(-5)), (Noon.AddHours(-2), Noon.AddHours(-1))],
            hits: [Noon.AddHours(-5)]));
        var reading = Record(Session("s-1"), Activity("s-1",
            runs: [(Noon.AddHours(-2), Noon)],
            hits: [Noon.AddMinutes(-1)]));

        var activity = AgentSessionRecords.Amend(stored, reading).Activity!;

        Assert.Equal(
            [new AgentActivityRun(Noon.AddHours(-6), Noon.AddHours(-5)), new AgentActivityRun(Noon.AddHours(-2), Noon)],
            activity.Runs);
        Assert.Equal([Noon.AddHours(-5), Noon.AddMinutes(-1)], activity.LimitHits.Select(hit => hit.At));
    }

    /// <summary>A reading with no fold keeps the record's.</summary>
    [Fact]
    public void A_reading_without_activity_keeps_the_records()
    {
        var stored = Record(Session("s-1"), Activity("s-1", runs: [(Noon.AddHours(-1), Noon)]));

        Assert.Same(stored.Activity, AgentSessionRecords.Amend(stored, Record(Session("s-1"))).Activity);
    }

    // --- Precedence -------------------------------------------------------------

    /// <summary>One answer per session: the files, then the record, then sync. A record
    /// answers alone for a session whose files are gone.</summary>
    [Fact]
    public async Task The_files_win_over_the_record_and_the_record_over_sync()
    {
        var composite = new CompositeAgentSessionSource(
        [
            new FixedSource([Session("both")]),
            new FixedSource([Session("both") with { Origin = AgentSessionOrigin.Recorded }, Session("gone") with { Origin = AgentSessionOrigin.Recorded }]),
            new FixedSource([Session("both") with { Origin = AgentSessionOrigin.Replicated }, Session("gone") with { Origin = AgentSessionOrigin.Replicated }])
        ]);

        var catalog = await composite.GetSessionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [("both", AgentSessionOrigin.Local), ("gone", AgentSessionOrigin.Recorded)],
            catalog.Sessions.Select(session => (session.Id, session.Origin)));
        Assert.Equal(2, catalog.Discovered);
    }

    // --- The recorded sources ----------------------------------------------------

    /// <summary>A record answers as a finished session of this machine's, and its activity
    /// is clipped to the horizon on the local source's terms.</summary>
    [Fact]
    public async Task A_record_answers_as_a_finished_session_and_clipped_activity()
    {
        var store = new MemoryStore();
        store.Save([Record(Session("gone"), Activity("gone", runs: [(Noon.AddHours(-3), Noon.AddHours(-1))], hits: [Noon.AddHours(-2), Noon.AddHours(-4)]))]);

        var session = Assert.Single((await new RecordedAgentSessionSource(store).GetSessionsAsync(TestContext.Current.CancellationToken)).Sessions);
        Assert.Equal(AgentSessionOrigin.Recorded, session.Origin);
        Assert.Equal(AgentSessionState.Finished, session.State);

        var activity = Assert.Single((await new RecordedAgentActivitySource(store).GetActivityAsync(Noon.AddHours(-2), TestContext.Current.CancellationToken)).Sessions);
        Assert.Equal([new AgentActivityRun(Noon.AddHours(-2), Noon.AddHours(-1))], activity.Runs);
        Assert.Equal([Noon.AddHours(-2)], activity.LimitHits.Select(hit => hit.At));
    }

    // --- The keeper ---------------------------------------------------------------

    /// <summary>The keeper records what the local readers hold — never a replicated
    /// session — with its fold, and asks sync to send everything again only when asked
    /// for everything.</summary>
    [Fact]
    public async Task The_keeper_records_local_sessions_with_their_fold_and_publishes_on_everything()
    {
        var store = new MemoryStore();
        var publisher = new StubPublisher();
        var keeper = new SessionRecordKeeper(
            new FixedSource([Session("mine"), Session("theirs") with { Origin = AgentSessionOrigin.Replicated }]),
            new FixedActivity([Activity("mine", runs: [(Noon.AddHours(-1), Noon)], hits: [Noon.AddMinutes(-5)])]),
            store,
            new FakeTimeProvider(Noon),
            publisher);

        var update = await keeper.UpdateAsync(everything: true, TestContext.Current.CancellationToken);

        Assert.Equal(new SessionRecordUpdate(1, 0, Published: true), update);
        var record = Assert.Single(store.All());
        Assert.Equal("mine", record.Session.Id);
        Assert.Single(record.Activity!.LimitHits);
        Assert.Equal(1, publisher.Calls);

        await keeper.UpdateAsync(everything: false, TestContext.Current.CancellationToken);
        Assert.Equal(1, publisher.Calls);
    }

    /// <summary>With no store composed the keeper does nothing and says so.</summary>
    [Fact]
    public async Task A_keeper_without_a_store_does_nothing()
    {
        var keeper = new SessionRecordKeeper(new FixedSource([Session("mine")]), null, null, new FakeTimeProvider(Noon), null);

        Assert.Equal(SessionRecordUpdate.None, await keeper.UpdateAsync(everything: true, TestContext.Current.CancellationToken));
    }

    // --- Helpers --------------------------------------------------------------------

    private static AgentSession Session(string id) =>
        new(
            id,
            AgentSessionKind.Claude,
            "11111111-1111-1111-1111-111111111111",
            "Workshop PC",
            "A session",
            @"D:\Repos\Backlog",
            null,
            null,
            Noon.AddHours(-6),
            Noon,
            AgentSessionState.Running,
            null,
            AgentSessionOrigin.Local);

    private static AgentSessionActivity Activity(
        string id,
        (DateTimeOffset From, DateTimeOffset To)[]? runs = null,
        DateTimeOffset[]? hits = null) =>
        new(id, AgentSessionKind.Claude, "11111111-1111-1111-1111-111111111111", "Workshop PC",
            [.. (runs ?? []).Select(run => new AgentActivityRun(run.From, run.To))], [])
        {
            LimitHits = [.. (hits ?? []).Select(at => new AgentLimitHit(at, AgentLimitKind.FiveHour, "five_hour"))]
        };

    private static AgentSessionRecord Record(AgentSession session, AgentSessionActivity? activity = null) =>
        new(session, activity, Noon);

    private sealed class FixedSource(IReadOnlyList<AgentSession> sessions) : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            GetSessionsAsync(AgentSessionQuery.Newest, cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentSessionCatalog(sessions, [], sessions.Count));
    }

    private sealed class FixedActivity(IReadOnlyList<AgentSessionActivity> sessions) : IAgentActivitySource
    {
        public Task<AgentActivityLog> GetActivityAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentActivityLog(sessions, [], since, TimeSpan.FromMinutes(5)));
    }

    private sealed class StubPublisher : ISessionRecordPublisher
    {
        public int Calls { get; private set; }

        public bool RepublishAll()
        {
            Calls++;

            return true;
        }
    }

    /// <summary>The store's contract without a disk: amend on save, one record per
    /// agent and id.</summary>
    internal sealed class MemoryStore : IAgentSessionRecordStore
    {
        private readonly Dictionary<(AgentSessionKind, string), AgentSessionRecord> _records = [];

        public IReadOnlyList<AgentSessionRecord> All() => [.. _records.Values];

        public SessionRecordUpdate Save(IReadOnlyList<AgentSessionRecord> readings)
        {
            var started = 0;
            var amended = 0;

            foreach (var reading in readings)
            {
                var key = (reading.Session.Kind, reading.Session.Id);
                var stored = _records.GetValueOrDefault(key);

                _records[key] = AgentSessionRecords.Amend(stored, reading);

                if (stored is null) started++;
                else amended++;
            }

            return new SessionRecordUpdate(started, amended);
        }
    }
}
