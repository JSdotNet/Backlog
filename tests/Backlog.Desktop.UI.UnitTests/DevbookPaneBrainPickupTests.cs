using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Picking work up from a Brain note in the pane
/// (<c>.devbook/domain/brain/features.md#brain-mode-of-the-devbook-pane</c>): a note
/// offers Create task — from the line selected in it, or the note — and a project
/// note Plan this project; each says what it did, a link the note refused as a
/// warning, and each stays on screen disabled with its reason when it cannot run.
/// </summary>
public sealed class DevbookPaneBrainPickupTests
{
    /// <summary>The note's own area first, so the pane opens on it.</summary>
    private static string Conventions(string notePath) => notePath.StartsWith("projects/", StringComparison.Ordinal)
        ? "## Areas\n\n- Projects — `projects`\n- Areas — `areas`\n"
        : "## Areas\n\n- Areas — `areas`\n- Projects — `projects`\n";

    private static Task<(DevbookPaneBrainHarness Harness, IRenderedComponent<DevbookPane> Pane)> OpenAsync(string notePath, FakePickup? pickup, IBrainTaskLinks? taskLinks = null)
    {
        var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, Conventions(notePath));
        harness.WriteNote(notePath, "# Move house\n\n```meta\nroadmap: []\n```\n\n- [ ] Buy boxes\n");
        if (pickup is not null) harness.Context.Services.AddSingleton<IBrainPickup>(pickup);
        if (taskLinks is not null) harness.Context.Services.AddSingleton(taskLinks);

        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='brain-note']")));
        return Task.FromResult((harness, component));
    }

    [Fact]
    public async Task Create_task_takes_the_selected_line_and_says_the_note_lists_it()
    {
        var pickup = new FakePickup();
        var (harness, component) = await OpenAsync("projects/move.md", pickup);
        await using var _ = harness;
        harness.Context.JSInterop.Setup<string?>("backlogSelectedText", _ => true).SetResult("- [ ] Buy boxes");

        component.Find("[data-testid='brain-note-create-task']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal([("projects/move.md", "- [ ] Buy boxes")], pickup.Tasks);
            var status = component.Find("[data-testid='brain-note-pickup-status']");
            Assert.Equal("Task created, and the note lists it.", status.TextContent.Trim());
            Assert.DoesNotContain("brain-note__pickup-status--warning", status.ClassList);
        });
    }

    [Fact]
    public async Task Create_task_with_nothing_selected_asks_for_the_note_itself()
    {
        var pickup = new FakePickup();
        var (harness, component) = await OpenAsync("projects/move.md", pickup);
        await using var _ = harness;

        component.Find("[data-testid='brain-note-create-task']").Click();

        component.WaitForAssertion(() => Assert.Equal([("projects/move.md", (string?)null)], pickup.Tasks));
    }

    [Fact]
    public async Task A_link_the_note_refused_is_said_as_a_warning_and_the_work_stays()
    {
        var pickup = new FakePickup
        {
            Answer = new BrainPickupResult(BrainPickupOutcome.NotLinked, [Guid.NewGuid()], null, "Task created. The link was not written into the note: The note's top meta block is never closed."),
        };
        var (harness, component) = await OpenAsync("projects/move.md", pickup);
        await using var _ = harness;

        component.Find("[data-testid='brain-note-create-task']").Click();

        component.WaitForAssertion(() =>
        {
            var status = component.Find("[data-testid='brain-note-pickup-status']");
            Assert.Contains("The link was not written into the note", status.TextContent);
            Assert.Contains("brain-note__pickup-status--warning", status.ClassList);
        });
    }

    [Fact]
    public async Task Plan_this_project_is_offered_on_a_project_note_and_asks_for_it()
    {
        var pickup = new FakePickup
        {
            Answer = new BrainPickupResult(BrainPickupOutcome.Linked, [Guid.NewGuid(), Guid.NewGuid()], "move-house-1a2b3c4d", "Planned as +move-house-1a2b3c4d: 2 tasks, and the note names the plan."),
        };
        var (harness, component) = await OpenAsync("projects/move.md", pickup);
        await using var _ = harness;

        component.Find("[data-testid='brain-note-plan-project']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal(["projects/move.md"], pickup.Plans);
            Assert.Contains("+move-house-1a2b3c4d", component.Find("[data-testid='brain-note-pickup-status']").TextContent);
        });
    }

    /// <summary>
    /// The open note is read again once the pickup linked it, so its Tasks section
    /// lists the task the message says it lists — though the Brain's database, which
    /// the section reads, has not yet been rebuilt from the file that now names it.
    /// </summary>
    [Fact]
    public async Task Create_task_lists_the_new_task_on_the_open_note_at_once()
    {
        var id = Guid.NewGuid();
        var pickup = new FakePickup { Answer = new BrainPickupResult(BrainPickupOutcome.Linked, [id], null, "Task created, and the note lists it.") };
        var (harness, component) = await OpenAsync("projects/move.md", pickup, new LaggingTaskLinks());
        await using var _ = harness;
        pickup.OnPick = () => harness.WriteNote("projects/move.md", $"# Move house\n\n```meta\nroadmap: []\ntasks: [{id:D}]\n```\n\n- [ ] Buy boxes\n");
        Assert.Empty(component.FindAll("[data-testid='brain-note-task']"));

        component.Find("[data-testid='brain-note-create-task']").Click();

        component.WaitForAssertion(() =>
        {
            var chip = Assert.Single(component.FindAll("[data-testid='brain-note-task']"));
            Assert.Equal(id.ToString("D"), chip.QuerySelector(".repo-link-chip__label")!.TextContent.Trim());
        });
        Assert.Equal("Task created, and the note lists it.", component.Find("[data-testid='brain-note-pickup-status']").TextContent.Trim());
    }

    /// <summary>The note is read again after a plan as well. The read view draws no
    /// <c>roadmap</c> field, so the file the pickup writes carries a body line too, to
    /// show which text is on screen.</summary>
    [Fact]
    public async Task Plan_this_project_reads_the_note_it_named_the_plan_in_again()
    {
        var pickup = new FakePickup
        {
            Answer = new BrainPickupResult(BrainPickupOutcome.Linked, [Guid.NewGuid()], "move-house-1a2b3c4d", "Planned as +move-house-1a2b3c4d: 1 task, and the note names the plan."),
        };
        var (harness, component) = await OpenAsync("projects/move.md", pickup);
        await using var _ = harness;
        pickup.OnPick = () => harness.WriteNote("projects/move.md", "# Move house\n\n```meta\nroadmap: [move-house-1a2b3c4d]\n```\n\n- [ ] Buy boxes\n- [ ] Book the van\n");

        component.Find("[data-testid='brain-note-plan-project']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Contains("Book the van", component.Find("[data-testid='brain-note-file']").TextContent);
            Assert.Contains("+move-house-1a2b3c4d", component.Find("[data-testid='brain-note-pickup-status']").TextContent);
        });
    }

    [Fact]
    public async Task A_link_the_note_refused_lists_no_task_the_note_does_not_name()
    {
        var pickup = new FakePickup
        {
            Answer = new BrainPickupResult(BrainPickupOutcome.NotLinked, [Guid.NewGuid()], null, "Task created. The link was not written into the note: The note's top meta block is never closed."),
        };
        var (harness, component) = await OpenAsync("projects/move.md", pickup, new LaggingTaskLinks());
        await using var _ = harness;

        // Written all the same, so only a re-read could draw it: a refused link is no
        // reason to read the note again.
        pickup.OnPick = () => harness.WriteNote("projects/move.md", $"# Move house\n\n```meta\nroadmap: []\ntasks: [{Guid.NewGuid():D}]\n```\n\n- [ ] Buy boxes\n");

        component.Find("[data-testid='brain-note-create-task']").Click();

        component.WaitForAssertion(() => Assert.Contains("The link was not written into the note", component.Find("[data-testid='brain-note-pickup-status']").TextContent));
        Assert.Empty(component.FindAll("[data-testid='brain-note-task']"));
    }

    [Fact]
    public async Task A_note_outside_projects_offers_no_plan()
    {
        var (harness, component) = await OpenAsync("areas/home.md", new FakePickup());
        await using var _ = harness;

        Assert.NotNull(component.Find("[data-testid='brain-note-create-task']"));
        Assert.Empty(component.FindAll("[data-testid='brain-note-plan-project']"));
    }

    [Fact]
    public async Task Each_action_that_cannot_run_stays_disabled_with_its_reason()
    {
        var pickup = new FakePickup { TaskReason = "Set up your Brain in Settings › Storage first.", PlanReason = "No plan drafter is configured." };
        var (harness, component) = await OpenAsync("projects/move.md", pickup);
        await using var _ = harness;

        var create = component.Find("[data-testid='brain-note-create-task']");
        Assert.True(create.HasAttribute("disabled"));
        Assert.Equal("Set up your Brain in Settings › Storage first.", create.GetAttribute("title"));

        var plan = component.Find("[data-testid='brain-note-plan-project']");
        Assert.True(plan.HasAttribute("disabled"));
        Assert.Equal("No plan drafter is configured.", plan.GetAttribute("title"));
    }

    [Fact]
    public async Task A_host_that_composes_no_pickup_offers_neither_action()
    {
        var (harness, component) = await OpenAsync("projects/move.md", pickup: null);
        await using var _ = harness;

        Assert.Empty(component.FindAll("[data-testid='brain-note-pickup']"));
    }

    private sealed class FakePickup : IBrainPickup
    {
        public List<(string Path, string? Line)> Tasks { get; } = [];

        public List<string> Plans { get; } = [];

        public string? TaskReason { get; init; }

        public string? PlanReason { get; init; }

        public BrainPickupResult Answer { get; init; } = new(BrainPickupOutcome.Linked, [Guid.NewGuid()], null, "Task created, and the note lists it.");

        /// <summary>What the pickup writes into the note, as the real link writer
        /// does before it answers.</summary>
        public Action? OnPick { get; set; }

        public string? CreateTaskUnavailableReason => TaskReason;

        public string? PlanUnavailableReason => PlanReason;

        public Task<BrainPickupResult> CreateTaskAsync(string notePath, string? line = null, CancellationToken cancellationToken = default)
        {
            Tasks.Add((notePath, line));
            OnPick?.Invoke();
            return Task.FromResult(Answer);
        }

        public Task<BrainPickupResult> PlanProjectAsync(string notePath, CancellationToken cancellationToken = default)
        {
            Plans.Add(notePath);
            OnPick?.Invoke();
            return Task.FromResult(Answer);
        }
    }

    /// <summary>The Brain's database before its rebuild has caught up with the note:
    /// it lists no task for any note.</summary>
    private sealed class LaggingTaskLinks : IBrainTaskLinks
    {
        public Task<IReadOnlyList<BrainTaskLink>> TasksOfAsync(string notePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BrainTaskLink>>([]);

        public Task<IReadOnlyDictionary<string, IReadOnlyList<BrainTaskNote>>> NotesByTaskAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<BrainTaskNote>>>(new Dictionary<string, IReadOnlyList<BrainTaskNote>>());
    }
}
