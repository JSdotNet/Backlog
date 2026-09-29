using Backlog.Modules.Roadmap.Abstractions.Services;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// A roadmap document is not a task (local ADR 0018, Decision §3). It goes to
/// Roadmap's replication port whole — its text and its stamp — never to the task
/// store, and how it is counted follows what the port said.
/// </summary>
public sealed class TaskReplicaMergeRoadmapTests
{
    private static readonly Guid OtherPc = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Morning = new(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);

    private const string PlanJson = """{"version":1,"items":[],"milestones":[],"bands":{}}""";
    private const string PaceJson = """{"storyPointsPerWeek":12,"source":"Manual"}""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Both_documents_go_to_the_roadmap_whole_and_never_to_the_task_store()
    {
        var tasks = new InMemoryTaskStore();
        var roadmap = new RecordingRoadmapReplication();

        var merged = await new TaskReplicaMerge(tasks, roadmap: roadmap).ApplyAsync(
            [
                TaskChanges.Record(RoadmapReplicaChanges.Plan(PlanJson, Morning), OtherPc, 100),
                TaskChanges.Record(RoadmapReplicaChanges.Pace(PaceJson, Morning.AddMinutes(1)), OtherPc, 101),
            ],
            Cancellation);

        Assert.Equal(2, merged.Applied);
        Assert.Equal(0, merged.Skipped);
        Assert.Empty(tasks.Tasks);
        Assert.Empty(tasks.Writes);

        Assert.Collection(
            roadmap.Offered.OrderBy(offer => offer.Document),
            plan =>
            {
                Assert.Equal(RoadmapReplicaDocument.Plan, plan.Document);
                Assert.Equal(PlanJson, plan.Copy.Content);
                Assert.Equal(Morning, plan.Copy.UpdatedAt);
            },
            pace =>
            {
                Assert.Equal(RoadmapReplicaDocument.Pace, pace.Document);
                Assert.Equal(PaceJson, pace.Copy.Content);
                Assert.Equal(Morning.AddMinutes(1), pace.Copy.UpdatedAt);
            });
    }

    /// <summary>ADR 0018 Verification 2 and 3: an older copy and an echo write
    /// nothing, so they count as neither applied nor skipped.</summary>
    [Theory]
    [InlineData(RoadmapReplicaOutcome.Echo)]
    [InlineData(RoadmapReplicaOutcome.Refused)]
    public async Task An_echo_or_an_older_copy_counts_as_nothing_applied(RoadmapReplicaOutcome answer)
    {
        var roadmap = new RecordingRoadmapReplication { Answer = answer };
        var activity = new SyncActivityLog();

        var merged = await new TaskReplicaMerge(new InMemoryTaskStore(), activity: activity, roadmap: roadmap)
            .ApplyAsync([TaskChanges.Record(RoadmapReplicaChanges.Plan(PlanJson, Morning), OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 0), merged);
        Assert.Empty(activity.Snapshot());
    }

    /// <summary>ADR 0018 Verification 6: a payload that is not a plan counts
    /// Skipped, and the port has left the local plan as it was.</summary>
    [Fact]
    public async Task A_copy_the_roadmap_cannot_read_counts_skipped()
    {
        var roadmap = new RecordingRoadmapReplication { Answer = RoadmapReplicaOutcome.Unreadable };

        var merged = await new TaskReplicaMerge(new InMemoryTaskStore(), roadmap: roadmap)
            .ApplyAsync([TaskChanges.Record(RoadmapReplicaChanges.Plan("not a plan", Morning), OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 1), merged);
    }

    [Fact]
    public async Task A_taken_copy_is_recorded_as_a_roadmap_document_received()
    {
        var activity = new SyncActivityLog();
        var change = RoadmapReplicaChanges.Plan(PlanJson, Morning);

        await new TaskReplicaMerge(new InMemoryTaskStore(), activity: activity, roadmap: new RecordingRoadmapReplication())
            .ApplyAsync([TaskChanges.Record(change, OtherPc, 100)], Cancellation);

        var entry = Assert.Single(activity.Snapshot());
        Assert.Equal(SyncDirection.Received, entry.Direction);
        Assert.Equal(SyncItemKind.Roadmap, entry.Kind);
        Assert.Equal(RoadmapReplicaDocuments.PlanId.ToString("D"), entry.Id);
        Assert.Equal("Roadmap plan", entry.Title);
    }

    /// <summary>ADR 0018 Verification 7: a merge without the Roadmap port — what an
    /// older build is — counts both kinds Skipped and writes no task.</summary>
    [Fact]
    public async Task Without_the_roadmap_port_both_kinds_are_skipped_and_no_task_is_written()
    {
        var tasks = new InMemoryTaskStore();

        var merged = await new TaskReplicaMerge(tasks).ApplyAsync(
            [
                TaskChanges.Record(RoadmapReplicaChanges.Plan(PlanJson, Morning), OtherPc, 100),
                TaskChanges.Record(RoadmapReplicaChanges.Pace(PaceJson, Morning), OtherPc, 101),
            ],
            Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 2), merged);
        Assert.Empty(tasks.Tasks);
        Assert.Empty(tasks.Writes);
    }

    [Fact]
    public async Task A_task_in_the_same_page_still_lands_as_a_task()
    {
        var tasks = new InMemoryTaskStore();
        var task = TaskChanges.Change("Ordinary work", Morning);

        var merged = await new TaskReplicaMerge(tasks, roadmap: new RecordingRoadmapReplication()).ApplyAsync(
            [
                TaskChanges.Record(RoadmapReplicaChanges.Plan(PlanJson, Morning), OtherPc, 100),
                TaskChanges.Record(task, OtherPc, 101),
            ],
            Cancellation);

        Assert.Equal(2, merged.Applied);
        Assert.Equal([task.Id], tasks.Writes);
    }

    /// <summary>The wire shape of Decision §1: one constant id, the stamp, the kind
    /// token, the text verbatim, a title, the Tasks defaults, and never a
    /// tombstone.</summary>
    [Fact]
    public void A_roadmap_document_is_written_as_a_task_shaped_change()
    {
        var plan = RoadmapReplicaChanges.Plan(PlanJson, Morning);
        var pace = RoadmapReplicaChanges.Pace(PaceJson, Morning);

        Assert.Equal(RoadmapReplicaDocuments.PlanId, plan.Id);
        Assert.Equal(RoadmapReplicaDocuments.PaceId, pace.Id);
        Assert.NotEqual(plan.Id, pace.Id);
        Assert.Equal(Morning, plan.UpdatedAt);
        Assert.Null(plan.DeletedAt);
        Assert.Equal("roadmap-plan", plan.Task.Type);
        Assert.Equal("planning-pace", pace.Task.Type);
        Assert.Equal(PlanJson, plan.Task.ContentMd);
        Assert.Equal("Roadmap plan", plan.Task.Title);
        Assert.Equal("Planning pace", pace.Task.Title);
        Assert.Equal("draft", plan.Task.Status);
        Assert.Equal("medium", plan.Task.Priority);
        Assert.Empty(plan.Task.Tags);
        Assert.Null(plan.Task.SourceInboxId);
    }

    /// <summary>The ids are written on every device and a changed one forks the plan,
    /// so the literals are pinned here: a test that fails is the only warning anyone
    /// gets before two plans exist on the replica.</summary>
    [Fact]
    public void The_document_ids_never_change()
    {
        Assert.Equal(Guid.Parse("7c1a0b52-3d4e-4f6a-9b8c-0d1e2f3a4b01"), RoadmapReplicaDocuments.PlanId);
        Assert.Equal(Guid.Parse("7c1a0b52-3d4e-4f6a-9b8c-0d1e2f3a4b02"), RoadmapReplicaDocuments.PaceId);
    }
}
