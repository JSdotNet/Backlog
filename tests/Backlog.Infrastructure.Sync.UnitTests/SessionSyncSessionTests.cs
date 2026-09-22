using System.Net;
using System.Text.Json;

using Backlog.Infrastructure.Sync.Sessions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// One session exchange: what leaves the machine, how far the watermark may move,
/// and when the pull stops.
///
/// <para>The session drives a real <see cref="SessionSyncClient"/> over a scripted
/// wire rather than a stand-in, because half of what these assert is the shape of
/// the request that would actually have gone out — and the sanitization boundary
/// can only honestly be asserted against a serialized body.</para>
/// </summary>
public sealed class SessionSyncSessionTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid ThisDevice = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ThisOwner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnotherOwner = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>Progress recorded for the fixture's own device. A seeded state
    /// with no identity is started over on first use, by design, so a test
    /// seeding progress it wants kept has to say whose it is.</summary>
    private static SessionSyncState Mine(DateTimeOffset watermark, string? cursor) =>
        new(watermark, cursor, ThisOwner, ThisDevice);

    private static readonly Guid OtherDevice = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // --- The sanitization boundary --------------------------------------------

    /// <summary>
    /// <strong>The one test this whole slice exists to keep passing.</strong>
    /// .arc42/adr/0005 §Session records permits twelve fields to leave a machine and
    /// says a whitelist and a filter fail in opposite directions. This asserts over
    /// the bytes that went out, not over the DTO: a test on the record would go on
    /// passing if somebody widened the wire contract, which is exactly the change
    /// that would leak.
    /// </summary>
    [Fact]
    public async Task The_pushed_body_carries_no_working_folder_and_no_title()
    {
        using var fixture = Fixture.Create(sessions:
        [
            AgentSessions.Local(
                title: "Rewrite the pairing dialog copy",
                workingFolder: @"C:\Users\jane\repos\backlog\src\App")
        ]);

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);

        // The literal strings, so the assertion fails on a field that carried them
        // under any name at all - "workingFolder", "path", "cwd", "title".
        Assert.DoesNotContain("Rewrite the pairing dialog copy", fixture.LastBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\\Users", fixture.LastBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("workingFolder", fixture.LastBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("title", fixture.LastBody, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The other half of the same rule, said as a whitelist rather than as a list
    /// of things that must be absent: these eleven property names and no others.
    /// A field added to the wire fails here even if nobody thought to write a test
    /// naming it, which is the only form of this assertion that keeps working
    /// against a change nobody anticipated.
    /// </summary>
    [Fact]
    public async Task The_pushed_body_carries_exactly_the_whitelisted_fields()
    {
        using var fixture = Fixture.Create(sessions: [AgentSessions.Local()]);

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(fixture.LastBody);
        var record = document.RootElement.GetProperty("sessions")[0];

        var fields = record.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal);

        Assert.Equal(
            [
                "agentKind",
                "branch",
                "durationSeconds",
                "lastActivityAt",
                "machineName",
                "repositoryAlias",
                "runs",
                "sessionId",
                "startedAt",
                "turnCount",
                "waits"
            ],
            fields);
    }

    /// <summary>
    /// The twelfth whitelisted field is the machine id, and the pushing device does
    /// not send it: the service stamps it from the token. There is no field to set,
    /// which is what makes "a caller may only write records stamped with its own
    /// machine id" hold by construction.
    /// </summary>
    [Fact]
    public async Task The_pushed_body_names_no_machine_id()
    {
        using var fixture = Fixture.Create(sessions: [AgentSessions.Local()]);

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("machineId", fixture.LastBody, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A record from another environment is not this machine's to publish.
    /// .arc42/adr/0005 §Session records makes session records single-writer, and
    /// re-publishing another machine's record under this device's token would
    /// attach its work to this box — and, because the watermark would then advance
    /// on a replicated stamp, do it again every cycle for ever.
    /// </summary>
    [Fact]
    public async Task A_replicated_session_is_never_pushed()
    {
        using var fixture = Fixture.Create(sessions:
        [
            AgentSessions.Local(id: "mine"),
            AgentSessions.Local(id: "theirs", origin: AgentSessionOrigin.Replicated)
        ]);

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.Contains("\"mine\"", fixture.LastBody, StringComparison.Ordinal);
        Assert.DoesNotContain("\"theirs\"", fixture.LastBody, StringComparison.Ordinal);
    }

    // --- The repository alias --------------------------------------------------

    /// <summary>Where the machine has an alias for the repository the agent
    /// recorded, the alias travels — that is the word .arc42/adr/0005 asks
    /// for.</summary>
    [Fact]
    public async Task A_configured_repository_travels_as_its_alias()
    {
        using var fixture = Fixture.Create(
            sessions: [AgentSessions.Local(repository: "jsdotnet/backlog")],
            aliases: new Dictionary<string, string> { ["jsdotnet/backlog"] = "backlog" });

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.Equal("backlog", fixture.PushedRecord().GetProperty("repositoryAlias").GetString());
    }

    /// <summary>
    /// Where it has none, the recorded <c>owner/name</c> travels rather than null.
    /// An alias is a label this machine happens to have configured; the repository
    /// is a fact about the session, and dropping the fact because the label is
    /// missing would lose it on every machine nobody has configured.
    /// </summary>
    [Fact]
    public async Task An_unconfigured_repository_travels_as_the_recorded_coordinate()
    {
        using var fixture = Fixture.Create(sessions: [AgentSessions.Local(repository: "jsdotnet/backlog")]);

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.Equal("jsdotnet/backlog", fixture.PushedRecord().GetProperty("repositoryAlias").GetString());
    }

    /// <summary>
    /// A session the agent recorded no repository for sends null, and nothing is
    /// derived from the folder it was running in. <c>.domain/sessions/domain.md</c>
    /// is explicit that a repository guessed from a path is indistinguishable from
    /// a recorded one and wrong — and the machine receiving it has no way to tell
    /// the two apart, so a guess made here would be believed there.
    /// </summary>
    [Fact]
    public async Task A_session_with_no_recorded_repository_sends_null_and_never_the_folder()
    {
        using var fixture = Fixture.Create(sessions:
        [
            AgentSessions.Local(repository: null, workingFolder: @"C:\Users\jane\repos\backlog")
        ]);

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(JsonValueKind.Null, fixture.PushedRecord().GetProperty("repositoryAlias").ValueKind);
        Assert.DoesNotContain("backlog", fixture.LastBody, StringComparison.OrdinalIgnoreCase);
    }

    // --- Turn count and duration ----------------------------------------------

    /// <summary>
    /// A turn count the agent never recorded must not arrive on the far side as
    /// zero. Zero is a count — it says a person opened a session and never spoke in
    /// it — and <c>.domain/sessions/domain.md#session-log</c> forbids filling a gap
    /// the agent left.
    /// </summary>
    [Fact]
    public async Task An_unrecorded_turn_count_does_not_travel_as_zero()
    {
        using var fixture = Fixture.Create(sessions: [AgentSessions.Local(turnCount: null)]);

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        var travelled = fixture.PushedRecord().GetProperty("turnCount");

        // Null on the wire, not zero and not a sentinel. Zero would be a count, and
        // a sentinel reads as a number to anything that has not been told what it
        // means — including a future client of this service that never heard of it.
        Assert.Equal(JsonValueKind.Null, travelled.ValueKind);
        Assert.Null(SessionRecordMapping.ToSession(
            SessionRecords.Entry(OtherDevice, turnCount: null),
            Noon).TurnCount);
    }

    /// <summary>Duration is the interval between the two stamps, and zero where
    /// there is no start to measure from rather than a negative number or a
    /// guess.</summary>
    [Fact]
    public async Task Duration_is_the_interval_and_zero_without_a_start()
    {
        using var fixture = Fixture.Create(sessions:
        [
            AgentSessions.Local(id: "timed", startedAt: Noon.AddMinutes(-30), lastActivityAt: Noon),
            AgentSessions.Local(id: "untimed", startedAt: null, lastActivityAt: Noon.AddMinutes(1))
        ]);

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(fixture.LastBody);
        var records = document.RootElement.GetProperty("sessions").EnumerateArray().ToList();

        Assert.Equal(1_800, records[0].GetProperty("durationSeconds").GetInt64());
        Assert.Equal(0, records[1].GetProperty("durationSeconds").GetInt64());
    }

    // --- Activity intervals ----------------------------------------------------

    /// <summary>
    /// A session this machine folded an activity record for travels with its runs
    /// and waits, as timestamps and nothing else. This is what lets a reading
    /// device measure a session it did not run: the transcript stays home, and
    /// these two lists are the whole of what leaves it.
    /// </summary>
    [Fact]
    public async Task A_session_with_an_activity_record_travels_with_its_runs_and_waits()
    {
        using var fixture = Fixture.Create(
            sessions: [AgentSessions.Local(id: "busy")],
            agentActivity: new StubAgentActivitySource(AgentActivities.Local(
                id: "busy",
                runs: [(Noon.AddMinutes(-30), Noon.AddMinutes(-20)), (Noon.AddMinutes(-10), Noon)],
                waits: [(Noon.AddMinutes(-20), Noon.AddMinutes(-10))])));

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        var record = fixture.PushedRecord();

        var runs = record.GetProperty("runs").EnumerateArray().ToList();
        Assert.Equal(2, runs.Count);
        Assert.Equal(Noon.AddMinutes(-30), runs[0].GetProperty("startedAt").GetDateTimeOffset());
        Assert.Equal(Noon.AddMinutes(-20), runs[0].GetProperty("endedAt").GetDateTimeOffset());
        Assert.Equal(Noon, runs[1].GetProperty("endedAt").GetDateTimeOffset());

        var wait = Assert.Single(record.GetProperty("waits").EnumerateArray());
        Assert.Equal(Noon.AddMinutes(-20), wait.GetProperty("startedAt").GetDateTimeOffset());
        Assert.Equal(Noon.AddMinutes(-10), wait.GetProperty("endedAt").GetDateTimeOffset());
    }

    /// <summary>
    /// A session the activity source had nothing for travels with null in both
    /// lists, and never with an empty one. Null says "no record"; an empty list says
    /// "a record, and this is what it held" — and the reader counts the two
    /// differently, so a pusher that sent <c>[]</c> for a session it could not fold
    /// would have every other machine reading that session as measured at zero.
    /// </summary>
    [Fact]
    public async Task A_session_without_an_activity_record_travels_with_null_lists()
    {
        using var fixture = Fixture.Create(
            sessions: [AgentSessions.Local(id: "quiet"), AgentSessions.Local(id: "busy", lastActivityAt: Noon.AddMinutes(1))],
            agentActivity: new StubAgentActivitySource(AgentActivities.Local(
                id: "busy",
                runs: [(Noon.AddMinutes(-10), Noon)])));

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(fixture.LastBody);
        var records = document.RootElement.GetProperty("sessions").EnumerateArray().ToList();

        var quiet = records.Single(record => record.GetProperty("sessionId").GetString() == "quiet");
        Assert.Equal(JsonValueKind.Null, quiet.GetProperty("runs").ValueKind);
        Assert.Equal(JsonValueKind.Null, quiet.GetProperty("waits").ValueKind);

        var busy = records.Single(record => record.GetProperty("sessionId").GetString() == "busy");
        Assert.Equal(JsonValueKind.Array, busy.GetProperty("runs").ValueKind);
    }

    /// <summary>
    /// An empty list is kept as an empty list. Every Copilot session has empty
    /// waits, because Copilot cannot mark the boundary a wait needs, and that is a
    /// fact about the session the far side is owed rather than a gap to be mapped
    /// onto null.
    /// </summary>
    [Fact]
    public async Task An_empty_list_travels_as_empty_and_not_as_null()
    {
        using var fixture = Fixture.Create(
            sessions: [AgentSessions.Local(id: "copilot", kind: AgentSessionKind.Copilot)],
            agentActivity: new StubAgentActivitySource(AgentActivities.Local(
                id: "copilot",
                kind: AgentSessionKind.Copilot,
                runs: [(Noon.AddMinutes(-10), Noon)],
                waits: [])));

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        var waits = fixture.PushedRecord().GetProperty("waits");

        Assert.Equal(JsonValueKind.Array, waits.ValueKind);
        Assert.Equal(0, waits.GetArrayLength());
    }

    /// <summary>
    /// A host that composed no activity source pushes the record it always did:
    /// null in both lists. That is the shape of every push before these fields
    /// existed, and the reader treats it as "no record" rather than as "measured at
    /// nothing".
    /// </summary>
    [Fact]
    public async Task A_host_without_an_activity_source_pushes_null_lists()
    {
        using var fixture = Fixture.Create(sessions: [AgentSessions.Local()]);

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        var record = fixture.PushedRecord();

        Assert.Equal(JsonValueKind.Null, record.GetProperty("runs").ValueKind);
        Assert.Equal(JsonValueKind.Null, record.GetProperty("waits").ValueKind);
    }

    /// <summary>
    /// The activity port is the merged one every screen reads, so what it answers
    /// includes what other machines reported. A replicated record is not this
    /// machine's to publish, for the reason
    /// <see cref="A_replicated_session_is_never_pushed"/> gives — and here the
    /// failure would be quieter still, because the session itself is local and only
    /// its activity would be somebody else's.
    /// </summary>
    [Fact]
    public async Task Replicated_activity_is_never_attached_to_a_local_session()
    {
        using var fixture = Fixture.Create(
            sessions: [AgentSessions.Local(id: "shared-id")],
            agentActivity: new StubAgentActivitySource(AgentActivities.Local(
                id: "shared-id",
                runs: [(Noon.AddMinutes(-10), Noon)],
                origin: AgentSessionOrigin.Replicated)));

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(JsonValueKind.Null, fixture.PushedRecord().GetProperty("runs").ValueKind);
    }

    /// <summary>
    /// Activity is matched on the agent and the id together, never the id alone.
    /// <c>.domain/sessions/naming.md#session-identity</c> puts a session's identity
    /// at both, because two agents may issue the same string — and a Copilot
    /// session's runs attached to a Claude session of the same id would be one
    /// machine's measure of the wrong session.
    /// </summary>
    [Fact]
    public async Task Activity_is_matched_on_the_agent_and_the_id_together()
    {
        using var fixture = Fixture.Create(
            sessions: [AgentSessions.Local(id: "shared-id", kind: AgentSessionKind.Claude)],
            agentActivity: new StubAgentActivitySource(AgentActivities.Local(
                id: "shared-id",
                kind: AgentSessionKind.Copilot,
                runs: [(Noon.AddMinutes(-10), Noon)])));

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(JsonValueKind.Null, fixture.PushedRecord().GetProperty("runs").ValueKind);
    }

    /// <summary>
    /// A record carries the session's whole activity every time it goes, so the
    /// horizon must never clip a pending session. The case that got it wrong: a
    /// session with no recorded start, pending alone, measured from its own last
    /// activity — every run ending before that instant is clipped away, the record
    /// goes out with null lists, and because the replica keeps one record per
    /// session it overwrites one that carried intervals with "no record". The stub
    /// here answers only what survives the horizon it was asked for, the way the
    /// local source does, and every run ends before the session's last activity.
    /// </summary>
    [Fact]
    public async Task A_session_with_no_recorded_start_still_goes_out_with_its_whole_record()
    {
        var earlier = (Noon.AddHours(-3), Noon.AddHours(-2));
        var activity = new ClippingAgentActivitySource(AgentActivities.Local(
            id: "unstarted",
            runs: [earlier],
            waits: [(Noon.AddHours(-2), Noon.AddMinutes(-90))]));

        using var fixture = Fixture.Create(
            sessions: [AgentSessions.Local(id: "unstarted", startedAt: null, lastActivityAt: Noon)],
            agentActivity: activity);

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.Equal([DateTimeOffset.MinValue], activity.Asked);

        var record = fixture.PushedRecord();
        var run = Assert.Single(record.GetProperty("runs").EnumerateArray());
        Assert.Equal(earlier.Item1, run.GetProperty("startedAt").GetDateTimeOffset());
        Assert.Single(record.GetProperty("waits").EnumerateArray());
    }

    /// <summary>
    /// The transcripts are read once per push, never once per batch or per record —
    /// the activity read is the expensive one — and from no later than the earliest
    /// recorded start among the sessions about to go, so no pending session's record
    /// is clipped on its way out.
    /// </summary>
    [Fact]
    public async Task The_activity_is_read_once_from_no_later_than_the_earliest_pending_start()
    {
        var activity = new StubAgentActivitySource();
        using var fixture = Fixture.Create(
            sessions:
            [
                AgentSessions.Local(id: "started", startedAt: Noon.AddHours(-3), lastActivityAt: Noon),
                AgentSessions.Local(id: "recent", startedAt: Noon.AddHours(-1), lastActivityAt: Noon.AddMinutes(1))
            ],
            agentActivity: activity);

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        var since = Assert.Single(activity.Asked);
        Assert.True(since <= Noon.AddHours(-3));
    }

    /// <summary>
    /// Nothing pending means nothing read. A device that is up to date runs this
    /// every five minutes, and parsing every transcript on the machine to attach
    /// activity to zero records would be the whole cost of the feature for nothing.
    /// </summary>
    [Fact]
    public async Task Nothing_pending_reads_no_activity()
    {
        var activity = new StubAgentActivitySource();
        using var fixture = Fixture.Create(
            sessions: [AgentSessions.Local(lastActivityAt: Noon.AddMinutes(-10))],
            state: Mine(Noon, null),
            agentActivity: activity);

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.Empty(activity.Asked);
        Assert.Empty(fixture.Handler.Requests);
    }

    /// <summary>
    /// A record is cut to the cap from the front, so what travels is the newest
    /// stretch of a long session. The service refuses a longer list rather than
    /// trimming it — a refusal is recoverable and a silent trim is not — so the
    /// pusher has to cut, and it cuts the oldest because a reader's window covers
    /// the end of a record before its beginning.
    /// </summary>
    [Fact]
    public async Task A_long_record_is_cut_to_the_cap_keeping_the_newest_intervals()
    {
        var cap = SessionRecordLimits.IntervalsPerList;

        // Ascending minute-wide runs, one more than the cap, so the oldest is the
        // one that has to go and the newest is the one that has to stay.
        var runs = Enumerable.Range(0, cap + 1)
            .Select(index => (Noon.AddMinutes(-2 * (cap + 1 - index)), Noon.AddMinutes(-2 * (cap + 1 - index) + 1)))
            .ToArray();

        using var fixture = Fixture.Create(
            sessions: [AgentSessions.Local(id: "long")],
            agentActivity: new StubAgentActivitySource(AgentActivities.Local(id: "long", runs: runs)));

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        var travelled = fixture.PushedRecord().GetProperty("runs").EnumerateArray().ToList();

        Assert.Equal(cap, travelled.Count);
        Assert.Equal(runs[1].Item1, travelled[0].GetProperty("startedAt").GetDateTimeOffset());
        Assert.Equal(runs[^1].Item2, travelled[^1].GetProperty("endedAt").GetDateTimeOffset());
    }

    /// <summary>
    /// A batch is bounded by weight as well as by count. Two hundred records is the
    /// count cap; four thousand intervals is the weight one, because a record can
    /// now carry a thousand of them and two hundred such records would be around
    /// 19 MB against the service's 1 MB body limit. The split lands before the
    /// record that would go over, and the watermark still advances per batch.
    /// </summary>
    [Fact]
    public async Task A_batch_is_flushed_before_the_record_that_would_exceed_the_interval_budget()
    {
        // Five sessions of a thousand intervals each — the most one record can carry.
        // Four fit the budget exactly; the fifth has to start a second batch.
        var sessions = Enumerable.Range(0, 5)
            .Select(index => AgentSessions.Local(id: $"heavy-{index}", lastActivityAt: Noon.AddMinutes(index)))
            .ToArray();

        var activity = sessions
            .Select(session => AgentActivities.Local(
                id: session.Id,
                runs: Stretches(SessionRecordLimits.IntervalsPerList),
                waits: Stretches(SessionRecordLimits.IntervalsPerList)))
            .ToArray();

        using var fixture = Fixture.Create(sessions: sessions, agentActivity: new StubAgentActivitySource(activity));

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, fixture.Handler.Requests.Count);
        Assert.Equal([4, 1], fixture.BatchSizes);
    }

    /// <summary>
    /// The count cap still applies on its own: two hundred light records are two
    /// hundred records, and the weight budget must not have replaced the count
    /// one.
    /// </summary>
    [Fact]
    public async Task Light_records_still_split_at_the_count_cap()
    {
        var sessions = Enumerable.Range(0, 201)
            .Select(index => AgentSessions.Local(id: $"light-{index}", lastActivityAt: Noon.AddSeconds(index)))
            .ToArray();

        using var fixture = Fixture.Create(sessions: sessions, agentActivity: new StubAgentActivitySource());

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.Equal([200, 1], fixture.BatchSizes);
    }

    /// <summary>Disjoint minute-wide stretches ending at <see cref="Noon"/>, ascending,
    /// as many as asked for.</summary>
    private static (DateTimeOffset, DateTimeOffset)[] Stretches(int count) =>
        [.. Enumerable.Range(0, count).Select(index => (Noon.AddMinutes(-2 * (count - index)), Noon.AddMinutes(-2 * (count - index) + 1)))];

    // --- The watermark ---------------------------------------------------------

    /// <summary>Only what moved after the watermark is offered. Everything the
    /// replica already has would land on the document it already wrote, so
    /// re-sending is safe — but a device that re-sent its whole history every five
    /// minutes would be doing it on somebody's battery.</summary>
    [Fact]
    public async Task Only_sessions_newer_than_the_watermark_are_pushed()
    {
        using var fixture = Fixture.Create(
            sessions:
            [
                AgentSessions.Local(id: "old", lastActivityAt: Noon.AddMinutes(-10)),
                AgentSessions.Local(id: "new", lastActivityAt: Noon.AddMinutes(10))
            ],
            state: Mine(Noon, null));

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("\"old\"", fixture.LastBody, StringComparison.Ordinal);
        Assert.Contains("\"new\"", fixture.LastBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// The watermark advances to the last stamp that was accepted and never to
    /// "now". A session that takes a turn while a batch is in flight carries a
    /// stamp between the two, and a watermark set to now would step over it — the
    /// far side would keep showing the session as it was before that turn, with
    /// nothing failing to say so.
    /// </summary>
    [Fact]
    public async Task The_watermark_advances_to_what_was_accepted_and_not_to_now()
    {
        var newest = Noon.AddMinutes(5);

        using var fixture = Fixture.Create(sessions: [AgentSessions.Local(lastActivityAt: newest)]);

        // Well past every stamp in the batch, so a watermark taken from the clock
        // would be visibly different from one taken from the records.
        fixture.Clock.Advance(TimeSpan.FromHours(1));

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(newest, fixture.State.Current.PushWatermark);
    }

    /// <summary>
    /// A batch the service refused leaves the watermark where it was. Advancing it
    /// on a failure is how a record is lost for good: it would never be selected
    /// again, and nothing about that shows up anywhere.
    /// </summary>
    [Fact]
    public async Task A_refused_batch_does_not_advance_the_watermark()
    {
        using var fixture = Fixture.Create(
            state: Mine(DateTimeOffset.MinValue, null),
            sessions: [AgentSessions.Local()],
            respond: (_, _) => StubHttpMessageHandler.Problem(
                HttpStatusCode.BadRequest, SyncErrorCodes.SessionInvalid, "No."));

        var result = await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SyncErrorCodes.SessionInvalid, result.Error.Code);
        Assert.Equal(DateTimeOffset.MinValue, fixture.State.Current.PushWatermark);
        Assert.Empty(fixture.State.Saved);
    }

    // --- The pull --------------------------------------------------------------

    /// <summary>
    /// This device's own records travel the feed like everybody else's, and keeping
    /// them would put a second copy of every local session in the pane — one read
    /// off disk and one that had been round trip. They are dropped on the machine
    /// id the service stamped, which is the only thing on the record this build
    /// could not have set itself.
    /// </summary>
    [Fact]
    public async Task The_device_drops_its_own_echo()
    {
        using var fixture = Fixture.Create(respond: (request, _) => request.Method == HttpMethod.Get
            ? Page(Entry(ThisDevice, "mine") + "," + Entry(OtherDevice, "theirs"), "cursor-1", hasMore: false)
            : StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":0}"""));

        var result = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Pulled);
        Assert.Equal(1, result.Value.Applied);

        var kept = Assert.Single(fixture.Replica.Saved);
        Assert.Equal("theirs", Assert.Single(kept).Record.SessionId);
    }

    /// <summary>
    /// The loop stops on <c>HasMore</c> and never on a page smaller than it asked
    /// for. The contract says a page size is a hint to the store rather than a
    /// promise, so a short page says nothing at all about what is behind it — and a
    /// device that read one as the end would stop, save the cursor, and only get
    /// the rest five minutes later. Twice, and it would never catch up.
    /// </summary>
    [Fact]
    public async Task The_pull_keeps_going_while_hasMore_even_on_a_short_page()
    {
        using var fixture = Fixture.Create(state: Mine(DateTimeOffset.MinValue, null), respond: (request, index) =>
        {
            if (request.Method != HttpMethod.Get)
            {
                return StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":0}""");
            }

            return index switch
            {
                // One record where a hundred were asked for, and more behind it.
                0 => Page(Entry(OtherDevice, "one"), "cursor-1", hasMore: true),
                // No records at all, and still more behind it: the feed's position
                // advances whether or not a page had anything in it.
                1 => Page(string.Empty, "cursor-2", hasMore: true),
                _ => Page(Entry(OtherDevice, "two"), "cursor-3", hasMore: false)
            };
        });

        var result = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Pulled);
        Assert.Equal(3, fixture.Handler.Requests.Count);

        // Saved after every page, so a pull interrupted half way resumes rather
        // than restarts.
        Assert.Equal(["cursor-1", "cursor-2", "cursor-3"], fixture.State.Saved.Select(state => state.PullCursor));
    }

    /// <summary>
    /// A cursor the store no longer resumes from is recovered from once, by
    /// forgetting it and reading the feed from the beginning — which is what a
    /// freshly paired device does anyway. Forgotten on disk before the retry, so a
    /// run that dies in between does not send it again either.
    /// </summary>
    [Fact]
    public async Task An_expired_cursor_is_forgotten_and_the_feed_is_read_from_the_beginning()
    {
        using var fixture = Fixture.Create(
            state: Mine(DateTimeOffset.MinValue, "v1.stale"),
            respond: (request, index) => index == 0
                ? StubHttpMessageHandler.Problem(
                    HttpStatusCode.BadRequest, SyncErrorCodes.SyncCursorExpired, "Start again.")
                : Page(Entry(OtherDevice, "one"), "cursor-1", hasMore: false));

        var result = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Pulled);
        Assert.Null(fixture.State.Saved[0].PullCursor);

        // On disk before the retry, so a run that dies in between does not send it
        // again either - and the retry itself carries no cursor at all.
        Assert.Contains("since=", fixture.Queries[0], StringComparison.Ordinal);
        Assert.DoesNotContain("since=", fixture.Queries[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// A correctly-signed cursor for another owner's feed is the one event
    /// .arc42/adr/0005 §Consequences asks to be loud about, so it is not on the
    /// list of things this device quietly starts over from.
    /// </summary>
    [Fact]
    public async Task A_cursor_belonging_to_somebody_else_is_not_recovered_from()
    {
        using var fixture = Fixture.Create(
            state: Mine(DateTimeOffset.MinValue, "v1.theirs"),
            respond: (_, _) => StubHttpMessageHandler.Problem(
                HttpStatusCode.Forbidden, SyncErrorCodes.SyncCursorNotYours, "Not yours."));

        var result = await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SyncErrorCodes.SyncCursorNotYours, result.Error.Code);
        Assert.Single(fixture.Handler.Requests);
    }

    /// <summary>A push that fails stops the exchange rather than being followed by
    /// a pull: the failure is almost always the service being unreachable, and a
    /// second call to say the same thing is a second thing for a person to
    /// read.</summary>
    [Fact]
    public async Task A_failed_push_stops_the_exchange()
    {
        using var fixture = Fixture.Create(
            sessions: [AgentSessions.Local()],
            respond: (_, _) => throw new HttpRequestException("No route to host."));

        var result = await fixture.Session.SyncAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Single(fixture.Handler.Requests);
    }

    // --- Whose progress this is -----------------------------------------------

    /// <summary>
    /// The symptom that found the whole thing: a device that had forgotten its
    /// credential and registered again kept the old owner's watermark, pushed only
    /// sessions newer than it, and the second machine to pair in saw none of the
    /// first one's. Progress recorded for another owner is started over.
    /// </summary>
    [Fact]
    public async Task Progress_recorded_for_another_owner_is_started_over()
    {
        using var fixture = Fixture.Create(
            sessions:
            [
                AgentSessions.Local(id: "old", lastActivityAt: Noon.AddMinutes(-10)),
                AgentSessions.Local(id: "new", lastActivityAt: Noon.AddMinutes(10))
            ],
            state: new SessionSyncState(Noon, "the-old-owners-cursor", AnotherOwner, ThisDevice));

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.Contains("\"old\"", fixture.LastBody, StringComparison.Ordinal);
        Assert.Contains("\"new\"", fixture.LastBody, StringComparison.Ordinal);
        Assert.Equal(ThisOwner, fixture.State.Current.OwnerId);
        Assert.Equal(ThisDevice, fixture.State.Current.DeviceId);
        Assert.Null(fixture.State.Current.PullCursor);
    }

    /// <summary>A state from before the identity was recorded is started over too,
    /// and stamped, so it happens once.</summary>
    [Fact]
    public async Task Progress_with_no_recorded_identity_is_started_over_and_stamped()
    {
        using var fixture = Fixture.Create(
            sessions: [AgentSessions.Local(id: "old", lastActivityAt: Noon.AddMinutes(-10))],
            state: new SessionSyncState(Noon, null));

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        Assert.Contains("\"old\"", fixture.LastBody, StringComparison.Ordinal);
        Assert.Equal(ThisOwner, fixture.State.Current.OwnerId);
    }

    // --- What the log says moved -----------------------------------------------

    /// <summary>
    /// A sent record is named in the log by the local session's title. The
    /// record on the wire carries no title — the whitelist leaves it behind on
    /// purpose — but the log is this machine's and the title never leaves it.
    /// </summary>
    [Fact]
    public async Task A_pushed_session_is_recorded_as_sent_under_its_local_title()
    {
        var activity = new SyncActivityLog();
        using var fixture = Fixture.Create(sessions: [AgentSessions.Local(title: "Fix the pairing dialog")], activity: activity);

        await fixture.Session.PushAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(activity.Snapshot());
        Assert.Equal(SyncDirection.Sent, entry.Direction);
        Assert.Equal(SyncItemKind.Session, entry.Kind);
        Assert.Equal("session-1", entry.Id);
        Assert.Equal("Fix the pairing dialog", entry.Title);
    }

    /// <summary>What arrives is named by where it ran, since that is all the
    /// record says and the thing a person wants to know about a session that is
    /// not theirs — and the device's own echo, which is dropped, is not listed
    /// as received either.</summary>
    [Fact]
    public async Task A_pulled_session_is_recorded_as_received_by_machine_and_the_echo_is_not()
    {
        var activity = new SyncActivityLog();
        using var fixture = Fixture.Create(
            respond: (request, _) => request.Method == HttpMethod.Get
                ? Page(Entry(ThisDevice, "mine") + "," + Entry(OtherDevice, "theirs"), "cursor-1", hasMore: false)
                : StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":0}"""),
            activity: activity);

        await fixture.Session.PullAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(activity.Snapshot());
        Assert.Equal(SyncDirection.Received, entry.Direction);
        Assert.Equal(SyncItemKind.Session, entry.Kind);
        Assert.Equal("theirs", entry.Id);
        Assert.Equal("claude session on Laptop · backlog on main", entry.Title);
    }

    // --- Fixture ---------------------------------------------------------------

    /// <summary>The JSON the service would send for one entry, serialized from the
    /// real contract rather than hand-written. A hand-written fixture drifts from
    /// the type it is standing in for, and the drift shows up as a test that goes
    /// on passing against a field the product no longer reads.</summary>
    private static string Entry(Guid machine, string id) =>
        JsonSerializer.Serialize(SessionRecords.Entry(machine, sessionId: id), Wire);

    private static HttpResponseMessage Page(string entries, string since, bool hasMore) =>
        StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            "{\"sessions\":[" + entries + "],\"since\":\"" + since + "\",\"hasMore\":"
            + (hasMore ? "true" : "false") + "}");

    /// <summary>What <c>PostAsJsonAsync</c> and <c>ReadFromJsonAsync</c> use when
    /// nobody says otherwise, so a fixture is serialized by the same rules the
    /// client reads it by.</summary>
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;
        private readonly List<string> _bodies;

        private Fixture(
            HttpClient http,
            StubHttpMessageHandler handler,
            SessionSyncSession session,
            InMemorySessionSyncStateStore state,
            InMemoryReplicatedSessionStore replica,
            FakeTimeProvider clock,
            List<string> bodies,
            List<string> queries)
        {
            _http = http;
            _bodies = bodies;

            Handler = handler;
            Session = session;
            State = state;
            Replica = replica;
            Clock = clock;
            Queries = queries;
        }

        /// <summary>The query string of every request, in order. Captured here
        /// rather than read off the recorded request, which keeps only the
        /// path.</summary>
        public List<string> Queries { get; }

        public StubHttpMessageHandler Handler { get; }

        public SessionSyncSession Session { get; }

        public InMemorySessionSyncStateStore State { get; }

        public InMemoryReplicatedSessionStore Replica { get; }

        public FakeTimeProvider Clock { get; }

        public string LastBody => _bodies.Count == 0 ? string.Empty : _bodies[^1];

        /// <summary>How many records each push body carried, in order — the shape
        /// of the batching, read off the bytes that went out.</summary>
        public List<int> BatchSizes =>
        [
            .. _bodies.Select(body =>
            {
                using var document = JsonDocument.Parse(body);
                return document.RootElement.GetProperty("sessions").GetArrayLength();
            })
        ];

        public static Fixture Create(
            AgentSession[]? sessions = null,
            Dictionary<string, string>? aliases = null,
            SessionSyncState? state = null,
            Func<HttpRequestMessage, int, HttpResponseMessage>? respond = null,
            SyncActivityLog? activity = null,
            IAgentActivitySource? agentActivity = null)
        {
            var bodies = new List<string>();
            var queries = new List<string>();

            var handler = new StubHttpMessageHandler((request, index) =>
            {
                queries.Add(request.RequestUri?.Query ?? string.Empty);

                if (request.Content is not null)
                {
                    bodies.Add(request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult());
                }

                if (respond is not null) return respond(request, index);

                return request.Method == HttpMethod.Post
                    ? StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":1}""")
                    : Page(string.Empty, "cursor-1", hasMore: false);
            });

            var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };
            var stateStore = new InMemorySessionSyncStateStore(state);
            var replica = new InMemoryReplicatedSessionStore();
            var clock = new FakeTimeProvider(Noon);

            // A paired device, because the pull drops its own echo on the device id
            // the credential carries and an unpaired one would keep everything.
            var credentials = new InMemoryDeviceCredentialStore(new DeviceCredential(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                ThisDevice,
                "Workshop PC",
                "a-registration-credential"));

            var session = new SessionSyncSession(
                new SessionSyncClient(http),
                new StubAgentSessionSource(sessions ?? []),
                new StubSessionRepositoryAliases(aliases),
                stateStore,
                replica,
                credentials,
                clock,
                activity,
                agentActivity);

            return new Fixture(http, handler, session, stateStore, replica, clock, bodies, queries);
        }

        /// <summary>The single record in the last push body. Every test using it
        /// pushes exactly one session, so a second would be a test asserting
        /// something it did not mean to.</summary>
        public JsonElement PushedRecord()
        {
            using var document = JsonDocument.Parse(LastBody);

            return document.RootElement.GetProperty("sessions")[0].Clone();
        }

        public void Dispose() => _http.Dispose();
    }
}
