using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Roadmap.Features.GetPlan;
using Backlog.Modules.Roadmap.Services;

namespace Backlog.Modules.Roadmap.UnitTests;

/// <summary>
/// The port the sync client hands the plan and the pace to (local ADR 0018,
/// Decision §3): one decision, by stamp, and a document it cannot read left alone.
/// The stores are doubles here — what a store writes is the adapters' own tests'
/// business; what is decided before it is asked to is this one's.
/// </summary>
public sealed class RoadmapReplicationTests
{
    private static readonly DateTimeOffset Morning = new(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_document_never_saved_here_reads_as_nothing()
    {
        var replication = new RoadmapReplication([new FakeReplicaStore(RoadmapReplicaDocument.Plan)], new RoadmapPlanChanges());

        Assert.Null(await replication.ReadAsync(RoadmapReplicaDocument.Plan, Cancellation));
    }

    [Fact]
    public async Task A_copy_with_no_local_one_is_taken_whole()
    {
        var store = new FakeReplicaStore(RoadmapReplicaDocument.Plan);
        var replication = new RoadmapReplication([store], new RoadmapPlanChanges());
        var inbound = new RoadmapReplicaCopyDto("""{"version":1,"items":[]}""", Morning);

        var outcome = await replication.ApplyAsync(RoadmapReplicaDocument.Plan, inbound, Cancellation);

        Assert.Equal(RoadmapReplicaOutcome.Taken, outcome);
        Assert.Equal(inbound, store.Stored);
    }

    /// <summary>ADR 0018 Verification 2: the newer save wins, stored with the inbound
    /// stamp rather than this machine's clock.</summary>
    [Fact]
    public async Task A_later_copy_replaces_the_local_one_at_its_own_stamp()
    {
        var store = new FakeReplicaStore(RoadmapReplicaDocument.Plan)
        {
            Stored = new RoadmapReplicaCopyDto("local", Morning),
        };
        var replication = new RoadmapReplication([store], new RoadmapPlanChanges());
        var inbound = new RoadmapReplicaCopyDto("remote", Morning.AddMinutes(1));

        var outcome = await replication.ApplyAsync(RoadmapReplicaDocument.Plan, inbound, Cancellation);

        Assert.Equal(RoadmapReplicaOutcome.Taken, outcome);
        Assert.Equal(inbound, store.Stored);
    }

    /// <summary>ADR 0018 Verification 2: the older pull writes nothing.</summary>
    [Fact]
    public async Task An_older_copy_is_refused_and_writes_nothing()
    {
        var store = new FakeReplicaStore(RoadmapReplicaDocument.Plan)
        {
            Stored = new RoadmapReplicaCopyDto("local", Morning),
        };
        var replication = new RoadmapReplication([store], new RoadmapPlanChanges());

        var outcome = await replication.ApplyAsync(
            RoadmapReplicaDocument.Plan, new RoadmapReplicaCopyDto("remote", Morning.AddMinutes(-1)), Cancellation);

        Assert.Equal(RoadmapReplicaOutcome.Refused, outcome);
        Assert.Equal(0, store.Writes);
        Assert.Equal("local", store.Stored!.Content);
    }

    /// <summary>ADR 0018 Verification 3: an echo writes no row and raises no
    /// local-change signal — nor tells the roadmap to redraw.</summary>
    [Fact]
    public async Task An_echo_writes_nothing_and_raises_nothing()
    {
        var changes = new RoadmapPlanChanges();
        var redraws = 0;
        changes.Changed += () => redraws++;
        var store = new FakeReplicaStore(RoadmapReplicaDocument.Plan)
        {
            Stored = new RoadmapReplicaCopyDto("mine", Morning),
        };
        var replication = new RoadmapReplication([store], changes);
        var local = 0;
        replication.Changed += () => local++;

        var outcome = await replication.ApplyAsync(
            RoadmapReplicaDocument.Plan, new RoadmapReplicaCopyDto("mine", Morning), Cancellation);

        Assert.Equal(RoadmapReplicaOutcome.Echo, outcome);
        Assert.Equal(0, store.Writes);
        Assert.Equal(0, local);
        Assert.Equal(0, redraws);
    }

    /// <summary>ADR 0018 Verification 6: a payload that is not a plan is skipped, and
    /// the local plan stays as it is.</summary>
    [Fact]
    public async Task A_copy_the_store_cannot_read_is_unreadable_and_leaves_the_local_one()
    {
        var store = new FakeReplicaStore(RoadmapReplicaDocument.Plan)
        {
            Stored = new RoadmapReplicaCopyDto("local", Morning),
            Readable = _ => false,
        };
        var changes = new RoadmapPlanChanges();
        var redraws = 0;
        changes.Changed += () => redraws++;
        var replication = new RoadmapReplication([store], changes);

        var outcome = await replication.ApplyAsync(
            RoadmapReplicaDocument.Plan, new RoadmapReplicaCopyDto("not a plan", Morning.AddHours(1)), Cancellation);

        Assert.Equal(RoadmapReplicaOutcome.Unreadable, outcome);
        Assert.Equal("local", store.Stored!.Content);
        Assert.Equal(0, redraws);
    }

    [Fact]
    public async Task A_document_this_head_keeps_no_store_for_is_unreadable()
    {
        var replication = new RoadmapReplication([new FakeReplicaStore(RoadmapReplicaDocument.Plan)], new RoadmapPlanChanges());

        var outcome = await replication.ApplyAsync(
            RoadmapReplicaDocument.Pace, new RoadmapReplicaCopyDto("{}", Morning), Cancellation);

        Assert.Equal(RoadmapReplicaOutcome.Unreadable, outcome);
        Assert.Null(await replication.ReadAsync(RoadmapReplicaDocument.Pace, Cancellation));
    }

    /// <summary>A taken copy redraws the roadmap — the band listens to the plan's own
    /// change notice — but is not announced as a local change, for the loop reason
    /// <c>ITaskChangeSignal.Suppress</c> gives (ADR 0018, Decision §3).</summary>
    [Fact]
    public async Task A_taken_copy_redraws_the_roadmap_without_announcing_a_local_change()
    {
        var changes = new RoadmapPlanChanges();
        var redraws = 0;
        changes.Changed += () => redraws++;
        var replication = new RoadmapReplication([new FakeReplicaStore(RoadmapReplicaDocument.Plan)], changes);
        var local = 0;
        replication.Changed += () => local++;

        await replication.ApplyAsync(RoadmapReplicaDocument.Plan, new RoadmapReplicaCopyDto("plan", Morning), Cancellation);

        Assert.Equal(1, redraws);
        Assert.Equal(0, local);
    }

    /// <summary>The pace file raises its own change when it is written; taken from
    /// the replica, that is not a local change either.</summary>
    [Fact]
    public async Task A_taken_pace_is_not_announced_as_a_local_change_either()
    {
        var pace = new FakePaceSettings();
        var store = new FakeReplicaStore(RoadmapReplicaDocument.Pace) { OnWrite = pace.RaiseChanged };
        var replication = new RoadmapReplication([store], new RoadmapPlanChanges(), pace);
        var local = 0;
        replication.Changed += () => local++;

        var outcome = await replication.ApplyAsync(
            RoadmapReplicaDocument.Pace, new RoadmapReplicaCopyDto("""{"storyPointsPerWeek":5}""", Morning), Cancellation);

        Assert.Equal(RoadmapReplicaOutcome.Taken, outcome);
        Assert.Equal(0, local);
    }

    /// <summary>A plan saved or a pace set on this machine is a local change, so a
    /// sync loop can push it within seconds.</summary>
    [Fact]
    public void A_plan_saved_or_a_pace_set_here_is_announced_as_a_local_change()
    {
        var changes = new RoadmapPlanChanges();
        var pace = new FakePaceSettings();
        var replication = new RoadmapReplication([], changes, pace);
        var local = 0;
        replication.Changed += () => local++;

        changes.Raise();
        pace.RaiseChanged();

        Assert.Equal(2, local);
    }

    /// <summary>ADR 0018, Consequences: the roadmap's first load follows a pull, so a
    /// device never draws — and saves over — the plan it held before the other PC's
    /// edit arrived. The read waits for the host's catch-up, then loads.</summary>
    [Fact]
    public async Task Reading_the_plan_waits_for_the_catch_up_before_it_loads()
    {
        var order = new List<string>();
        var handler = new GetPlanQueryHandler(new RecordingPlans(order), new RecordingCatchUp(order));

        await handler.Handle(new GetPlanQuery(), Cancellation);

        Assert.Equal(["catch-up", "load"], order);
    }

    [Fact]
    public async Task Reading_the_plan_without_a_catch_up_loads_at_once()
    {
        var order = new List<string>();

        await new GetPlanQueryHandler(new RecordingPlans(order)).Handle(new GetPlanQuery(), Cancellation);

        Assert.Equal(["load"], order);
    }

    private sealed class RecordingPlans(List<string> order) : IRoadmapPlanRepository
    {
        public Task<DomainModels.RoadmapPlan> LoadAsync(CancellationToken cancellationToken = default)
        {
            order.Add("load");
            return Task.FromResult(DomainModels.RoadmapPlan.Empty());
        }

        public Task SaveAsync(DomainModels.RoadmapPlan plan, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingCatchUp(List<string> order) : IRoadmapCatchUp
    {
        public async Task CatchUpAsync(CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            order.Add("catch-up");
        }
    }

    /// <summary>A store that holds one copy in memory and counts its writes.</summary>
    private sealed class FakeReplicaStore(RoadmapReplicaDocument document) : IRoadmapReplicaStore
    {
        public RoadmapReplicaDocument Document { get; } = document;

        public RoadmapReplicaCopyDto? Stored { get; set; }

        public int Writes { get; private set; }

        public Func<string, bool> Readable { get; init; } = _ => true;

        public Action? OnWrite { get; init; }

        public Task<RoadmapReplicaCopyDto?> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored);

        public Task<bool> TryWriteAsync(RoadmapReplicaCopyDto copy, CancellationToken cancellationToken = default)
        {
            if (!Readable(copy.Content)) return Task.FromResult(false);

            Stored = copy;
            Writes++;
            OnWrite?.Invoke();
            return Task.FromResult(true);
        }
    }

    /// <summary>Only the change notice is asked for; the rest is never reached.</summary>
    private sealed class FakePaceSettings : IPlanningVelocitySettings
    {
        public event Action? Changed;

        public void RaiseChanged() => Changed?.Invoke();

        public decimal Manual(string? repository = null) => 7m;

        public PaceSource Source(string? repository = null) => PaceSource.Manual;

        public string? SetManual(string? typed, string? repository = null) => null;

        public string? Choose(PaceSource source, string? repository = null) => null;

        public string? SetOwn(string? typed, string? repository = null) => null;
    }
}
