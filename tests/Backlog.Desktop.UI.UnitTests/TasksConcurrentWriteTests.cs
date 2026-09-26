using Backlog.Modules.Tasks.Abstractions.Services;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A write that lands on an entry while the reader's own edit of it is still on
/// its way to the store.
/// <para>
/// The list saves whole entries: the text on screen is the entry. So a save built
/// on the text as it stood before somebody else's write — an agent's comment
/// through the MCP server, a sync pull — used to put that older text back and
/// erase the other write without a word. The reload that write asked for is
/// rightly put off while a keystroke's save is pending or the raw hatch is open;
/// what these hold is that the save that then runs carries the other write
/// forward instead of writing over it.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class TasksConcurrentWriteTests
{
    private const string Comment = "2026-09-26: Picked up by an agent.";

    /// <summary>
    /// The reported case: typing in the Markdown block, the comment arrives inside
    /// the 750ms a keystroke waits before it saves, and the typing carries on.
    /// </summary>
    [Fact]
    public async Task A_comment_that_lands_while_a_keystroke_save_is_pending_survives_that_save()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Draft the rollout\n`task` `!ready`\n\nFirst paragraph.\n");
        await host.OpenAsync(row);

        var pane = host.Render();
        await pane.Find("[data-testid='entry-body-editor'] textarea")
            .InputAsync(new() { Value = "First paragraph, being extended\n" });

        await CommentFromElsewhereAsync(host, row.Id!.Value);

        // What the shell does on hearing the write: ask for a reload, which is put
        // off because a keystroke's save is still waiting.
        await host.State.ReloadFromStoreAsync();
        Assert.True(host.State.ReloadIsDeferred);

        await pane.Find("[data-testid='entry-body-editor'] textarea")
            .InputAsync(new() { Value = "First paragraph, being extended further\n" });

        pane.WaitForAssertion(
            () =>
            {
                Assert.False(host.State.ReloadIsDeferred);
                var open = host.State.SelectedRow;
                Assert.NotNull(open);
                Assert.Contains("being extended further", open!.RawText, StringComparison.Ordinal);
                Assert.Contains(Comment, open.RawText, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(5));

        var stored = await StoredBodyAsync(host, row.Id!.Value);
        Assert.Contains("being extended further", stored, StringComparison.Ordinal);
        Assert.Contains(Comment, stored, StringComparison.Ordinal);
    }

    /// <summary>
    /// The raw hatch puts the reload off until it closes, and its close is a
    /// flush of the whole text — the same stale base, held for as long as the
    /// reader keeps the editor open.
    /// </summary>
    [Fact]
    public async Task A_comment_that_lands_while_the_raw_hatch_is_open_survives_the_hatch_closing()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Draft the rollout\n`task` `!ready`\n\nFirst paragraph.\n");

        host.State.BeginEdit(row);
        host.State.OnRawTextInput(row, "# Draft the rollout plan\n`task` `!ready`\n\nFirst paragraph.\n");

        await CommentFromElsewhereAsync(host, row.Id!.Value);
        await host.State.ReloadFromStoreAsync();
        Assert.True(host.State.ReloadIsDeferred);

        await host.State.EndEditAsync(row);

        var stored = (await host.EntriesElsewhere().ListAsync(TestContext.Current.CancellationToken)).Single(e => e.Id == row.Id);
        Assert.Equal("Draft the rollout plan", stored.Title);
        Assert.Contains(Comment, stored.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// When both writers changed the same line there is no merge that keeps both
    /// as one line. Neither is dropped: both versions stay in the body, and the
    /// reader is told to look, because the list cannot choose for them.
    /// </summary>
    [Fact]
    public async Task Two_writes_to_the_same_line_keep_both_and_say_so()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Draft the rollout\n`task` `!ready`\n\nFirst paragraph.\n\nSecond paragraph.\n");

        host.State.BeginEdit(row);
        host.State.OnRawTextInput(row, "# Draft the rollout\n`task` `!ready`\n\nMine: first paragraph.\n\nSecond paragraph.\n");

        var elsewhere = host.EntriesElsewhere();
        var entry = (await elsewhere.ListAsync(TestContext.Current.CancellationToken)).Single(e => e.Id == row.Id);
        var theirs = EntryTextParser.ToRawText(entry).Replace("First paragraph.", "Theirs: first paragraph.", StringComparison.Ordinal);
        Assert.True((await elsewhere.SaveFromTextAsync(entry.Id, theirs, entry.Order, cancellationToken: TestContext.Current.CancellationToken)).IsSuccess);
        await host.State.ReloadFromStoreAsync();

        await host.State.EndEditAsync(row);

        var stored = await StoredBodyAsync(host, row.Id!.Value);
        Assert.Contains("Mine: first paragraph.", stored, StringComparison.Ordinal);
        Assert.Contains("Theirs: first paragraph.", stored, StringComparison.Ordinal);
        Assert.Contains("Second paragraph.", stored, StringComparison.Ordinal);

        Assert.Contains(host.Toasts.Visible, toast => toast.TestId == "entry-changed-elsewhere");
    }

    /// <summary>What <c>TrackerTools.CommentAsync</c> writes: a dated line appended
    /// to the parent text, through a use-case graph the list does not share.</summary>
    private static async Task CommentFromElsewhereAsync(TasksPaneHost host, Guid id)
    {
        var entries = host.EntriesElsewhere();
        var entry = (await entries.ListAsync(TestContext.Current.CancellationToken)).Single(e => e.Id == id);
        var raw = EntryTextParser.ToRawText(entry);
        var parent = EntryTextParser.GetParentText(raw).TrimEnd('\n');
        var saved = await entries.SaveFromTextAsync(
            entry.Id,
            EntryTextParser.ReplaceParentText(raw, $"{parent}\n\n{Comment}"),
            entry.Order,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(saved.IsSuccess);
    }

    private static async Task<string> StoredBodyAsync(TasksPaneHost host, Guid id) =>
        (await host.EntriesElsewhere().ListAsync(TestContext.Current.CancellationToken)).Single(e => e.Id == id).Body;
}
