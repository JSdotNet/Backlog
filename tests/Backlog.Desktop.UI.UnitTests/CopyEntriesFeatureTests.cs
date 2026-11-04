using Backlog.Modules.Tasks.Abstractions;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The <c>copy-entries</c> switch: on by default, every copy in the Tasks pane is
/// there; off, the row, the open entry and the pressed plan chip offer no copy, and
/// the queue icon that is meant to replace it stays where it was.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class CopyEntriesFeatureTests
{
    private const string Entry = "# Start it +alpha\n`task` `!ready` `@platform`\n";

    [Fact]
    public async Task Copy_is_offered_by_default_on_the_row_the_open_entry_and_the_pressed_plan()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        host.State.ShowPlan("+alpha");

        var pane = host.Render();

        Assert.True(host.Features.IsEnabled(AppFeatureKeys.CopyEntries));
        Assert.NotNull(pane.Find($"[data-testid='entry-list-{row.TaskId}-copy']"));
        Assert.NotNull(pane.Find("[data-testid='entry-panel-copy']"));
        Assert.NotNull(pane.Find("[data-testid='tag-filter-copy-plan']"));
    }

    [Fact]
    public async Task Switched_off_no_copy_is_offered_on_the_row_or_the_open_entry()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        host.Features.SetEnabled(AppFeatureKeys.CopyEntries, enabled: false);

        var pane = host.Render();

        Assert.NotNull(pane.Find("[data-testid='entry-panel']"));
        Assert.Empty(pane.FindAll($"[data-testid='entry-list-{row.TaskId}-copy']"));
        Assert.Empty(pane.FindAll("[data-testid='entry-panel-copy']"));
    }

    [Fact]
    public async Task Switched_off_a_board_card_offers_no_copy()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync(Entry);

        var pane = host.Context.Render<TasksPane>(parameters => parameters.Add(p => p.Layout, TasksLayout.Board));
        Assert.NotEmpty(pane.FindAll(".task-card__copy"));

        await pane.InvokeAsync(() => host.Features.SetEnabled(AppFeatureKeys.CopyEntries, enabled: false));

        pane.WaitForAssertion(() =>
        {
            Assert.NotEmpty(pane.FindAll("[data-testid='board-card']"));
            Assert.Empty(pane.FindAll(".task-card__copy"));
        });
    }

    [Fact]
    public async Task Switching_off_while_the_pane_is_open_takes_the_copy_away()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        host.State.ShowPlan("+alpha");
        var pane = host.Render();
        Assert.NotNull(pane.Find($"[data-testid='entry-list-{row.TaskId}-copy']"));

        await pane.InvokeAsync(() => host.Features.SetEnabled(AppFeatureKeys.CopyEntries, enabled: false));

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll($"[data-testid='entry-list-{row.TaskId}-copy']"));
            Assert.Empty(pane.FindAll("[data-testid='entry-panel-copy']"));
            Assert.Empty(pane.FindAll("[data-testid='tag-filter-copy-plan']"));
        });
    }
}
