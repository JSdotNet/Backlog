using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Brain.UnitTests;

/// <summary>
/// Picking work up from a Brain note
/// (<c>.devbook/domain/brain/features.md#brain-mode-of-the-devbook-pane</c>) against
/// real note files and the real link writer: Create task files a draft task in the
/// Brain scope or the note's repositories, pointing back at the note, and lists it in
/// the note's <c>tasks</c>; Plan this project drafts a plan from the note and the
/// notes it links to, lays it out with the note as its Knowledge Ref, and names it in
/// the note's <c>roadmap</c>. A note whose <c>meta</c> block does not parse keeps the
/// work and says the link was not written.
/// </summary>
public sealed class BrainPickupTests : IDisposable
{
    private readonly string _brain = Path.Combine(Path.GetTempPath(), "brain-pickup-tests-" + Guid.NewGuid().ToString("N"));

    public BrainPickupTests() => Directory.CreateDirectory(_brain);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_brain, recursive: true);
        }
        catch (IOException)
        {
            // Left for the OS to clear.
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Note(string relativePath, string text)
    {
        var path = Path.Combine([_brain, .. relativePath.Split('/')]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private string Read(string relativePath) => File.ReadAllText(Path.Combine([_brain, .. relativePath.Split('/')]));

    private BrainPickup Pickup(RecordingTaskItems? tasks, IInboxPlanDrafter? drafter = null, string? brainFolder = "<default>")
    {
        var source = new BrainFolderSource(() => brainFolder == "<default>" ? _brain : brainFolder);
        return new BrainPickup(tasks, source, new BrainNoteLinkWriter(source), drafter, new KnownRepositories());
    }

    private const string Plain = """
        # Fix the gutter

        ```meta
        type: note
        tasks: []
        ```

        Ask about the ladder.

        - [ ] Buy a ladder
        """;

    [Fact]
    public async Task Create_task_files_a_draft_in_the_brain_scope_pointing_at_the_note_and_lists_it()
    {
        Note("areas/home.md", Plain);
        var tasks = new RecordingTaskItems();

        var result = await Pickup(tasks).CreateTaskAsync("areas/home.md", cancellationToken: Ct);

        Assert.Equal(BrainPickupOutcome.Linked, result.Outcome);
        var save = Assert.Single(tasks.Saves);
        Assert.Equal("# Fix the gutter\n`task` `!draft`\n", save.RawText);
        var id = Assert.Single(result.TaskIds);
        Assert.Equal(["#brain:areas/home.md"], tasks.References[id]);
        Assert.Contains($"tasks: [{id:D}]", Read("areas/home.md"));
        Assert.Contains("Ask about the ladder.", Read("areas/home.md"));
    }

    [Fact]
    public async Task Create_task_from_a_selected_line_takes_the_line_as_its_title()
    {
        Note("areas/home.md", Plain);
        var tasks = new RecordingTaskItems();

        await Pickup(tasks).CreateTaskAsync("areas/home.md", "  - [ ] Buy a   ladder\nand more", Ct);

        Assert.StartsWith("# Buy a ladder\n", Assert.Single(tasks.Saves).RawText);
    }

    [Fact]
    public async Task Create_task_on_a_note_naming_repositories_files_one_task_in_each()
    {
        Note("projects/sync.md", """
            # Sync rewrite

            ```meta
            repos: [JSdotNet/Backlog, JSdotNet/Sync]
            ```
            """);
        var tasks = new RecordingTaskItems();

        var result = await Pickup(tasks).CreateTaskAsync("projects/sync.md", cancellationToken: Ct);

        Assert.Equal(BrainPickupOutcome.Linked, result.Outcome);
        Assert.Collection(tasks.Saves,
            save => Assert.Contains("`repo:JSdotNet/Backlog`", save.RawText),
            save => Assert.Contains("`repo:JSdotNet/Sync`", save.RawText));
        var note = Read("projects/sync.md");
        Assert.Contains($"tasks: [{result.TaskIds[0]:D}, {result.TaskIds[1]:D}]", note);
    }

    [Fact]
    public async Task A_note_whose_meta_block_does_not_parse_keeps_the_task_and_says_the_link_was_not_written()
    {
        const string broken = "# Broken\n\n```meta\ntasks: [a\n```\n";
        Note("areas/broken.md", broken);
        var tasks = new RecordingTaskItems();

        var result = await Pickup(tasks).CreateTaskAsync("areas/broken.md", cancellationToken: Ct);

        Assert.Equal(BrainPickupOutcome.NotLinked, result.Outcome);
        Assert.Single(tasks.Saves);
        Assert.Single(result.TaskIds);
        Assert.Contains("The link was not written into the note", result.Message);
        Assert.Equal(broken, Read("areas/broken.md"));
    }

    [Fact]
    public async Task Without_a_brain_or_a_backlog_both_actions_are_unavailable_with_a_reason()
    {
        var noBrain = Pickup(new RecordingTaskItems(), new FixedDrafter("# x\n"), brainFolder: null);
        Assert.Equal(BrainPickup.NoBrain, noBrain.CreateTaskUnavailableReason);
        Assert.Equal(BrainPickup.NoBrain, noBrain.PlanUnavailableReason);

        var noBacklog = Pickup(tasks: null, new FixedDrafter("# x\n"));
        Assert.Equal(BrainPickup.NoBacklog, noBacklog.CreateTaskUnavailableReason);

        var refused = await noBrain.CreateTaskAsync("areas/home.md", cancellationToken: Ct);
        Assert.Equal(BrainPickupOutcome.Failed, refused.Outcome);
    }

    [Fact]
    public void Without_a_drafter_only_plan_is_unavailable()
    {
        var none = Pickup(new RecordingTaskItems());
        Assert.Null(none.CreateTaskUnavailableReason);
        Assert.Equal(BrainPickup.NoDrafter, none.PlanUnavailableReason);

        var unconfigured = Pickup(new RecordingTaskItems(), new FixedDrafter("# x\n", available: false));
        Assert.Equal("Set the endpoint first.", unconfigured.PlanUnavailableReason);
    }

    private const string Project = """
        # Move house

        ```meta
        type: note
        repos: [JSdotNet/Backlog]
        related: [resources/movers.md, "#brain:people/sam.md#phone", repo:backlog/.devbook/arc42/01.md]
        roadmap: []
        ```

        Out by June.
        """;

    [Fact]
    public async Task Plan_this_project_drafts_from_the_note_and_its_links_imports_under_a_plan_entry_and_names_the_tag()
    {
        Note("projects/move.md", Project);
        Note("resources/movers.md", "# Movers\n\nCall Acme.\n");
        Note("people/sam.md", "# Sam\n\nKnows a van.\n");
        var tasks = new RecordingTaskItems();
        var drafter = new FixedDrafter("""
            # Rough plan
            `plan` `+whatever`

            # Book movers
            `task` `!draft` `+tag` `id:book` `repo:JSdotNet/Backlog`

            # Pack
            `task` `!ready` `+tag` `#home` `id:pack` `after:book`
            """);

        var result = await Pickup(tasks, drafter).PlanProjectAsync("projects/move.md", Ct);

        Assert.Equal(BrainPickupOutcome.Linked, result.Outcome);
        var request = Assert.Single(drafter.Requests);
        Assert.Equal("Move house", request.Title);
        Assert.Equal(["JSdotNet/Backlog"], request.RepoIds);
        Assert.Contains("Out by June.", request.BodyMd);
        Assert.Contains("Call Acme.", request.BodyMd);
        Assert.Contains("Knows a van.", request.BodyMd);
        Assert.Equal(result.RoadmapTag, request.PlanTag);
        Assert.StartsWith("move-house-", result.RoadmapTag);

        var import = Assert.Single(tasks.Imports);
        var segments = EntryTextParser.SplitSegments(import).Select(EntryTextParser.Parse).ToList();
        var plan = Assert.Single(segments, segment => segment.Kind == EntryKind.Plan);
        Assert.Equal("Move house", plan.Title.Trim());
        Assert.Contains($"+{result.RoadmapTag}", plan.Tags);
        Assert.Equal(["#brain:projects/move.md"], plan.DevbookReferences);
        Assert.DoesNotContain(segments, segment => segment.Title.Trim() == "Rough plan");
        var steps = segments.Where(segment => segment.Kind != EntryKind.Plan).ToList();
        Assert.Equal(2, steps.Count);
        Assert.All(steps, step =>
        {
            Assert.Contains($"+{result.RoadmapTag}", step.Tags);
            Assert.DoesNotContain("+tag", step.Tags);
            Assert.Equal(EntryStatus.Draft, step.Status);
        });
        Assert.Equal(2, result.TaskIds.Count);

        Assert.Contains($"roadmap: [{result.RoadmapTag}]", Read("projects/move.md"));
    }

    [Fact]
    public async Task A_plan_naming_a_repository_the_note_does_not_is_refused_before_import()
    {
        Note("projects/move.md", Project);
        var tasks = new RecordingTaskItems();
        var drafter = new FixedDrafter("# Book movers\n`task` `!draft` `repo:someone/else`\n");

        var result = await Pickup(tasks, drafter).PlanProjectAsync("projects/move.md", Ct);

        Assert.Equal(BrainPickupOutcome.Failed, result.Outcome);
        Assert.Contains("someone/else", result.Message);
        Assert.Empty(tasks.Imports);
    }

    [Fact]
    public async Task A_refused_roadmap_link_keeps_the_plan_and_says_so()
    {
        Note("projects/move.md", "# Move house\n\nNo meta block at all.\n");
        var tasks = new RecordingTaskItems();
        var drafter = new FixedDrafter("# Pack\n`task` `!draft`\n");

        var result = await Pickup(tasks, drafter).PlanProjectAsync("projects/move.md", Ct);

        Assert.Equal(BrainPickupOutcome.NotLinked, result.Outcome);
        Assert.Single(tasks.Imports);
        Assert.NotNull(result.RoadmapTag);
        Assert.Contains("The link was not written into the note", result.Message);
    }

    [Fact]
    public async Task A_note_naming_a_repository_the_workspace_does_not_have_makes_nothing()
    {
        Note("projects/typo.md", "# Typo\n\n```meta\nrepos: [JSdotNet/Backlgo]\n```\n");
        var tasks = new RecordingTaskItems();
        var drafter = new FixedDrafter("# Pack\n`task`\n");

        var task = await Pickup(tasks, drafter).CreateTaskAsync("projects/typo.md", cancellationToken: Ct);
        var plan = await Pickup(tasks, drafter).PlanProjectAsync("projects/typo.md", Ct);

        Assert.Equal(BrainPickupOutcome.Failed, task.Outcome);
        Assert.Contains("JSdotNet/Backlgo", task.Message);
        Assert.Equal(BrainPickupOutcome.Failed, plan.Outcome);
        Assert.Empty(tasks.Saves);
        Assert.Empty(tasks.Imports);
        Assert.Empty(drafter.Requests);
    }

    [Fact]
    public async Task Plan_this_project_is_refused_on_a_note_outside_projects()
    {
        Note("areas/home.md", Plain);
        var drafter = new FixedDrafter("# Pack\n`task`\n");

        var result = await Pickup(new RecordingTaskItems(), drafter).PlanProjectAsync("areas/home.md", Ct);

        Assert.Equal(BrainPickupOutcome.Failed, result.Outcome);
        Assert.Equal(BrainPickup.NotAProject, result.Message);
        Assert.Empty(drafter.Requests);
    }

    [Theory]
    [InlineData("../outside.md")]
    [InlineData("projects/../areas/home.md")]
    [InlineData("areas/./home.md")]
    public async Task A_path_that_is_not_one_plain_note_path_is_not_a_note(string path)
    {
        Note("areas/home.md", Plain);
        var tasks = new RecordingTaskItems();

        var result = await Pickup(tasks).CreateTaskAsync(path, cancellationToken: Ct);

        Assert.Equal(BrainPickupOutcome.Failed, result.Outcome);
        Assert.Equal(BrainNoteLinkWriter.NotANote, result.Message);
        Assert.Empty(tasks.Saves);
    }

    [Theory]
    [InlineData("- [ ] Buy a ladder", "Buy a ladder")]
    [InlineData("* [x] Done thing", "Done thing")]
    [InlineData("## Heading line", "Heading line")]
    [InlineData("3. Third   step", "Third step")]
    [InlineData("\n\n  > quoted\nnext", "quoted")]
    [InlineData("   ", null)]
    public void A_selected_line_is_read_as_a_title(string selection, string? expected) =>
        Assert.Equal(expected, BrainPickupText.TitleFromLine(selection));

    /// <summary>The two repositories this workspace has, matched as the registry
    /// matches an id: without regard to case.</summary>
    private sealed class KnownRepositories : IRepositoryDirectory
    {
        public IReadOnlyList<TasksRepositoryRef> Repositories { get; } =
            [new("backlog", "JSdotNet", "Backlog"), new("sync", "JSdotNet", "Sync")];

        public TasksRepositoryRef? Resolve(string name) =>
            Repositories.FirstOrDefault(repo => string.Equals(repo.Id, name, StringComparison.OrdinalIgnoreCase) || repo.Alias == name);

        public Result<TasksRepositoryRef> Register(string name) => throw new NotSupportedException("A pickup never registers a repository.");
    }

    private sealed class FixedDrafter(string plan, bool available = true) : IInboxPlanDrafter
    {
        public List<InboxPlanDraftRequestDto> Requests { get; } = [];

        public bool IsAvailable => available;

        public string? UnavailableReason => available ? null : "Set the endpoint first.";

        public Task<Result<InboxPlanDraftDto>> DraftAsync(InboxPlanDraftRequestDto request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Result.Success(new InboxPlanDraftDto(plan)));
        }
    }

    private sealed class RecordingTaskItems : ITaskItems
    {
        public List<(Guid Id, string RawText)> Saves { get; } = [];

        public List<string> Imports { get; } = [];

        public Dictionary<Guid, IReadOnlyList<string>> References { get; } = [];

        public Task<IReadOnlyList<TaskItemDto>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaskItemDto>>([]);

        public Task<Result<SavedTaskDto>> SaveFromTextAsync(Guid? id, string rawText, int order, string? sourceInboxId = null, CancellationToken cancellationToken = default)
        {
            var created = Guid.NewGuid();
            Saves.Add((created, rawText));
            return Task.FromResult(Result.Success(new SavedTaskDto(Entry(created, EntryTextParser.Parse(rawText).Title))));
        }

        public Task<Result<ImportPlanResultDto>> ImportPlanAsync(string rawText, string? defaultRepo = null, IReadOnlyDictionary<string, string>? repoMatches = null, string? sourceInboxId = null, IReadOnlyDictionary<string, string>? sourceInboxIds = null, bool layOutOnRoadmap = false, CancellationToken cancellationToken = default)
        {
            Imports.Add(rawText);
            var entries = EntryTextParser.SplitSegments(rawText)
                .Select(EntryTextParser.Parse)
                .Where(parsed => parsed.Kind != EntryKind.Plan)
                .Select(parsed => Entry(Guid.NewGuid(), parsed.Title))
                .ToList();
            return Task.FromResult(Result.Success(new ImportPlanResultDto(entries.Count, 0, 0, 0, 0, entries)));
        }

        public Task<Result<TaskItemDto>> SetDevbookReferencesAsync(Guid id, IReadOnlyList<string> references, CancellationToken cancellationToken = default)
        {
            References[id] = references;
            return Task.FromResult(Result.Success(Entry(id, "x")));
        }

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ReorderAsync(IReadOnlyList<Guid> idsInOrder, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Result<TaskItemDto>> LinkToIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<TaskItemDto>> UnlinkFromIssueAsync(Guid id, string repoId, string externalId, string targetType, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RecordUsageAsync(Guid id, string action, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Result<int>> ReconcileRepositoryIdsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result.Success(0));

        public Task<Result<int>> RenameRepositoryAsync(string oldId, string newId, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success(0));

        public Task<Result<int>> RefileRepositoryTasksAsync(string repositoryId, string? moveToId, bool drop, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success(0));

        private static TaskItemDto Entry(Guid id, string title) =>
            new(id, title, string.Empty, EntryType.Task, Priority.Medium, EntryStatus.Draft, null, [], 0, 0, 0, []);
    }
}
