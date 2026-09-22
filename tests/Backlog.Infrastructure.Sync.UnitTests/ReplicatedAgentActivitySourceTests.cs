using Backlog.Infrastructure.Sync.Sessions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The read side of activity: what another environment's runs and waits look like
/// once they are an activity record on this device.
/// <para>
/// Every assertion here is about a figure the Dashboard would otherwise get wrong
/// on somebody else's behalf — a machine measured at zero because its record was
/// dropped, a stretch counted from before the window because it was not clipped,
/// a record this device pushed and received back going out again under its own
/// id. None of those fails visibly if it is got wrong.
/// </para>
/// </summary>
public sealed class ReplicatedAgentActivitySourceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Laptop = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>
    /// A record with intervals becomes an activity record for the same session by
    /// identity: the agent's own id and kind, so the Dashboard's join against the
    /// session list finds it, with the intervals exactly as they travelled.
    /// </summary>
    [Fact]
    public async Task A_record_with_intervals_is_answered_as_activity_for_the_same_session()
    {
        var source = Source(SessionRecords.Entry(
            Laptop,
            sessionId: "abc-123",
            agentKind: "copilot",
            runs: [new(Noon.AddMinutes(-30), Noon.AddMinutes(-20)), new(Noon.AddMinutes(-10), Noon)],
            waits: []));

        var activity = Assert.Single((await source.GetActivityAsync(Noon.AddDays(-1), TestContext.Current.CancellationToken)).Sessions);

        Assert.Equal("abc-123", activity.Id);
        Assert.Equal(AgentSessionKind.Copilot, activity.Kind);
        Assert.Equal(
            [new AgentActivityRun(Noon.AddMinutes(-30), Noon.AddMinutes(-20)), new AgentActivityRun(Noon.AddMinutes(-10), Noon)],
            activity.Runs);
        Assert.Empty(activity.Waits);
    }

    /// <summary>
    /// The environment is keyed on the id the service stamped and labelled with the
    /// name the record carried, spelled the way the local source spells it — an
    /// activity record and a session row are meant to be the same machine by
    /// identity, and two spellings of one id is exactly how that stops being true.
    /// And it is stamped <see cref="AgentSessionOrigin.Replicated"/> on the way out,
    /// which is what stops the push sending it back under this machine's id.
    /// </summary>
    [Fact]
    public async Task Every_record_is_stamped_with_its_machine_and_as_replicated()
    {
        var source = Source(SessionRecords.Entry(
            Laptop,
            machineName: "Kitchen laptop",
            runs: [new(Noon.AddMinutes(-10), Noon)]));

        var activity = Assert.Single((await source.GetActivityAsync(Noon.AddDays(-1), TestContext.Current.CancellationToken)).Sessions);

        Assert.Equal(Laptop.ToString(), activity.EnvironmentId);
        Assert.Equal("Kitchen laptop", activity.Environment);
        Assert.Equal(AgentSessionOrigin.Replicated, activity.Origin);
    }

    /// <summary>
    /// A record that travelled with null in both lists is a session the pushing
    /// machine had no activity record for, and it is absent here rather than
    /// present-and-empty — the same rule the local source reads its own folders
    /// under, and the difference is what the Dashboard counts as unrecorded.
    /// </summary>
    [Fact]
    public async Task A_record_without_intervals_is_absent_rather_than_empty()
    {
        var source = Source(SessionRecords.Entry(Laptop, runs: null, waits: null));

        var log = await source.GetActivityAsync(Noon.AddDays(-1), TestContext.Current.CancellationToken);

        Assert.Empty(log.Sessions);
    }

    /// <summary>
    /// Clipped to the horizon rather than dropped at it, the way
    /// <c>LocalAgentActivitySource</c> clips: a run that began before the window
    /// reports the part of itself inside it, a run that ended on or before the
    /// horizon has no part inside and goes, and a run wholly inside is untouched.
    /// </summary>
    [Fact]
    public async Task Intervals_are_clipped_to_the_horizon_and_not_dropped_at_it()
    {
        var since = Noon.AddHours(-1);

        var source = Source(SessionRecords.Entry(
            Laptop,
            runs:
            [
                new(Noon.AddHours(-3), Noon.AddHours(-2)),
                new(Noon.AddHours(-2), since),
                new(Noon.AddMinutes(-90), Noon.AddMinutes(-30)),
                new(Noon.AddMinutes(-10), Noon)
            ],
            waits: [new(Noon.AddMinutes(-70), Noon.AddMinutes(-50))]));

        var activity = Assert.Single((await source.GetActivityAsync(since, TestContext.Current.CancellationToken)).Sessions);

        Assert.Equal(
            [new AgentActivityRun(since, Noon.AddMinutes(-30)), new AgentActivityRun(Noon.AddMinutes(-10), Noon)],
            activity.Runs);
        Assert.Equal([new AgentActivityWait(since, Noon.AddMinutes(-50))], activity.Waits);
    }

    /// <summary>
    /// A record whose whole activity fell before the horizon has nothing to
    /// contribute to a duration, and is absent rather than present with two empty
    /// lists — an entry with no intervals would be a session claiming to have been
    /// measured inside a window it was never in.
    /// </summary>
    [Fact]
    public async Task A_record_with_nothing_inside_the_horizon_is_absent()
    {
        var source = Source(SessionRecords.Entry(
            Laptop,
            runs: [new(Noon.AddHours(-3), Noon.AddHours(-2))],
            waits: [new(Noon.AddHours(-2), Noon.AddHours(-1))]));

        var log = await source.GetActivityAsync(Noon, TestContext.Current.CancellationToken);

        Assert.Empty(log.Sessions);
    }

    /// <summary>
    /// Ascending on the way out whatever order they arrived in. The contract
    /// promises the consumer ordered, disjoint, ascending lists, and the wire is
    /// not somewhere that promise can be trusted to have been kept — a store that
    /// re-serialised a document, or a client that was not ours, may hand them back
    /// in any order at all.
    /// </summary>
    [Fact]
    public async Task Intervals_are_sorted_rather_than_trusted()
    {
        var source = Source(SessionRecords.Entry(
            Laptop,
            runs: [new(Noon.AddMinutes(-10), Noon), new(Noon.AddMinutes(-30), Noon.AddMinutes(-20))],
            waits: [new(Noon.AddMinutes(-20), Noon.AddMinutes(-10)), new(Noon.AddMinutes(-40), Noon.AddMinutes(-30))]));

        var activity = Assert.Single((await source.GetActivityAsync(Noon.AddDays(-1), TestContext.Current.CancellationToken)).Sessions);

        Assert.Equal(Noon.AddMinutes(-30), activity.Runs[0].StartedAt);
        Assert.Equal(Noon.AddMinutes(-10), activity.Runs[1].StartedAt);
        Assert.Equal(Noon.AddMinutes(-40), activity.Waits[0].StartedAt);
        Assert.Equal(Noon.AddMinutes(-20), activity.Waits[1].StartedAt);
    }

    /// <summary>
    /// The log says what it is: the horizon it was asked for, no unreadable
    /// sources, no subagents, and no opinion on the fold threshold. Sub-agent
    /// activity does not travel, so a concurrency figure stays a local one; the
    /// threshold is the pushing machine's and is assumed to be the same everywhere
    /// rather than becoming a thirteenth field, so this source has nothing of its
    /// own to say about it.
    /// </summary>
    [Fact]
    public async Task The_log_names_no_unreadable_source_no_subagents_and_no_threshold()
    {
        var since = Noon.AddDays(-7);
        var source = Source(SessionRecords.Entry(Laptop, runs: [new(Noon.AddMinutes(-10), Noon)]));

        var log = await source.GetActivityAsync(since, TestContext.Current.CancellationToken);

        Assert.Equal(since, log.Since);
        Assert.Empty(log.Unreadable);
        Assert.Empty(log.Subagents);
        Assert.Equal(TimeSpan.Zero, log.IdleAfter);
    }

    /// <summary>
    /// Switching the feature off has to take the other machines' figures off the
    /// Dashboard, not merely stop new ones arriving — a switch that leaves what it
    /// gathered on display is a switch a person cannot see the effect of. The cache
    /// is left alone, so switching it back on shows what was already there.
    /// </summary>
    [Fact]
    public async Task Nothing_is_answered_while_the_feature_is_off()
    {
        var store = new InMemoryReplicatedSessionStore(SessionRecords.Entry(Laptop, runs: [new(Noon.AddMinutes(-10), Noon)]));
        var features = new StubFeatureSettings(enabled: false);
        var source = new ReplicatedAgentActivitySource(store, features);

        Assert.Empty((await source.GetActivityAsync(Noon.AddDays(-1), TestContext.Current.CancellationToken)).Sessions);

        _ = features.SetEnabled("session-sync", true);

        Assert.Single((await source.GetActivityAsync(Noon.AddDays(-1), TestContext.Current.CancellationToken)).Sessions);
    }

    /// <summary>A source over a fixed set of records with the feature on.</summary>
    private static ReplicatedAgentActivitySource Source(params SessionRecordEntry[] entries) =>
        new(new InMemoryReplicatedSessionStore(entries), new StubFeatureSettings(enabled: true));
}
