using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Adapters;
using Backlog.Modules.Sync.DomainModels;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// The session replica's own contract, away from HTTP: what appending twice
/// does, what the feed promises when nothing has happened, and what it does with
/// a cursor that should never have reached it.
/// <para>
/// These are worth having separately from the endpoint tests because the
/// in-memory adapter is the store every one of those runs against. If it were
/// wrong in the same direction the handlers are, the endpoint suite would agree
/// with itself and prove nothing.
/// </para>
/// </summary>
public class InMemorySessionReplicaTests
{
    private static readonly OwnerId Mine = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly OwnerId Theirs = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));

    private static readonly OwnerScope Desktop = new(Mine, new DeviceId(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001")));
    private static readonly OwnerScope Laptop = new(Mine, new DeviceId(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002")));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>The machine id on the way out is the scope's, because a
    /// <see cref="SessionRecord"/> has nowhere to carry one of its own.</summary>
    [Fact]
    public async Task An_appended_record_is_stamped_with_the_scopes_machine()
    {
        var replica = new InMemorySessionReplica();

        Assert.Equal(1, await replica.Append(Desktop, [Session("s-1")], Cancellation));

        var entry = Assert.Single((await replica.ReadChanges(Mine, null, 10, Cancellation)).Sessions);
        Assert.Equal(Desktop.DeviceId.Value, entry.MachineId);
    }

    /// <summary>
    /// Single-writer, held structurally. The record is keyed on the machine id
    /// first, so two machines reporting the same session id keep two records and
    /// neither can reach the other's — which is .arc42/adr/0005 §Session records'
    /// rule surviving even a bug in the code that stamps the id.
    /// </summary>
    [Fact]
    public async Task Two_machines_reporting_one_session_id_keep_two_records()
    {
        var replica = new InMemorySessionReplica();

        await replica.Append(Desktop, [Session("shared") with { MachineName = "JS-DESKTOP" }], Cancellation);
        await replica.Append(Laptop, [Session("shared") with { MachineName = "JS-LAPTOP" }], Cancellation);

        var page = await replica.ReadChanges(Mine, null, 10, Cancellation);

        Assert.Equal(2, page.Sessions.Count);
        Assert.Equal(2, page.Sessions.Select(entry => entry.MachineId).Distinct().Count());
    }

    /// <summary>Two agents may issue the same session id, and
    /// .domain/sessions/naming.md#session-identity says that is two sessions.
    /// Keying on the id alone would show one row where there were two.</summary>
    [Fact]
    public async Task Two_agents_reporting_one_session_id_keep_two_records()
    {
        var replica = new InMemorySessionReplica();

        await replica.Append(
            Desktop,
            [Session("shared") with { AgentKind = "claude" }, Session("shared") with { AgentKind = "copilot" }],
            Cancellation);

        Assert.Equal(2, (await replica.ReadChanges(Mine, null, 10, Cancellation)).Sessions.Count);
    }

    /// <summary>Append-only in the sense the record means: later evidence about
    /// the same session replaces that machine's own record and moves it to the
    /// end of the feed, rather than accumulating a second one or editing a
    /// history nobody may edit.</summary>
    [Fact]
    public async Task Re_appending_a_session_replaces_that_machines_record()
    {
        var replica = new InMemorySessionReplica();

        await replica.Append(Desktop, [Session("s-1") with { TurnCount = 3 }], Cancellation);
        await replica.Append(Desktop, [Session("s-1") with { TurnCount = 9 }], Cancellation);

        var entry = Assert.Single((await replica.ReadChanges(Mine, null, 10, Cancellation)).Sessions);
        Assert.Equal(9, entry.Record.TurnCount);
    }

    /// <summary>
    /// The feed advances over a quiet poll. Without this a client polling a fleet
    /// nobody is working on would rescan from its old position on every poll, for
    /// ever — and on this feed a quiet poll is the ordinary case rather than the
    /// edge one.
    /// </summary>
    [Fact]
    public async Task The_feed_advances_on_an_empty_page_so_a_poller_does_not_rescan()
    {
        var replica = new InMemorySessionReplica();

        await replica.Append(Desktop, [Session("s-1")], Cancellation);

        var first = await replica.ReadChanges(Mine, null, 10, Cancellation);
        Assert.Single(first.Sessions);

        var quiet = await replica.ReadChanges(Mine, first.Cursor, 10, Cancellation);
        Assert.Empty(quiet.Sessions);
        Assert.False(quiet.HasMore);

        await replica.Append(Desktop, [Session("s-2")], Cancellation);

        var resumed = await replica.ReadChanges(Mine, quiet.Cursor, 10, Cancellation);
        Assert.Equal("s-2", Assert.Single(resumed.Sessions).Record.SessionId);
    }

    /// <summary>An owner with nothing still gets a cursor. A page with no cursor
    /// would leave a freshly paired device with nothing to send back next
    /// time.</summary>
    [Fact]
    public async Task An_owner_with_no_records_still_gets_a_cursor()
    {
        var page = await new InMemorySessionReplica().ReadChanges(Theirs, null, 10, Cancellation);

        Assert.Empty(page.Sessions);
        Assert.False(page.HasMore);
        Assert.False(string.IsNullOrEmpty(page.Cursor.Continuation));
        Assert.Equal(Theirs, page.Cursor.Owner);
    }

    /// <summary>Nesting by owner means another owner's records are unreachable
    /// rather than filtered out — there is no predicate here to forget.</summary>
    [Fact]
    public async Task One_owner_never_sees_another()
    {
        var replica = new InMemorySessionReplica();

        await replica.Append(Desktop, [Session("s-1")], Cancellation);

        Assert.Empty((await replica.ReadChanges(Theirs, null, 10, Cancellation)).Sessions);
    }

    /// <summary>
    /// A cursor for another owner reaching the replica is a bug in the caller,
    /// not something a person did: the handler verifies it first. It throws rather
    /// than answering an empty page, because an empty page looks exactly like a
    /// drained feed and would hide the bug — which is the failure
    /// .arc42/adr/0005 §Consequences says must not pass quietly.
    /// </summary>
    [Fact]
    public async Task A_cursor_for_another_owner_is_a_bug_and_says_so()
    {
        var replica = new InMemorySessionReplica();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => replica.ReadChanges(Mine, new SessionReplicaCursor(Theirs, "0"), 10, Cancellation));
    }

    /// <summary>A full page claims there may be more; the take is exact, so it
    /// never claims a drained feed while records are left.</summary>
    [Fact]
    public async Task A_full_page_says_there_may_be_more()
    {
        var replica = new InMemorySessionReplica();

        await replica.Append(Desktop, [Session("s-1"), Session("s-2"), Session("s-3")], Cancellation);

        var page = await replica.ReadChanges(Mine, null, 2, Cancellation);

        Assert.Equal(2, page.Sessions.Count);
        Assert.True(page.HasMore);

        Assert.Single((await replica.ReadChanges(Mine, page.Cursor, 2, Cancellation)).Sessions);
    }

    private static SessionRecord Session(string sessionId) => new(
        sessionId,
        AgentKind: "claude",
        MachineName: "JS-DESKTOP",
        RepositoryAlias: "backlog",
        Branch: "main",
        StartedAt: new DateTimeOffset(2026, 9, 8, 9, 0, 0, TimeSpan.Zero),
        LastActivityAt: new DateTimeOffset(2026, 9, 8, 10, 30, 0, TimeSpan.Zero),
        TurnCount: 42,
        DurationSeconds: 5_400);
}
