using Backlog.Modules.Tasks.Abstractions;
using Microsoft.AspNetCore.Components.Web;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The menu a right-click on an entry row opens.
/// <para>
/// Every item is an act the detail pane already offers, so what is under test is
/// not the write — <see cref="EntryScheduleControlsTests"/> covers those — but
/// that the menu reaches the same write from the row, says the right thing about
/// the row it was opened on, and closes once it has been answered.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class TasksPaneContextMenuTests
{
    private const string Entry = "# Provision the box\n`task` `!in-progress`\n";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private static string RowTestId(EntryRow row) => $"entry-list-{(row.Id ?? row.Key)}";

    private static Task OpenMenuAsync(IRenderedComponent<TasksPane> pane, EntryRow row) =>
        pane.Find($"[data-testid='{RowTestId(row)}']")
            .ContextMenuAsync(new MouseEventArgs { ClientX = 40, ClientY = 60 });

    private static Task ChooseAsync(IRenderedComponent<TasksPane> pane, string item) =>
        pane.Find($"[data-testid='entry-menu-item-{item}']").ClickAsync(new());

    private static string Label(IRenderedComponent<TasksPane> pane, string item) =>
        pane.Find($"[data-testid='entry-menu-item-{item}'] .menu-list__label").TextContent;

    private static bool IsDisabled(IRenderedComponent<TasksPane> pane, string item) =>
        pane.Find($"[data-testid='entry-menu-item-{item}']").HasAttribute("disabled");

    [Fact]
    public async Task A_right_click_opens_the_menu_where_the_pointer_was()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);

        var pane = host.Render();
        Assert.Empty(pane.FindAll("[data-testid='entry-menu']"));

        await OpenMenuAsync(pane, row);

        var menu = pane.Find("[data-testid='entry-menu']");
        Assert.Contains("--context-menu-x: 40px", menu.GetAttribute("style"), StringComparison.Ordinal);
        Assert.Contains("--context-menu-y: 60px", menu.GetAttribute("style"), StringComparison.Ordinal);

        // The order Microsoft To Do settled on: the marks, the dates, the place, the end.
        Assert.Equal(
            ["myday", "important", "done", "due-today", "due-tomorrow", "due-pick", "due-clear", "move-up", "move-down", "delete"],
            pane.FindAll("[data-testid='entry-menu'] [role='menuitem']")
                .Select(item => item.GetAttribute("data-testid")!["entry-menu-item-".Length..]));
    }

    [Fact]
    public async Task Due_today_and_due_tomorrow_write_the_date_and_close_the_menu()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        var pane = host.Render();

        await OpenMenuAsync(pane, row);
        await ChooseAsync(pane, "due-today");

        Assert.Equal(Today, row.PreviewDueOn);
        Assert.Empty(pane.FindAll("[data-testid='entry-menu']"));

        await OpenMenuAsync(pane, row);
        await ChooseAsync(pane, "due-tomorrow");

        Assert.Equal(Today.AddDays(1), row.PreviewDueOn);
    }

    [Fact]
    public async Task Remove_due_date_is_offered_disabled_until_there_is_one_to_remove()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        var pane = host.Render();

        await OpenMenuAsync(pane, row);
        Assert.True(IsDisabled(pane, "due-clear"));

        await ChooseAsync(pane, "due-today");
        await OpenMenuAsync(pane, row);
        Assert.False(IsDisabled(pane, "due-clear"));

        await ChooseAsync(pane, "due-clear");

        Assert.Null(row.PreviewDueOn);
    }

    [Fact]
    public async Task Pick_a_date_opens_the_entry_with_its_due_picker_showing()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        await host.WriteEntryAsync("# Deploy it\n`task`\n");
        var pane = host.Render();

        Assert.NotSame(row, host.State.SelectedRow);

        await OpenMenuAsync(pane, row);
        await ChooseAsync(pane, "due-pick");

        // The date field lives in the detail pane, so that is where the reader lands.
        Assert.Same(row, host.State.SelectedRow);
        Assert.NotEmpty(pane.FindAll("[data-testid='entry-due-input']"));
    }

    [Fact]
    public async Task My_Day_is_a_toggle_whose_label_names_the_way_out()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        var pane = host.Render();

        await OpenMenuAsync(pane, row);
        Assert.Equal("Add to My Day", Label(pane, "myday"));
        await ChooseAsync(pane, "myday");

        Assert.Equal(Today, row.PreviewInMyDayOn);

        await OpenMenuAsync(pane, row);
        Assert.Equal("Remove from My Day", Label(pane, "myday"));
        await ChooseAsync(pane, "myday");

        Assert.Null(row.PreviewInMyDayOn);
    }

    [Fact]
    public async Task Important_lifts_the_priority_to_high_and_back_to_medium()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        var pane = host.Render();

        await OpenMenuAsync(pane, row);
        Assert.Equal("Mark as important", Label(pane, "important"));
        await ChooseAsync(pane, "important");

        Assert.Equal(Priority.High, row.PreviewPriority);

        await OpenMenuAsync(pane, row);
        Assert.Equal("Remove importance", Label(pane, "important"));
        await ChooseAsync(pane, "important");

        Assert.Equal(Priority.Medium, row.PreviewPriority);
    }

    [Fact]
    public async Task Mark_as_completed_finishes_the_row()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        var pane = host.Render();

        await OpenMenuAsync(pane, row);
        Assert.Equal("Mark as completed", Label(pane, "done"));
        await ChooseAsync(pane, "done");

        Assert.Equal(EntryStatus.Done, row.PreviewStatus);
    }

    [Fact]
    public async Task Move_up_and_move_down_swap_with_the_neighbour_on_screen()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var first = await host.WriteEntryAsync("# First\n`task`\n");
        var second = await host.WriteEntryAsync("# Second\n`task`\n");
        var third = await host.WriteEntryAsync("# Third\n`task`\n");
        var pane = host.Render();

        Assert.Equal([first, second, third], host.State.Rows);

        await OpenMenuAsync(pane, first);
        Assert.True(IsDisabled(pane, "move-up"));
        Assert.False(IsDisabled(pane, "move-down"));
        await ChooseAsync(pane, "move-down");

        Assert.Equal([second, first, third], host.State.Rows);

        await OpenMenuAsync(pane, third);
        Assert.False(IsDisabled(pane, "move-up"));
        Assert.True(IsDisabled(pane, "move-down"));
        await ChooseAsync(pane, "move-up");

        Assert.Equal([second, third, first], host.State.Rows);
    }

    [Fact]
    public async Task Delete_task_removes_the_row()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        var pane = host.Render();

        await OpenMenuAsync(pane, row);
        await ChooseAsync(pane, "delete");

        Assert.DoesNotContain(row, host.State.Rows);
        Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(row)}']"));
    }

    [Fact]
    public async Task Escape_closes_the_menu_without_choosing()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        var pane = host.Render();

        await OpenMenuAsync(pane, row);
        await pane.Find(".context-menu__backdrop").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(pane.FindAll("[data-testid='entry-menu']"));
        Assert.Null(row.PreviewDueOn);
    }

    /// <summary>The menu's backdrop takes the focus when it opens, and it sits
    /// outside both halves of the split — which is exactly the move the pane reads
    /// as the reader having left the open entry. Asking a row for its menu is not
    /// leaving.</summary>
    [Fact]
    public async Task The_open_entry_survives_its_row_being_right_clicked()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        await host.OpenAsync(row);

        // The focus did land outside the detail pane — on the menu's backdrop.
        host.Context.JSInterop.Setup<bool>("backlogFocusOutside", _ => true).SetResult(true);

        var pane = host.Render();
        await OpenMenuAsync(pane, row);
        await pane.Find("[data-testid='backlog-pane']").TriggerEventAsync("onfocusout", new FocusEventArgs());

        Assert.Same(row, host.State.SelectedRow);
    }
}
