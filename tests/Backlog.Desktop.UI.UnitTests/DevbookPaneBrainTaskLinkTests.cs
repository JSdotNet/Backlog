using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A Brain note's Tasks section (local ADR 0026 §3,
/// <c>.devbook/domain/devbook/requirements.md#bi-directional-linking</c>): the tasks
/// its <c>tasks</c> field names, read from the Brain's database, each a chip that
/// opens the task — or, for a task this backlog has not got, a dashed chip reading
/// "not in this backlog", never an error.
/// </summary>
public sealed class DevbookPaneBrainTaskLinkTests
{
    private const string Known = "6f9619ff-8b86-d011-b42d-00c04fc964ff";
    private const string Unknown = "0c8a1b4e-2f6d-4a37-9c11-5e0b7d3f2a90";

    [Fact]
    public async Task A_note_lists_its_tasks_known_ones_followable_and_unknown_ones_dashed()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, "## Areas\n\n- Work — `work`\n");
        harness.WriteNote("work/plan.md", "# Plan\n\nThe plan.\n");
        harness.Context.Services.AddSingleton<IBrainTaskLinks>(new FixedTaskLinks("work/plan.md",
            [BrainTaskLink.Parse(Known.ToUpperInvariant())!, BrainTaskLink.Parse(Unknown)!]));

        var opened = new List<string>();
        var component = harness.Context.Render<DevbookPane>(parameters => parameters
            .Add(pane => pane.RepositoryAlias, string.Empty)
            .Add(pane => pane.TaskTitleFor, id => id == Known ? "Ship the badge" : null)
            .Add(pane => pane.OnOpenTask, (string id) => opened.Add(id)));
        component.WaitForAssertion(() => Assert.Equal(2, component.FindAll("[data-testid='brain-note-task']").Count));

        Assert.Equal("Tasks", component.Find("[data-testid='brain-note-tasks'] h4").TextContent.Trim());
        var chips = component.FindAll("[data-testid='brain-note-task']");

        var known = chips[0];
        Assert.Contains("repo-link-chip--resolved", known.ClassList);
        Assert.Equal("Ship the badge", known.QuerySelector(".repo-link-chip__label")!.TextContent.Trim());

        var unknown = chips[1];
        Assert.Contains("repo-link-chip--unresolved", unknown.ClassList);
        Assert.Equal(Backlog.UI.Components.Devbook.TaskLinkChip.NotInThisBacklog, unknown.QuerySelector(".repo-link-chip__problem")!.TextContent.Trim());
        Assert.Equal(Unknown, unknown.QuerySelector(".repo-link-chip__label")!.TextContent.Trim());
        Assert.Null(unknown.QuerySelector("button"));
        Assert.Empty(component.FindAll(".alert--error"));

        known.QuerySelector("button")!.Click();
        component.WaitForAssertion(() => Assert.Equal([Known], opened));
    }

    [Fact]
    public async Task A_note_naming_no_task_draws_no_tasks_section()
    {
        await using var harness = DevbookPaneBrainHarness.Create();
        harness.WriteNote(BrainConventions.FileName, "## Areas\n\n- Work — `work`\n");
        harness.WriteNote("work/plan.md", "# Plan\n\nThe plan.\n");
        harness.Context.Services.AddSingleton<IBrainTaskLinks>(new FixedTaskLinks("elsewhere.md", [BrainTaskLink.Parse(Known)!]));

        var component = harness.RenderBrain();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='brain-note']")));

        Assert.Empty(component.FindAll("[data-testid='brain-note-tasks']"));
    }

    private sealed class FixedTaskLinks(string notePath, IReadOnlyList<BrainTaskLink> links) : IBrainTaskLinks
    {
        public Task<IReadOnlyList<BrainTaskLink>> TasksOfAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Equals(path, notePath, StringComparison.OrdinalIgnoreCase) ? links : (IReadOnlyList<BrainTaskLink>)[]);

        public Task<IReadOnlyDictionary<string, IReadOnlyList<BrainTaskNote>>> NotesByTaskAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<BrainTaskNote>>>(new Dictionary<string, IReadOnlyList<BrainTaskNote>>());
    }
}
