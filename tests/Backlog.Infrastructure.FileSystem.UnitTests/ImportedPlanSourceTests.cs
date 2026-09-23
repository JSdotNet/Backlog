using Backlog.Infrastructure.FileSystem.Roadmap;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// What the shelf of plans not yet on the roadmap is built from: the backlog's
/// imported tasks, grouped by the plan they came in with and totalled. Asserted
/// over the pure grouping, without a store.
/// </summary>
public class ImportedPlanSourceTests
{
    private static TaskItemDto Entry(string? planId, int? effort, params string[] repoIds) =>
        new(
            Guid.NewGuid(),
            "Step",
            Body: "",
            EntryType.Task,
            Priority.Medium,
            EntryStatus.Ready,
            Area: null,
            Tags: [],
            Order: 0,
            TotalSubItems: 0,
            CompletedSubItems: 0,
            Projections: [],
            Effort: effort,
            RepoIds: repoIds,
            ImportPlanId: planId);

    [Fact]
    public void One_row_per_plan_id_with_its_count_effort_and_unestimated_tasks()
    {
        var plans = ImportedPlanSource.Summarise(
            [
                Entry("+release-q4", 3),
                Entry("+shelf", 2),
                Entry("+release-q4", 5),
                Entry("+release-q4", null)
            ],
            []);

        Assert.Collection(
            plans,
            first =>
            {
                Assert.Equal("release-q4", first.Tag);
                Assert.Equal(3, first.TaskCount);
                Assert.Equal(8, first.TotalEffort);
                Assert.Equal(1, first.UnestimatedCount);
            },
            second =>
            {
                Assert.Equal("shelf", second.Tag);
                Assert.Equal(1, second.TaskCount);
                Assert.Equal(2, second.TotalEffort);
                Assert.Equal(0, second.UnestimatedCount);
            });
    }

    [Fact]
    public void Only_a_plan_written_with_the_plan_sigil_is_answered()
    {
        // A task typed by hand has no plan id; a legacy plan's id is its bare tag.
        var plans = ImportedPlanSource.Summarise(
            [Entry(null, 1), Entry("legacy", 1), Entry("+", 1), Entry("+current", 1)],
            []);

        Assert.Equal("current", Assert.Single(plans).Tag);
    }

    [Fact]
    public void Repository_ids_become_the_aliases_the_plan_files_under_once_each()
    {
        var plans = ImportedPlanSource.Summarise(
            [
                Entry("+shelf", 1, "JSdotNet/Backlog"),
                Entry("+shelf", 1, "jsdotnet/backlog", "someone/unknown"),
                Entry("+shelf", 1, "someone/unknown")
            ],
            [new TasksRepositoryRef("backlog", "JSdotNet", "Backlog")]);

        // An id the registry does not know is kept as the task wrote it.
        Assert.Equal(new[] { "backlog", "someone/unknown" }, Assert.Single(plans).RepositoryAliases);
    }

    [Fact]
    public void The_effort_is_handed_to_the_import_command_in_its_own_shape()
    {
        var plan = Assert.Single(ImportedPlanSource.Summarise([Entry("+shelf", 5), Entry("+shelf", null)], []));

        Assert.Equal(new PlanTagEffortDto("shelf", 5, 1), plan.Effort);
    }
}
