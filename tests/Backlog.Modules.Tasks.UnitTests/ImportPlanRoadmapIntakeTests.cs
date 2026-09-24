using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Features.ImportPlan;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// ADR 0013, ruling 3: one document may hold <c>plan</c> entries and task entries.
/// The task entries go down first by the unchanged ADR 0007 path; the <c>plan</c>
/// entries then cross to Roadmap through <see cref="IRoadmapPlanIntake"/>, with the
/// effort now gathered under every tag. What the roadmap does with them is Roadmap's
/// own test suite; these pin what Import hands it, when, and what it reports back.
/// </summary>
public sealed class ImportPlanRoadmapIntakeTests
{
    private const string MixedDocument =
        "# Imported plans on the roadmap\n`plan` `+myplan` `id:the-item` `*high` `due:2026-10-31` `repo:backlog`\n\n"
        + "What the plan is about.\n\n"
        + "# First prompt\n`prompt` `+myplan` `id:first` `effort:3`\n\n"
        + "# Second prompt\n`prompt` `+myplan` `id:second` `after:first` `effort:2`\n";

    [Fact]
    public async Task Plan_entries_cross_to_the_roadmap_after_the_tasks_are_written()
    {
        var store = new InMemoryTaskRepository();
        var intake = new RecordingIntake(store);

        var result = await Import(store, MixedDocument, intake);

        Assert.Equal(2, result.Created);
        var request = Assert.Single(intake.Requests);

        // Tasks first: when the roadmap was asked, both prompts were already stored.
        Assert.Equal(2, intake.TasksStoredWhenCalled);

        var entry = Assert.Single(request.Entries);
        Assert.Equal("Imported plans on the roadmap", entry.Title);
        Assert.Equal("+myplan", entry.Tag);
        Assert.Equal("the-item", entry.LocalId);
        Assert.Equal(Priority.High, entry.Priority);
        Assert.Equal(new DateOnly(2026, 10, 31), entry.Due);
        Assert.Equal(["backlog"], entry.RepositoryAliases);
        Assert.Equal("What the plan is about.", entry.Notes?.Trim());
        Assert.Empty(request.LayOutIfMissing);

        var effort = Assert.Single(request.GatheredEffort);
        Assert.Equal(new RoadmapPlanEffortDto("+myplan", 5, 0), effort);

        Assert.Equal(1, result.Roadmap!.Created);
    }

    /// <summary>The effort handed across is what the store now holds under the tag,
    /// not only what this document wrote: work already under way there counts, and
    /// so does a task with no estimate.</summary>
    [Fact]
    public async Task Gathered_effort_includes_what_was_already_stored_under_the_tag()
    {
        var store = new InMemoryTaskRepository();
        await Import(
            store,
            "# Earlier work\n`task` `+myplan` `effort:8` `!in-progress`\n\n# Unsized\n`task` `+myplan` `!in-progress`\n",
            intake: null);

        var intake = new RecordingIntake(store);
        await Import(store, MixedDocument, intake);

        var effort = Assert.Single(Assert.Single(intake.Requests).GatheredEffort);
        Assert.Equal(13, effort.TotalEffort);
        Assert.Equal(1, effort.UnestimatedCount);
    }

