using Backlog.Modules.Tasks.Abstractions;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The copy in the Tasks pane is permanent: the row, the open entry and the pressed
/// plan chip always offer it, beside the queue icon.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class CopyEntriesFeatureTests
{
    private const string Entry = "# Start it +alpha\n`task` `!ready` `@platform`\n";

    [Fact]
    public async Task Copy_is_offered_on_the_row_the_open_entry_and_the_pressed_plan()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        host.State.ShowPlan("+alpha");

        var pane = host.Render();

        Assert.NotNull(pane.Find($"[data-testid='entry-list-{row.TaskId}-copy']"));
        Assert.NotNull(pane.Find("[data-testid='entry-panel-copy']"));
        Assert.NotNull(pane.Find("[data-testid='tag-filter-copy-plan']"));
    }
}
