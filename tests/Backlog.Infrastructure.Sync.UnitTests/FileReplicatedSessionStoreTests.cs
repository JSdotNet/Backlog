using Backlog.Infrastructure.Sync.Sessions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The cache the pull writes into: what it keeps, what it replaces, what it throws
/// away, and what it says about what it threw away.
/// <para>
/// The cursor has already moved past everything in here, so a record this store
/// silently loses is a record the service will never send again. That is the
/// failure every test in this file is about, and none of it announces itself.
/// </para>
/// </summary>
public sealed class FileReplicatedSessionStoreTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Laptop = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly Guid Desktop = Guid.Parse("44444444-4444-4444-4444-444444444444");

    /// <summary>An instant far enough past every fixture record that the history
    /// retains none of them, so the facts about the count cap are about the count cap
    /// alone. The facts about the history read at <see cref="Noon"/> instead.</summary>
    private static readonly DateTimeOffset Later = Noon + ReplicatedSessionLimits.History + TimeSpan.FromDays(1);

    /// <summary>
    /// A session pushed again with a later reading replaces the earlier one rather
    /// than sitting beside it. The feed is a log of readings, not of events, so two
    /// rows for one session would show it twice with the older row claiming an
    /// activity time that has been superseded.
    /// </summary>
    [Fact]
    public void A_later_reading_of_a_session_replaces_the_earlier_one()
    {
        var merged = FileReplicatedSessionStore.Merge(
            ReplicatedSessions.Empty,
            [
                SessionRecords.Entry(Laptop, sessionId: "one", lastActivityAt: Noon, serverTimestamp: 1),
                SessionRecords.Entry(Laptop, sessionId: "one", lastActivityAt: Noon.AddMinutes(5), serverTimestamp: 2)
            ],
            Later);

        var kept = Assert.Single(merged.Entries);

        Assert.Equal(Noon.AddMinutes(5), kept.Record.LastActivityAt);
        Assert.Equal(0, merged.Dropped);
    }

    /// <summary>
    /// Two agents may issue the same session id, and two machines may too, so
    /// identity is all three together. Collapsing on the id alone would merge
    /// unrelated sessions — the one failure
    /// <c>.devbook/domain/sessions/domain.md#session-identity</c> exists to prevent.
    /// </summary>
    [Fact]
    public void One_session_id_on_two_agents_or_two_machines_is_not_one_session()
    {
        var merged = FileReplicatedSessionStore.Merge(
            ReplicatedSessions.Empty,
            [
                SessionRecords.Entry(Laptop, sessionId: "shared", agentKind: "claude"),
                SessionRecords.Entry(Laptop, sessionId: "shared", agentKind: "copilot"),
                SessionRecords.Entry(Desktop, sessionId: "shared", agentKind: "claude")
            ],
            Later);

        Assert.Equal(3, merged.Entries.Count);
    }

    /// <summary>
    /// The cap is per environment per agent, so a machine that has been busy cannot
    /// crowd a quiet one out of the list — the same argument
    /// <see cref="Backlog.Modules.Sessions.Abstractions.AgentSessionLimits"/> makes
    /// for splitting its cap per agent rather than over the merged list.
    /// </summary>
    [Fact]
    public void The_cap_is_per_environment_and_per_agent()
    {
        var busy = Enumerable
            .Range(0, ReplicatedSessionLimits.PerEnvironmentPerAgent + 10)
            .Select(index => SessionRecords.Entry(
                Laptop,
                sessionId: $"laptop-{index}",
                lastActivityAt: Noon.AddMinutes(index)));

        var quiet = SessionRecords.Entry(Desktop, sessionId: "desktop-1");

        var merged = FileReplicatedSessionStore.Merge(ReplicatedSessions.Empty, [.. busy, quiet], Later);

        Assert.Equal(ReplicatedSessionLimits.PerEnvironmentPerAgent + 1, merged.Entries.Count);
        Assert.Contains(merged.Entries, entry => entry.Record.SessionId == "desktop-1");
        Assert.Equal(10, merged.Dropped);
    }

    /// <summary>The cap takes the oldest, so the list a person opens is the most
    /// recent activity rather than whatever happened to arrive first.</summary>
    [Fact]
    public void The_cap_discards_the_least_recently_active()
    {
        var entries = Enumerable
            .Range(0, ReplicatedSessionLimits.PerEnvironmentPerAgent + 1)
            .Select(index => SessionRecords.Entry(
                Laptop,
                sessionId: $"session-{index}",
                lastActivityAt: Noon.AddMinutes(index)))
            .ToList();

        var merged = FileReplicatedSessionStore.Merge(ReplicatedSessions.Empty, entries, Later);

        Assert.DoesNotContain(merged.Entries, entry => entry.Record.SessionId == "session-0");
    }

    /// <summary>
    /// What an earlier run's cap discarded is gone from the file, so the count is
    /// the only remaining evidence that it existed. Recomputing it from what is left
    /// would report a truncated history as the whole of it.
    /// </summary>
    [Fact]
    public void An_earlier_runs_dropped_count_is_carried_forward()
    {
        var merged = FileReplicatedSessionStore.Merge(
            new ReplicatedSessions([], Dropped: 7),
            [SessionRecords.Entry(Laptop)],
            Later);

        Assert.Equal(7, merged.Dropped);
    }

    /// <summary>
    /// The count cap never cuts into the history. A machine that ran more than the
    /// cap's worth of sessions inside twelve weeks keeps every one of them here, or
    /// the Dashboard's count for that machine would be the cap — the "200" this store
    /// used to answer on every busy machine.
    /// </summary>
    [Fact]
    public void Everything_inside_the_history_is_kept_whatever_the_cap()
    {
        const int inside = ReplicatedSessionLimits.PerEnvironmentPerAgent + 40;

        var recent = Enumerable
            .Range(0, inside)
            .Select(index => SessionRecords.Entry(Laptop, sessionId: $"laptop-{index}", lastActivityAt: Noon.AddHours(-index)))
            .ToList();

        var merged = FileReplicatedSessionStore.Merge(ReplicatedSessions.Empty, recent, Noon);

        Assert.Equal(inside, merged.Entries.Count);
        Assert.Equal(0, merged.Dropped);
    }

    /// <summary>
    /// And the history never cuts into the count: a quiet machine's newest hundred
    /// stay, however old, so the inventory still has its list to show for a machine
    /// that has done nothing this quarter.
    /// </summary>
    [Fact]
    public void The_newest_records_are_kept_whatever_the_history()
    {
        var old = Enumerable
            .Range(0, ReplicatedSessionLimits.PerEnvironmentPerAgent + 5)
            .Select(index => SessionRecords.Entry(
                Laptop,
                sessionId: $"laptop-{index}",
                lastActivityAt: Noon - ReplicatedSessionLimits.History - TimeSpan.FromDays(index + 1)))
            .ToList();

        var merged = FileReplicatedSessionStore.Merge(ReplicatedSessions.Empty, old, Noon);

        Assert.Equal(ReplicatedSessionLimits.PerEnvironmentPerAgent, merged.Entries.Count);
        Assert.Equal(5, merged.Dropped);
        Assert.Contains(merged.Entries, entry => entry.Record.SessionId == "laptop-0");
    }

    /// <summary>A record active exactly at the edge of the history is inside it: the
    /// Dashboard's window is closed at its start, and a store that dropped the boundary
    /// record would answer a count one short of the window's own scoping.</summary>
    [Fact]
    public void A_record_exactly_at_the_edge_of_the_history_is_inside_it()
    {
        var edge = Noon - ReplicatedSessionLimits.History;

        var entries = Enumerable
            .Range(0, ReplicatedSessionLimits.PerEnvironmentPerAgent)
            .Select(index => SessionRecords.Entry(Laptop, sessionId: $"laptop-{index}", lastActivityAt: Noon.AddMinutes(-index)))
            .Append(SessionRecords.Entry(Laptop, sessionId: "edge", lastActivityAt: edge))
            .Append(SessionRecords.Entry(Laptop, sessionId: "beyond", lastActivityAt: edge.AddSeconds(-1)))
            .ToList();

        var merged = FileReplicatedSessionStore.Merge(ReplicatedSessions.Empty, entries, Noon);

        Assert.Contains(merged.Entries, entry => entry.Record.SessionId == "edge");
        Assert.DoesNotContain(merged.Entries, entry => entry.Record.SessionId == "beyond");
    }

    /// <summary>
    /// A round trip through a real file, because the JSON shape is what a next
    /// launch depends on and nothing else here would notice a property that failed
    /// to serialize.
    /// </summary>
    [Fact]
    public void Records_survive_a_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"backlog-replicated-{Guid.NewGuid():N}", "replicated-sessions.json");

        try
        {
            var store = new FileReplicatedSessionStore(path, new FakeTimeProvider(Noon));
            store.Save([SessionRecords.Entry(Laptop, sessionId: "one", machineName: "Kitchen laptop")]);

            var reopened = new FileReplicatedSessionStore(path, new FakeTimeProvider(Noon));
            var kept = Assert.Single(reopened.Current.Entries);

            Assert.Equal("one", kept.Record.SessionId);
            Assert.Equal("Kitchen laptop", kept.Record.MachineName);
            Assert.Equal(Laptop, kept.MachineId);
        }
        finally
        {
            var folder = Path.GetDirectoryName(path);
            if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// The runs and waits survive the file too, and null survives as null. The
    /// file is the only copy this device has of another machine's intervals — the
    /// cursor has moved past them — and a list that failed to serialise would have
    /// that machine measured at nothing on every launch after the first.
    /// </summary>
    [Fact]
    public void Intervals_survive_a_restart_and_null_stays_null()
    {
        var path = Path.Combine(Path.GetTempPath(), $"backlog-replicated-{Guid.NewGuid():N}", "replicated-sessions.json");

        try
        {
            var store = new FileReplicatedSessionStore(path, new FakeTimeProvider(Noon));
            store.Save(
            [
                SessionRecords.Entry(Laptop, sessionId: "measured", runs: [new(Noon.AddMinutes(-10), Noon)], waits: []),
                SessionRecords.Entry(Laptop, sessionId: "unmeasured")
            ]);

            var reopened = new FileReplicatedSessionStore(path, new FakeTimeProvider(Noon));

            var measured = reopened.Current.Entries.Single(entry => entry.Record.SessionId == "measured").Record;
            Assert.Equal([new ActivityInterval(Noon.AddMinutes(-10), Noon)], measured.Runs);
            Assert.NotNull(measured.Waits);
            Assert.Empty(measured.Waits);

            var unmeasured = reopened.Current.Entries.Single(entry => entry.Record.SessionId == "unmeasured").Record;
            Assert.Null(unmeasured.Runs);
            Assert.Null(unmeasured.Waits);
        }
        finally
        {
            var folder = Path.GetDirectoryName(path);
            if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// A file written before the two lists existed loads with null in both, which
    /// is the same thing a current pusher sends for a session it has no record for.
    /// Nothing in the store had to change for that: the record's positional
    /// constructor defaults the two, so an older file is an ordinary file rather
    /// than one that reads as empty and loses everything the cursor has passed.
    /// </summary>
    [Fact]
    public void A_file_from_before_the_intervals_existed_loads_with_null_lists()
    {
        var path = Path.Combine(Path.GetTempPath(), $"backlog-replicated-{Guid.NewGuid():N}", "replicated-sessions.json");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                """
                {"entries":[{"record":{"sessionId":"old","agentKind":"claude","machineName":"Laptop",
                "repositoryAlias":"backlog","branch":"main","startedAt":null,
                "lastActivityAt":"2026-09-07T12:00:00+00:00","turnCount":3,"durationSeconds":0},
                "machineId":"33333333-3333-3333-3333-333333333333","serverTimestamp":1}],"dropped":0}
                """);

            var store = new FileReplicatedSessionStore(path, new FakeTimeProvider(Noon));

            var kept = Assert.Single(store.Current.Entries);
            Assert.Equal("old", kept.Record.SessionId);
            Assert.Null(kept.Record.Runs);
            Assert.Null(kept.Record.Waits);
        }
        finally
        {
            var folder = Path.GetDirectoryName(path);
            if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// A file that cannot be read reads as empty rather than throwing. It loses
    /// records the cursor has moved past, which is a real loss — and one this class
    /// cannot repair, because refusing to start would leave the person unable to ask
    /// for the feed to be read again.
    /// </summary>
    [Fact]
    public void An_unreadable_file_reads_as_nothing_held()
    {
        var path = Path.Combine(Path.GetTempPath(), $"backlog-replicated-{Guid.NewGuid():N}", "replicated-sessions.json");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ this is not json");

            var store = new FileReplicatedSessionStore(path, new FakeTimeProvider(Noon));

            Assert.Empty(store.Current.Entries);
        }
        finally
        {
            var folder = Path.GetDirectoryName(path);
            if (folder is not null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
