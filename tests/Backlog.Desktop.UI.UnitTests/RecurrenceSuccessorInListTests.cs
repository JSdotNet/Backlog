using Backlog.Modules.Tasks.DomainModels;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Completing a recurring entry has to put the next occurrence on screen.
/// <para>
/// It was already being written to the store — that part was never broken. What was
/// missing is that the list only ever refreshes the row it just saved, and the
/// successor is by definition not that row, so the new entry stayed invisible until
/// something else happened to reload. A repeating backlog that silently produced
/// entries nobody could see is worse than one that produced none: the work exists
/// and the reader has no way to know.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class RecurrenceSuccessorInListTests
{
    private const string Repeating =
        "# Weekly review\n" +
        "`task` `!in-progress` `due:2026-08-21` `repeat:weekly`\n\n" +
        "Read the week back.\n";

    [Fact]
    public async Task Completing_a_recurring_entry_puts_its_successor_in_the_list()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Repeating);

        Assert.Single(host.State.Rows);

        await host.State.ChangeStatusAsync(row, EntryStatus.Done);

        // Two rows: the occurrence that was finished, and the one that follows it.
        // The finished one stays — it is the record of what was done.
        Assert.Equal(2, host.State.Rows.Count);
        Assert.Contains(host.State.Rows, r => r.PreviewStatus == EntryStatus.Done);

        var successor = Assert.Single(host.State.Rows, r => r.PreviewStatus != EntryStatus.Done);
        Assert.Equal(new DateOnly(2026, 8, 28), successor.PreviewDueOn);
        Assert.Equal("Weekly review", successor.PreviewTitle);
    }

    [Fact]
    public async Task The_successor_is_on_screen_rather_than_only_in_the_store()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Repeating);

        var pane = host.Render();

        // One title per row in the list. The list is the shared TaskListView now, so
        // a row's title carries the list's testid plus the row id rather than a bare
        // `entry-title` the pane wrote itself — same claim, said in the vocabulary
        // the markup actually has.
        Assert.Equal(1, EntryTitles(pane));
        Assert.Empty(pane.FindAll("[data-testid='entry-list-completed']"));

        await host.State.ChangeStatusAsync(row, EntryStatus.Done);
        pane.Render();

        // Still one row in the open list — but a different one. The occurrence that
        // was finished moved to the completed section, which the shared list folds
        // away by default, and the successor took its place. That is the whole claim:
        // an entry the store produced is on screen rather than only in the store.
        Assert.Equal(1, EntryTitles(pane));
        Assert.Contains("Weekly review", pane.Find("[data-testid='entry-list']").TextContent, StringComparison.Ordinal);

        // And the finished one is accounted for rather than gone: the count beside
        // the fold says so.
        var completed = pane.Find("[data-testid='entry-list-completed']");
        Assert.Contains("1", completed.TextContent, StringComparison.Ordinal);
    }

    /// <summary>The same thing when the completion arrives as typed text rather
    /// than through the status badge. Both routes are one save, which is the point
    /// of the entry being its text.</summary>
    [Fact]
    public async Task Typing_the_completion_and_leaving_the_editor_shows_the_successor()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Repeating);

        host.State.BeginEdit(row);
        host.State.OnRawTextInput(row, Repeating.Replace("!in-progress", "!done", StringComparison.Ordinal));
        await host.State.EndEditAsync(row);

        Assert.Equal(2, host.State.Rows.Count);
    }

    /// <summary>
    /// A reload replaces every row object, so it waits until no editor is open.
    /// <para>
    /// Doing it immediately would take the textarea out from under whoever is
    /// typing — the debounced save fires 750ms into a sentence, and the row it
    /// belongs to would be gone by the next keystroke. The flag survives instead,
    /// and blurring flushes it through on the way out.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_list_does_not_reload_under_a_live_caret()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Repeating);

        host.State.BeginEdit(row);
        host.State.OnRawTextInput(row, Repeating.Replace("!in-progress", "!done", StringComparison.Ordinal));

        // The save the debounce would have made, made now so the test does not
        // wait on a timer.
        await host.State.ChangeStatusAsync(row, EntryStatus.Done);

        // The successor exists in the store, and the row being edited is still the
        // same object the editor is bound to.
        Assert.Single(host.State.Rows);
        Assert.Same(row, host.State.EditingRow);

        await host.State.EndEditAsync(row);

        Assert.Equal(2, host.State.Rows.Count);
        Assert.Null(host.State.EditingRow);
    }

    /// <summary>An ordinary save reloads nothing. The list is rebuilt because a
    /// spawn happened, not because a save did — a reload per keystroke would
    /// rebuild every row on the screen 750ms into every sentence.</summary>
    [Fact]
    public async Task A_save_that_spawned_nothing_leaves_the_rows_alone()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Water the plants\n`task` `!in-progress`\n\nThe big one.\n");

        await host.State.ChangeStatusAsync(row, EntryStatus.Done);

        // Same object, so nothing was rebuilt: a reload would have replaced it.
        Assert.Same(row, Assert.Single(host.State.Rows));
    }

    /// <summary>How many entry titles are on screen, wherever the list has put them.
    /// Finished rows go to their own section at the bottom of the shared list, and a
    /// selector tied to the open group would stop counting the occurrence that was
    /// just completed — which is one of the two rows this is about. Both sections
    /// name a row's title <c>entry-list-{id}-title</c>, so one selector reaches
    /// both.</summary>
    private static int EntryTitles(IRenderedComponent<TasksPane> pane) =>
        pane.FindAll("[data-testid^='entry-list-'][data-testid$='-title']").Count;
}
