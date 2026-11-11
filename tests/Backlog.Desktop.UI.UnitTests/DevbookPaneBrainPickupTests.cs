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

    private static Task<(DevbookPaneBrainHarness Harness, IRenderedComponent<DevbookPane> Pane)> OpenAsync(string notePath, FakePickup? pickup)
    {
        var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, Conventions(notePath));
        harness.WriteNote(notePath, "# Move house\n\n```meta\nroadmap: []\n```\n\n- [ ] Buy boxes\n");
        if (pickup is not null) harness.Context.Services.AddSingleton<IBrainPickup>(pickup);

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

        component.WaitForAssertion(() => Assert.Equal([("projects/move.md", "- [ ] Buy boxes")], pickup.Tasks));
        var status = component.Find("[data-testid='brain-note-pickup-status']");
        Assert.Equal("Task created, and the note lists it.", status.TextContent.Trim());
        Assert.DoesNotContain("brain-note__pickup-status--warning", status.ClassList);
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

        component.WaitForAssertion(() => Assert.Equal(["projects/move.md"], pickup.Plans));
        Assert.Contains("+move-house-1a2b3c4d", component.Find("[data-testid='brain-note-pickup-status']").TextContent);
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

        public string? CreateTaskUnavailableReason => TaskReason;

        public string? PlanUnavailableReason => PlanReason;

        public Task<BrainPickupResult> CreateTaskAsync(string notePath, string? line = null, CancellationToken cancellationToken = default)
        {
            Tasks.Add((notePath, line));
            return Task.FromResult(Answer);
        }

        public Task<BrainPickupResult> PlanProjectAsync(string notePath, CancellationToken cancellationToken = default)
        {
            Plans.Add(notePath);
            return Task.FromResult(Answer);
        }
    }
}
