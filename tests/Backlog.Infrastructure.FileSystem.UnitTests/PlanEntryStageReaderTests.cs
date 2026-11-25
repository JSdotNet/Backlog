using Backlog.Infrastructure.FileSystem.Tasks;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The stage each plan entry's session reports (local ADR 0062 §2), off the delivery runs:
/// a run under way or parked that names an entry by id gives it the stage it is on, as
/// <c>Review · 8 of 9</c>, and nothing else gives an entry a stage.
/// </summary>
public sealed class PlanEntryStageReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);

    private static readonly string[] Names =
        ["Update Base", "Scope", "Implement", "Review", "Build & Test", "Verify", "Spec Check", "Ready", "Personal Validation"];

    private static DeliveryRun Run(string status, int at, Guid? entry, string atStatus = DeliveryStageStatuses.InProgress, DateTimeOffset? updated = null) =>
        new(
            Id: Guid.NewGuid().ToString(),
            Dashboard: "backlog",
            Worktree: "wt-00000000",
            WorktreeName: "wt",
            EnvironmentId: "tower",
            Environment: "DEV-TOWER",
            SkillId: "flow-code",
            Title: "A run",
            Status: status,
            ChangeKind: "feature",
            References: entry is { } id
                ? [new DeliveryRunReference(DeliveryRunReferenceKind.Task, id.ToString(), null, null, "JSdotNet/Backlog", "plan-first-tasks", id)]
                : [],
            StartedAt: Now.AddHours(-1),
            UpdatedAt: updated ?? Now,
            Stages: [.. Names.Select((name, index) => new DeliveryRunStage(
                name,
                index < at ? DeliveryStageStatuses.Done : index == at ? atStatus : DeliveryStageStatuses.Pending,
                null,
                index < at ? 1 : 0))],
            TokenUsage: null,
            Context: null,
            InsightsByCategory: [],
            InsightsByServer: []);

    [Fact]
    public void A_run_under_way_gives_its_entry_the_stage_in_progress_and_its_place()
    {
        var entry = Guid.NewGuid();

        var stages = PlanEntryStageReader.StagesOf([Run(DeliveryRunStatuses.InProgress, 3, entry)]);

        Assert.Equal("Review · 4 of 9", stages[entry]);
    }

    [Fact]
    public void Between_stages_a_run_is_at_its_first_not_yet_reached()
    {
        var entry = Guid.NewGuid();

        var stages = PlanEntryStageReader.StagesOf([Run(DeliveryRunStatuses.InProgress, 4, entry, atStatus: DeliveryStageStatuses.Pending)]);

        Assert.Equal("Build & Test · 5 of 9", stages[entry]);
    }

    [Fact]
    public void A_parked_run_is_at_the_stage_it_parked_on()
    {
        var entry = Guid.NewGuid();

        var stages = PlanEntryStageReader.StagesOf([Run(DeliveryRunStatuses.Parked, 8, entry, atStatus: DeliveryStageStatuses.Blocked)]);

        Assert.Equal("Personal Validation · 9 of 9", stages[entry]);
    }

    [Theory]
    [InlineData(DeliveryRunStatuses.Done)]
    [InlineData(DeliveryRunStatuses.Cancelled)]
    public void A_finished_run_reports_no_stage(string status)
    {
        Assert.Empty(PlanEntryStageReader.StagesOf([Run(status, 3, Guid.NewGuid())]));
    }

    [Fact]
    public void A_run_naming_no_entry_or_with_no_stages_reports_nothing()
    {
        var bare = Run(DeliveryRunStatuses.InProgress, 3, Guid.NewGuid()) with { Stages = [] };

        Assert.Empty(PlanEntryStageReader.StagesOf([Run(DeliveryRunStatuses.InProgress, 3, entry: null), bare]));
    }

    [Fact]
    public void Of_two_runs_on_one_entry_the_latest_updated_wins()
    {
        var entry = Guid.NewGuid();

        var stages = PlanEntryStageReader.StagesOf(
        [
            Run(DeliveryRunStatuses.InProgress, 2, entry, updated: Now.AddMinutes(-30)),
            Run(DeliveryRunStatuses.InProgress, 5, entry, updated: Now),
        ]);

        Assert.Equal("Verify · 6 of 9", stages[entry]);
    }

    [Fact]
    public async Task Without_a_run_source_nothing_reports()
    {
        var stages = await new PlanEntryStageReader(runs: null, changes: null).ReadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(stages);
    }

    [Fact]
    public async Task The_reader_maps_the_source_s_runs_and_passes_on_its_change_signal()
    {
        var entry = Guid.NewGuid();
        var signal = new DeliveryRunChangeSignal();
        var reader = new PlanEntryStageReader(new HeldRuns([Run(DeliveryRunStatuses.InProgress, 3, entry)]), signal);
        var heard = 0;
        reader.Changed += () => heard++;

        var stages = await reader.ReadAsync(TestContext.Current.CancellationToken);
        signal.Raise();

        Assert.Equal("Review · 4 of 9", stages[entry]);
        Assert.Equal(1, heard);
    }

    private sealed class HeldRuns(IReadOnlyList<DeliveryRun> runs) : IDeliveryRunSource
    {
        public Task<DeliveryRunCatalog> GetRunsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeliveryRunCatalog(runs, []));
    }
}
