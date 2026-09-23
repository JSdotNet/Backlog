using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

public sealed class AgentSessionRecordStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "agent-session-record-store-tests-" + Guid.NewGuid().ToString("N"));

    private static readonly DateTimeOffset Noon = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private AgentSessionRecordStore Store() => new(() => _root);

    /// <summary>A record comes back with everything it was written with — the local
    /// half the sync record never carries included — and reads as a record rather than
    /// a live session.</summary>
    [Fact]
    public void A_record_written_is_a_record_read_back()
    {
        var store = Store();

        var update = store.Save([Record("s-1", title: "Rewrite the pairing dialog copy", hits: [Hit(Noon.AddMinutes(-1))])]);

        Assert.Equal(new SessionRecordUpdate(1, 0), update);

        var read = Assert.Single(store.All());
        Assert.Equal("Rewrite the pairing dialog copy", read.Session.Title);
        Assert.Equal(@"D:\Repos\Backlog", read.Session.WorkingFolder);
        Assert.Equal("backlog", read.Session.ResolvedRepository);
        Assert.Equal(AgentSessionOrigin.Recorded, read.Session.Origin);
        Assert.Equal(AgentSessionState.Finished, read.Session.State);

        var hit = Assert.Single(read.Activity!.LimitHits);
        Assert.Equal(AgentLimitKind.FiveHour, hit.Kind);
        Assert.Equal("org_spend_cap_reached", hit.OverageDisabledReason);
        Assert.False(hit.IsUsingOverage);
    }

    /// <summary>A second reading amends the record rather than replacing it: a title the
    /// reading lost is kept, a hit only the record held is kept, and the count says one
    /// record was amended rather than started.</summary>
    [Fact]
    public void A_second_reading_amends_the_record()
    {
        var store = Store();

        store.Save([Record("s-1", title: "First name", hits: [Hit(Noon.AddHours(-3))])]);
        var update = store.Save([Record("s-1", title: "", hits: [Hit(Noon.AddMinutes(-1))])]);

        Assert.Equal(new SessionRecordUpdate(0, 1), update);

        var read = Assert.Single(store.All());
        Assert.Equal("First name", read.Session.Title);
        Assert.Equal([Noon.AddHours(-3), Noon.AddMinutes(-1)], read.Activity!.LimitHits.Select(hit => hit.At));
    }

    /// <summary>A file this build cannot read is somebody's only record of a session. It
    /// is left exactly as it is, never overwritten by a reading of the same session.</summary>
    [Fact]
    public void An_unreadable_record_is_never_overwritten()
    {
        var store = Store();
        store.Save([Record("s-1")]);

        var file = Assert.Single(Directory.GetFiles(_root, "*.json"));
        File.WriteAllText(file, "{ not json");

        var update = store.Save([Record("s-1")]);

        Assert.Equal(SessionRecordUpdate.None, update);
        Assert.Equal("{ not json", File.ReadAllText(file));
        Assert.Empty(store.All());
    }

    /// <summary>Two agents issuing one session id are two records.</summary>
    [Fact]
    public void Two_agents_with_one_session_id_are_two_records()
    {
        var store = Store();

        store.Save([Record("same"), Record("same", kind: AgentSessionKind.Copilot)]);

        Assert.Equal(2, store.All().Count);
    }

    private static AgentSessionRecord Record(
        string id,
        string title = "A session",
        AgentSessionKind kind = AgentSessionKind.Claude,
        IReadOnlyList<AgentLimitHit>? hits = null) =>
        new(
            new AgentSession(
                id,
                kind,
                "11111111-1111-1111-1111-111111111111",
                "Workshop PC",
                title,
                @"D:\Repos\Backlog",
                null,
                "main",
                Noon.AddHours(-4),
                Noon,
                AgentSessionState.Running,
                7,
                AgentSessionOrigin.Local)
            {
                ResolvedRepository = "backlog"
            },
            new AgentSessionActivity(
                id,
                kind,
                "11111111-1111-1111-1111-111111111111",
                "Workshop PC",
                [new AgentActivityRun(Noon.AddHours(-4), Noon)],
                [])
            {
                LimitHits = hits ?? []
            },
            Noon);

    private static AgentLimitHit Hit(DateTimeOffset at) =>
        new(at, AgentLimitKind.FiveHour, "five_hour")
        {
            ResetsAt = at.AddHours(3),
            OverageStatus = "rejected",
            OverageDisabledReason = "org_spend_cap_reached",
            IsUsingOverage = false
        };
}