    /// <summary>A roadmap document naturally holds several plans, and so do the task
    /// entries under them: no tag is common to every one, so none of them had a plan
    /// id and a second import of the same document wrote every step again beside the
    /// first. Each entry's own plan tag is its plan when the document shares none,
    /// found in the desktop harness while validating the combined import.</summary>
    [Fact]
    public async Task Reimporting_a_document_of_several_plans_replaces_each_plans_steps()
    {
        const string twoPlans =
            "# Foundation\n`plan` `+foundation` `id:foundation`\n\n"
            + "# Rollout\n`plan` `+rollout` `id:rollout` `after:foundation`\n\n"
            + "# Schema\n`prompt` `+foundation` `id:schema` `effort:3`\n\n"
            + "# Store\n`prompt` `+foundation` `id:store` `after:schema` `effort:5`\n\n"
            + "# Announce\n`prompt` `+rollout` `id:announce` `effort:2`\n\n"
            + "# Migrate\n`prompt` `+rollout` `id:migrate` `after:announce` `effort:8`\n";

        var store = new InMemoryTaskRepository();
        var first = await Import(store, twoPlans, new RecordingIntake(store));

        Assert.Equal(4, first.Created);
        var byTitle = Live(store).ToDictionary(entry => entry.Title);
        Assert.Equal("+foundation", byTitle["Schema"].ImportPlanId);
        Assert.Equal("+rollout", byTitle["Migrate"].ImportPlanId);
        Assert.Equal([byTitle["Announce"].Id.ToString()], byTitle["Migrate"].DependsOn!);

        var second = await Import(store, twoPlans, new RecordingIntake(store));

        Assert.Equal(0, second.Created);
        Assert.Equal(4, second.Replaced);
        Assert.Equal(4, Live(store).Count);
    }

    [Fact]
    public async Task A_roadmap_refusal_is_reported_and_the_tasks_are_kept()
    {
        var store = new InMemoryTaskRepository();
        var intake = new RecordingIntake(store) { Refusal = "That would make a cycle." };

        var result = await Import(store, MixedDocument, intake);

        Assert.Equal(2, result.Created);
        Assert.Equal(2, Live(store).Count);
        Assert.Equal("That would make a cycle.", result.Roadmap!.Refusal);
        Assert.Equal(0, result.Roadmap.Created);
    }

    [Fact]
    public async Task A_task_after_naming_a_plan_entry_is_dropped_and_reported()
    {
        var store = new InMemoryTaskRepository();

        var result = await Import(
            store,
            "# The item\n`plan` `+myplan` `id:the-item`\n\n"
            + "# First prompt\n`prompt` `+myplan` `id:first` `after:the-item`\n\n"
            + "# Second prompt\n`prompt` `+myplan` `id:second` `after:first`\n",
            new RecordingIntake(store));

        var first = Assert.Single(Live(store), entry => entry.Title == "First prompt");
        var second = Assert.Single(Live(store), entry => entry.Title == "Second prompt");
        Assert.Empty(first.DependsOn ?? []);
        Assert.Equal([first.Id.ToString()], second.DependsOn!);

        var unresolved = Assert.Single(result.UnresolvedTaskDependencies!);
        Assert.Equal(new ImportUnresolvedDependencyDto("first", "the-item"), unresolved);
    }

    [Fact]
    public async Task A_plan_after_naming_a_task_is_not_sent_and_is_reported()
    {
        var store = new InMemoryTaskRepository();
        var intake = new RecordingIntake(store);

        var result = await Import(
            store,
            "# Earlier item\n`plan` `+earlier`\n\n"
            + "# The item\n`plan` `+myplan` `after:first` `after:earlier`\n\n"
            + "# First prompt\n`prompt` `+myplan` `id:first`\n",
            intake);

        var sent = Assert.Single(Assert.Single(intake.Requests).Entries, entry => entry.Tag == "+myplan");
        Assert.Equal(["earlier"], sent.After);
        Assert.Contains(new ImportUnresolvedDependencyDto("+myplan", "first"), result.Roadmap!.UnresolvedDependencies);
    }

    [Fact]
    public async Task A_plan_entry_without_exactly_one_plan_tag_is_skipped_and_reported()
    {
        var store = new InMemoryTaskRepository();
        var intake = new RecordingIntake(store);

        var result = await Import(
            store,
            "# No tag\n`plan`\n\n# Two tags\n`plan` `+one` `+two`\n\n# Just right\n`plan` `+one` `effort:5`\n",
            intake);

        Assert.Equal(["Just right"], Assert.Single(intake.Requests).Entries.Select(entry => entry.Title));
        Assert.Equal(["No tag", "Two tags"], result.Roadmap!.Skipped);
        Assert.Equal(["Just right"], result.Roadmap.EffortIgnored);
    }

