using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Ctrl+Z on the Tasks pane (<c>.devbook/design/interaction-guidelines.md#undo-and-history</c>):
/// there is no save gate, so every change is already in the store by the time the
/// reader wants it back. Each test therefore asserts on what the store holds after
/// the undo, not only on the row — an undo that only repainted the list would be
/// put straight back by the next reload.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class TasksUndoTests
{
    private const string Provision =
        "# Provision the box\n" +
        "`task` `!ready`\n\n" +
        "Rack it first.\n";

    private const string Runbook =
        "# Write the runbook\n" +
        "`task` `!draft`\n\n" +
        "Then say how.\n";

    [Fact]
    public async Task Undo_takes_back_a_status_change_in_the_store()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Provision);

        await host.State.ChangeStatusAsync(row, EntryStatus.InProgress);
        Assert.Equal(EntryStatus.InProgress, row.PreviewStatus);

        Assert.True(await host.State.UndoAsync());

        Assert.Equal(EntryStatus.Ready, row.PreviewStatus);
        Assert.Equal(EntryStatus.Ready, (await StoredAsync(host, row.Id!.Value)).Status);
    }

    [Fact]
    public async Task Redo_puts_the_change_back()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Provision);
        await host.State.ChangeStatusAsync(row, EntryStatus.InProgress);
        await host.State.UndoAsync();

        Assert.True(await host.State.RedoAsync());

        Assert.Equal(EntryStatus.InProgress, (await StoredAsync(host, row.Id!.Value)).Status);
    }

    [Fact]
    public async Task Undo_goes_back_one_change_at_a_time()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Provision);
        await host.State.ChangeStatusAsync(row, EntryStatus.InProgress);
        await host.State.ChangePriorityAsync(row, Priority.High);

        await host.State.UndoAsync();

        var stored = await StoredAsync(host, row.Id!.Value);
        Assert.Equal(EntryStatus.InProgress, stored.Status);
        Assert.NotEqual(Priority.High, stored.Priority);

        await host.State.UndoAsync();

        Assert.Equal(EntryStatus.Ready, (await StoredAsync(host, row.Id!.Value)).Status);
    }

    [Fact]
    public async Task A_new_change_after_an_undo_leaves_nothing_to_redo()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Provision);
        await host.State.ChangeStatusAsync(row, EntryStatus.InProgress);
        await host.State.UndoAsync();

        await host.State.ChangePriorityAsync(row, Priority.High);

        Assert.False(host.State.CanRedo);
        Assert.False(await host.State.RedoAsync());
    }

    [Fact]
    public async Task One_typing_session_is_one_undo_step()
    {
        var clock = new FakeTimeProvider();
        using var host = await TasksPaneHost.CreateAsync(roadmapTags: null, [], clock: clock);
        var row = await host.WriteEntryAsync(Provision);

        host.State.BeginEdit(row);
        await TypeAndLetItSaveAsync(host, clock, row, Provision.Replace("Rack it first.", "Rack it first. Then", StringComparison.Ordinal));
        await TypeAndLetItSaveAsync(host, clock, row, Provision.Replace("Rack it first.", "Rack it first. Then cable it.", StringComparison.Ordinal));
        await host.State.EndEditAsync(row);

        Assert.Contains("cable it", EntryTextParser.ToRawText(await StoredAsync(host, row.Id!.Value)), StringComparison.Ordinal);

        await host.State.UndoAsync();

        var stored = await StoredAsync(host, row.Id!.Value);
        Assert.DoesNotContain("Then", EntryTextParser.ToRawText(stored), StringComparison.Ordinal);
        Assert.Contains("Rack it first.", EntryTextParser.ToRawText(stored), StringComparison.Ordinal);

        // The entry itself is still there: the step before the typing is its
        // creation, and one Ctrl+Z did not reach it.
        Assert.Contains(host.State.Rows, r => r.Id == row.Id);
    }

    [Fact]
    public async Task Undoing_a_reorder_restores_the_previous_order_and_redo_moves_it_again()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var first = await host.WriteEntryAsync(Provision);
        var second = await host.WriteEntryAsync(Runbook);
        var before = OrderOf(host);

        await host.State.MoveEntryAsync(second, first);
        var moved = OrderOf(host);
        Assert.NotEqual(before, moved);

        await host.State.UndoAsync();

        Assert.Equal(before, OrderOf(host));
        Assert.Equal(before, await StoredOrderAsync(host));

        await host.State.RedoAsync();

        Assert.Equal(moved, await StoredOrderAsync(host));
    }

    [Fact]
    public async Task Undoing_a_delete_brings_the_entry_back_where_it_was()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var first = await host.WriteEntryAsync(Provision);
        await host.WriteEntryAsync(Runbook);
        var index = host.State.Rows.IndexOf(first);

        await host.State.DeleteRowAsync(first);
        Assert.DoesNotContain(host.State.Rows, r => r.PreviewTitle == "Provision the box");

        await host.State.UndoAsync();

        var restored = host.State.Rows[index];
        Assert.Equal("Provision the box", restored.PreviewTitle);
        Assert.Equal(EntryStatus.Ready, restored.PreviewStatus);
        Assert.Contains("Rack it first.", EntryTextParser.ToRawText(await StoredAsync(host, restored.Id!.Value)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Redoing_a_delete_after_its_undo_deletes_the_entry_that_came_back()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var first = await host.WriteEntryAsync(Provision);
        await host.WriteEntryAsync(Runbook);
        await host.State.DeleteRowAsync(first);
        await host.State.UndoAsync();

        await host.State.RedoAsync();

        var stored = await host.EntriesElsewhere().ListAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(stored, e => e.Title == "Provision the box");
        Assert.Single(stored);
    }

    [Fact]
    public async Task Undoing_an_edit_made_before_a_delete_and_its_undo_still_reaches_the_entry()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Provision);
        await host.State.ChangeStatusAsync(row, EntryStatus.InProgress);
        await host.State.DeleteRowAsync(row);

        await host.State.UndoAsync();
        await host.State.UndoAsync();

        var restored = Assert.Single(host.State.Rows);
        Assert.Equal(EntryStatus.Ready, (await StoredAsync(host, restored.Id!.Value)).Status);
    }

    [Fact]
    public async Task Undoing_the_creation_of_an_entry_deletes_it()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync(Provision);

        await host.State.UndoAsync();

        Assert.Empty(host.State.Rows);
        Assert.Empty(await host.EntriesElsewhere().ListAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Marking an entry blocked is one change like any other, and undoing
    /// it takes the mark back off in the store — not only on the row.</summary>
    [Fact]
    public async Task Undo_takes_a_hand_set_block_back_off_and_redo_puts_it_on_again()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Provision);
        var day = new DateOnly(2026, 10, 5);

        await host.State.ChangeBlockedAsync(row, blocked: true, day);
        Assert.Equal(day, (await StoredAsync(host, row.Id!.Value)).BlockedSince);

        Assert.True(await host.State.UndoAsync());

        Assert.Null(row.PreviewBlockedSince);
        Assert.Null((await StoredAsync(host, row.Id!.Value)).BlockedSince);

        Assert.True(await host.State.RedoAsync());

        Assert.Equal(day, (await StoredAsync(host, row.Id!.Value)).BlockedSince);
    }

    [Fact]
    public async Task A_bulk_edit_is_one_undo_step()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var first = await host.WriteEntryAsync(Provision);
        var second = await host.WriteEntryAsync(Runbook);
        host.State.SetSelectionMode(true);
        host.State.SetSelection([first.TaskId, second.TaskId]);

        await host.State.BulkChangeStatusAsync(EntryStatus.Done);

        await host.State.UndoAsync();

        Assert.Equal(EntryStatus.Ready, (await StoredAsync(host, first.Id!.Value)).Status);
        Assert.Equal(EntryStatus.Draft, (await StoredAsync(host, second.Id!.Value)).Status);
    }

    [Fact]
    public async Task A_bulk_delete_comes_back_whole_with_one_undo()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var first = await host.WriteEntryAsync(Provision);
        var second = await host.WriteEntryAsync(Runbook);
        host.State.SetSelectionMode(true);
        host.State.SetSelection([first.TaskId, second.TaskId]);

        await host.State.BulkDeleteAsync();
        Assert.Empty(await host.EntriesElsewhere().ListAsync(TestContext.Current.CancellationToken));

        await host.State.UndoAsync();

        Assert.Equal(
            ["Provision the box", "Write the runbook"],
            host.State.Rows.Select(r => r.PreviewTitle).ToList());
        Assert.Equal(2, (await host.EntriesElsewhere().ListAsync(TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task Undo_keeps_a_write_made_elsewhere_since_the_change()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Provision);
        await host.State.ChangeStatusAsync(row, EntryStatus.InProgress);

        await host.FromElsewhereAsync(async elsewhere =>
        {
            var same = elsewhere.Rows.Single(r => r.Id == row.Id);
            elsewhere.ChangeBody(same, EntryTextParser.Parse(same.RawText).Body + "\n\n2026-10-02: A comment.");
            await elsewhere.EndEditAsync(same);
        });
        await host.State.ReloadFromStoreAsync();

        await host.State.UndoAsync();

        var stored = await StoredAsync(host, row.Id!.Value);
        Assert.Equal(EntryStatus.Ready, stored.Status);
        Assert.Contains("A comment.", EntryTextParser.ToRawText(stored), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_undo_says_what_it_took_back()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Provision);
        await host.State.ChangeStatusAsync(row, EntryStatus.InProgress);

        await host.State.UndoAsync();

        var toast = Assert.Single(host.Toasts.Visible, t => t.TestId == TasksDesktopState.UndoTestId);
        Assert.Equal("Undid editing “Provision the box”", toast.Message);
    }

    [Fact]
    public async Task With_nothing_to_undo_it_says_so_and_changes_nothing()
    {
        using var host = await TasksPaneHost.CreateAsync();

        Assert.False(await host.State.UndoAsync());

        var toast = Assert.Single(host.Toasts.Visible, t => t.TestId == TasksDesktopState.UndoTestId);
        Assert.Equal("Nothing to undo", toast.Message);
    }

    [Fact]
    public async Task The_keyboard_entry_points_undo_and_redo()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Provision);
        await host.State.ChangeStatusAsync(row, EntryStatus.InProgress);
        var pane = host.Render();

        await pane.Instance.UndoFromKeyboardAsync();
        Assert.Equal(EntryStatus.Ready, (await StoredAsync(host, row.Id!.Value)).Status);

        await pane.Instance.RedoFromKeyboardAsync();
        Assert.Equal(EntryStatus.InProgress, (await StoredAsync(host, row.Id!.Value)).Status);
    }

    [Fact]
    public async Task The_pane_registers_its_shortcut_with_the_document()
    {
        using var host = await TasksPaneHost.CreateAsync();

        host.Render();

        Assert.Contains(
            host.Context.JSInterop.Invocations,
            call => call.Identifier == "backlogUndoKeys.register" && Equals(call.Arguments[0], "backlog-pane"));
    }

    // --- Helpers -----------------------------------------------------------

    private static async Task TypeAndLetItSaveAsync(TasksPaneHost host, FakeTimeProvider clock, EntryRow row, string text)
    {
        host.State.OnRawTextInput(row, text);
        clock.Advance(TimeSpan.FromSeconds(1));

        // The save state turns Saved after the save has been put on the history,
        // so waiting for both is waiting for the step, not only for the write.
        var stored = string.Empty;
        await Polling.WaitUntilAsync(
            async () =>
            {
                stored = EntryTextParser.ToRawText(await StoredAsync(host, row.Id!.Value));
                return stored.Contains(text.Split('\n')[^2], StringComparison.Ordinal)
                    && host.State.SaveState == AppSaveState.Saved;
            },
            () => $"The debounced save never landed; the store holds: {stored}");
    }

    private static async Task<TaskItemDto> StoredAsync(TasksPaneHost host, Guid id) =>
        (await host.EntriesElsewhere().ListAsync(TestContext.Current.CancellationToken)).Single(entry => entry.Id == id);

    private static List<Guid> OrderOf(TasksPaneHost host) =>
        [.. host.State.Rows.Select(row => row.Id!.Value)];

    private static async Task<List<Guid>> StoredOrderAsync(TasksPaneHost host) =>
        [.. (await host.EntriesElsewhere().ListAsync(TestContext.Current.CancellationToken)).Select(entry => entry.Id)];
}
