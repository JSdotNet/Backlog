using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Done on the backlog's status strip. It was dropped once to give the tag pile
/// room, and asked for back: "what did I finish" is a question the strip answers
/// in one press, where "All" only answers it by scrolling past everything open.
/// Archived stays off — nobody asked for it, and the width argument still holds.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class DoneStatusFilterTests
{
    [Fact]
    public async Task The_strip_offers_done_after_in_progress()
    {
        using var host = await TasksPaneHost.CreateAsync();

        Assert.Equal(
            ["All", "Draft", "Ready", "In progress", "Done"],
            host.State.StatusFilters.Select(option => option.Label));
        Assert.Equal("done", host.State.StatusFilters[^1].Wire);
    }

    [Fact]
    public async Task Pressing_done_narrows_the_list_to_done_entries()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync("# Provision the box\n`task` `!ready` `@platform`\n");
        var finished = await host.WriteEntryAsync("# Run the QA pass\n`task` `!done` `completed:2026-09-22` `@platform`\n");
        await host.State.SelectAsync(null);

        var pane = host.Render();
        pane.Find("#status-filter-done").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal("done", host.State.SelectedStatusFilterWire);
            Assert.Equal([finished], host.State.FilteredRows);
            Assert.Equal("true", pane.Find("#status-filter-done").GetAttribute("aria-checked"));
        });
    }
}
