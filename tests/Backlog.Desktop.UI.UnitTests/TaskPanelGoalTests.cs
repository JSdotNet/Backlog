using Backlog.UI.Components.Goals;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Tasks pane's side panel and the goal control the host cascades
/// (<c>.devbook/domain/progress/features.md#attaching-work-to-a-goal</c>): a stored entry's
/// panel draws the slot for that task, by its id, in the panel's Goal line; with no slot
/// cascaded the panel draws no goal line at all. The pane never sees Progress — it hands
/// the slot a subject and draws what comes back.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class TaskPanelGoalTests
{
    private const string Entry = "# Record the adoption decision\n`prompt` `!in-progress`\n\nWrite the ADR.\n";

    [Fact]
    public async Task A_stored_entrys_panel_draws_the_goal_for_that_task()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        var drawn = new List<GoalSubject>();
        var slot = new GoalControlSlot(subject => builder =>
        {
            drawn.Add(subject);
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "data-testid", "goal-slot");
            builder.CloseElement();
        });

        var pane = host.Context.Render<TasksPane>(parameters => parameters.AddCascadingValue(slot));

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid=entry-panel-goal]").QuerySelector("[data-testid=goal-slot]")));
        Assert.Equal([GoalSubject.ForTask(row.Id!.Value)], drawn.Distinct());
    }

    /// <summary>The host composes goals with the switch off: the slot is cascaded and
    /// not offered, and the panel draws no goal line — not an empty one.</summary>
    [Fact]
    public async Task A_slot_that_is_not_offered_draws_no_goal_line()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync(Entry);
        var drawn = 0;
        var slot = new GoalControlSlot(_ => builder => { drawn++; builder.AddContent(0, "goal"); }, isOffered: () => false);

        var pane = host.Context.Render<TasksPane>(parameters => parameters.AddCascadingValue(slot));

        pane.WaitForAssertion(() => pane.Find("[data-testid=entry-panel]"));
        Assert.Empty(pane.FindAll("[data-testid=entry-panel-goal]"));
        Assert.Empty(pane.FindAll(".task-panel__goal"));
        Assert.Equal(0, drawn);
    }

    [Fact]
    public async Task Without_a_slot_the_panel_draws_no_goal_line()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync(Entry);

        var pane = host.Render();

        pane.WaitForAssertion(() => pane.Find("[data-testid=entry-panel]"));
        Assert.Empty(pane.FindAll("[data-testid=entry-panel-goal]"));
    }
}