    /// <summary>A roadmap-only document is a plan Import can act on: nothing about
    /// tasks is touched, not even the not-yet-started entries of a plan sharing the
    /// tag — the clear-then-write scope is the task entries' shared tag, and there
    /// are none.</summary>
    [Fact]
    public async Task A_document_of_plan_entries_alone_writes_no_task_and_clears_none()
    {
        var store = new InMemoryTaskRepository();
        await Import(store, "# First prompt\n`prompt` `+myplan` `id:first`\n", intake: null);

        var intake = new RecordingIntake(store);
        var result = await Import(store, "# The item\n`plan` `+myplan`\n", intake);

        Assert.Equal((0, 0, 0, 0, 0), (result.Created, result.Replaced, result.Updated, result.Skipped, result.Removed));
        Assert.Equal(["First prompt"], Live(store).Select(entry => entry.Title));
        Assert.Single(Assert.Single(intake.Requests).Entries);
    }

    [Fact]
    public async Task Lay_out_on_the_roadmap_makes_one_entry_per_plan_tag()
    {
        var store = new InMemoryTaskRepository();
        var intake = new RecordingIntake(store);

        await Import(
            store,
            "# One\n`prompt` `+release-q4` `repo:backlog`\n\n"
            + "# Two\n`prompt` `+release-q4` `repo:site` `#chore`\n\n"
            + "# Three\n`prompt` `+docs_refresh`\n",
            intake,
            layOut: true);

        var request = Assert.Single(intake.Requests);
        Assert.Empty(request.Entries);
        Assert.Collection(
            request.LayOutIfMissing,
            release =>
            {
                Assert.Equal("Release q4", release.Title);
                Assert.Equal("+release-q4", release.Tag);
                Assert.Equal(["backlog/backlog", "site/site"], release.RepositoryAliases);
            },
            docs =>
            {
                Assert.Equal("Docs refresh", docs.Title);
                Assert.Empty(docs.RepositoryAliases);
            });
    }

    /// <summary>Off by default: the task-only document still crosses, with no entries,
    /// so an effort-placed item under its tag is re-lengthened (ruling 5) — but nothing
    /// is made up.</summary>
    [Fact]
    public async Task Without_lay_out_a_task_only_document_sends_effort_and_no_entries()
    {
        var store = new InMemoryTaskRepository();
        var intake = new RecordingIntake(store);

        var result = await Import(store, "# One\n`prompt` `+myplan` `effort:3`\n", intake);

        var request = Assert.Single(intake.Requests);
        Assert.Empty(request.Entries);
        Assert.Empty(request.LayOutIfMissing);
        Assert.Equal(new RoadmapPlanEffortDto("+myplan", 3, 0), Assert.Single(request.GatheredEffort));
        Assert.Null(result.Roadmap); // nothing happened on the roadmap, so nothing is said
    }

    [Fact]
    public async Task Lay_out_is_ignored_for_a_document_that_wrote_plan_entries()
    {
        var store = new InMemoryTaskRepository();
        var intake = new RecordingIntake(store);

        await Import(store, MixedDocument, intake, layOut: true);

        Assert.Empty(Assert.Single(intake.Requests).LayOutIfMissing);
    }

    [Fact]
    public async Task A_task_import_with_no_plan_tag_does_not_cross()
    {
        var store = new InMemoryTaskRepository();
        var intake = new RecordingIntake(store);

        var result = await Import(store, "# One\n`prompt` `#myplan`\n", intake, layOut: true);

        Assert.Empty(intake.Requests);
        Assert.Null(result.Roadmap);
    }

    [Fact]
    public async Task Without_a_roadmap_the_tasks_import_and_the_plan_entries_are_reported()
    {
        var store = new InMemoryTaskRepository();

        var result = await Import(store, MixedDocument, intake: null);

        Assert.Equal(2, result.Created);
        Assert.Equal(ImportPlanCommandHandler.RoadmapUnavailable, result.Roadmap!.Refusal);
    }

    [Theory]
    [InlineData("+roadmap-imported-plans", "Roadmap imported plans")]
    [InlineData("+docs_refresh", "Docs refresh")]
    [InlineData("+x", "X")]
    public void A_made_up_title_reads_from_the_tag(string tag, string title) =>
        Assert.Equal(title, ImportPlanCommandHandler.TitleFromTag(tag));

    [Fact]
    public void The_preview_counts_roadmap_items_and_tasks()
    {
        var preview = ImportPlanPreview.Of(MixedDocument + "# Other\n`plan` `+other`\n\n# Untagged\n`plan`\n", layOutOnRoadmap: false);

        Assert.Equal(new ImportPlanPreview(2, 2, HasPlanEntries: true), preview);
    }

    [Fact]
    public void The_preview_counts_laid_out_tags_only_when_asked()
    {
        const string tasks = "# One\n`prompt` `+a`\n\n# Two\n`prompt` `+a` `+b`\n\n# Three\n`prompt` `#c`\n";

        Assert.Equal(new ImportPlanPreview(0, 3, false), ImportPlanPreview.Of(tasks, layOutOnRoadmap: false));
        Assert.Equal(new ImportPlanPreview(2, 3, false), ImportPlanPreview.Of(tasks, layOutOnRoadmap: true));
    }

    private static async Task<ImportPlanResultDto> Import(
        InMemoryTaskRepository store,
        string rawText,
        IRoadmapPlanIntake? intake,
        bool layOut = false)
    {
        var handler = new ImportPlanCommandHandler(store, new FakeRepositoryDirectory(), intake);
        var result = await handler.Handle(new ImportPlanCommand(rawText, LayOutOnRoadmap: layOut));

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static List<TaskItem> Live(InMemoryTaskRepository store) =>
        [.. store.Entries.Values.Where(entry => entry.DeletedAt is null)];

    /// <summary>Records what crossed and how many tasks were stored at that moment,
    /// and answers as a roadmap that created one item per entry would.</summary>
    private sealed class RecordingIntake(InMemoryTaskRepository store) : IRoadmapPlanIntake
    {
        public List<RoadmapPlanIntakeRequestDto> Requests { get; } = [];

        public int TasksStoredWhenCalled { get; private set; }

        public string? Refusal { get; init; }

        public Task<RoadmapIntakeResultDto> LayOutAsync(
            RoadmapPlanIntakeRequestDto request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            TasksStoredWhenCalled = store.Entries.Values.Count(entry => entry.DeletedAt is null);

            return Task.FromResult(Refusal is { } refusal
                ? RoadmapIntakeResultDto.Refused(refusal)
                : RoadmapIntakeResultDto.Empty with { Created = request.Entries.Count + request.LayOutIfMissing.Count });
        }
    }

    private sealed class InMemoryTaskRepository : ITaskRepository
    {
        public Dictionary<Guid, TaskItem> Entries { get; } = [];

        public Task SaveAsync(TaskItem entry, CancellationToken cancellationToken = default)
        {
            Entries[entry.Id] = entry;
            return Task.CompletedTask;
        }

        public Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entries.TryGetValue(id, out var entry) && entry.DeletedAt is null ? entry : null);

        public Task<TaskItem?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Entries.TryGetValue(id, out var entry) ? entry : null);

        public Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>([.. Entries.Values.Where(entry => entry.DeletedAt is null)]);

        public Task<IReadOnlyList<TaskItem>> ListChangedSinceAsync(
            DateTimeOffset since,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItem>>(
                [.. Entries.Values.Where(entry => entry.UpdatedAt > since).OrderBy(entry => entry.UpdatedAt)]);
    }
}
