using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The two pickers a backlog row carries: which repository it is filed against and
/// where it has got to.
/// <para>
/// Both facts were already on the row before this — the repository as a colour
/// stripe down its leading edge, the status as a read-only badge beside the title —
/// and neither could be changed without opening the entry. Filing a row and moving
/// it along are the two edits a reader makes while scanning a column, and a column
/// that made them both a two-step round trip through the panel was a column you read
/// rather than one you triage in.
/// </para>
/// <para>
/// The pickers live in the row's action slot, at the end of the row, because the
/// status badge they replace was drawn <em>inside</em> the row's own button and a
/// <c>select</c> cannot nest in a button. That the slot also stops clicks and
/// mousedowns is the second half of why it is the right place: changing a status is
/// not opening the entry.
/// </para>
/// </summary>
public class TasksRowPickersTests
{
    private static string RowTestId(EntryRow row) => $"entry-list-{(row.Id ?? row.Key)}";

    private static IElement Row(IRenderedComponent<TasksPane> pane, EntryRow row) =>
        pane.Find($"[data-testid='{RowTestId(row)}']");

    private static IElement Picker(IRenderedComponent<TasksPane> pane, EntryRow row, string testId) =>
        Row(pane, row).QuerySelector($"[data-testid='{testId}'] select")
            ?? throw new InvalidOperationException($"The row for '{row.PreviewTitle}' has no {testId}.");

    [Fact]
    public async Task ARowCarriesBothPickersShowingWhereTheEntryStands()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs");
        var row = await host.WriteEntryAsync("# Provision the box\n`task` `@docs` `!in-progress`\n");

        var pane = host.Render();

        // Preselected, not merely present. A picker that opens on the first option
        // is a control that misreports the row until somebody touches it.
        Assert.Equal("docs", Picker(pane, row, "row-area-badge").GetAttribute("value"));
        Assert.Equal(nameof(EntryStatus.InProgress), Picker(pane, row, "row-status-badge").GetAttribute("value"));
    }

    [Fact]
    public async Task EachPickerSaysWhichRowItIsFor()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var row = await host.WriteEntryAsync("# Provision the box\n`task`\n");

        var pane = host.Render();

        // A column of these reads as one control repeated unless each says whose it
        // is — the same reason the row's pencil is "Rename Provision the box".
        Assert.Equal("Change status of Provision the box", Picker(pane, row, "row-status-badge").GetAttribute("aria-label"));
        Assert.Equal("Change repository of Provision the box", Picker(pane, row, "row-area-badge").GetAttribute("aria-label"));
    }

    [Fact]
    public async Task ChangingARowsStatusWritesItToTheEntry()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var row = await host.WriteEntryAsync("# Provision the box\n`task`\n");

        var pane = host.Render();
        await Picker(pane, row, "row-status-badge").ChangeAsync(new() { Value = nameof(EntryStatus.Ready) });

        // The markdown is canonical, so that is where the assertion is: the token is
        // what survives a restart, and the row is redrawn from it.
        Assert.Contains("`!ready`", row.RawText, StringComparison.Ordinal);
        Assert.Equal(EntryStatus.Ready, row.PreviewStatus);
        Assert.Equal(nameof(EntryStatus.Ready), Picker(pane, row, "row-status-badge").GetAttribute("value"));
    }

    [Fact]
    public async Task FinishingARowFromTheListMovesItUnderCompleted()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Provision the box\n`task`\n");

        var pane = host.Render();
        Assert.Empty(pane.FindAll("[data-testid='entry-list-completed']"));

        await Picker(pane, row, "row-status-badge").ChangeAsync(new() { Value = nameof(EntryStatus.Done) });

        // Done is not one more word on the row: it is what the list groups on, and a
        // picker that set the status without the row folding away would be a second,
        // quieter notion of finished sitting beside the circle's. The fold is shut by
        // default, so the row leaving the open list is the whole of the evidence.
        Assert.True(row.PreviewStatus is EntryStatus.Done);
        Assert.NotNull(pane.Find("[data-testid='entry-list-completed']"));
        Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(row)}']"));
    }

    [Fact]
    public async Task ChangingARowsRepositoryFilesItThereAndRepaintsTheMark()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs");
        Assert.Null(host.GitHub.Settings.SetShowRepositoryColours(true));
        var row = await host.WriteEntryAsync("# Provision the box\n`task` `@backlog`\n");

        var pane = host.Render();
        Assert.Contains("repo-mark--1", Row(pane, row).ClassName);

        await Picker(pane, row, "row-area-badge").ChangeAsync(new() { Value = "docs" });

        // Filing is an area write, exactly as it is in the panel — and the stripe is
        // derived from the area rather than set beside it, so it follows or the two
        // disagree about which project the row belongs to.
        Assert.Equal("docs", row.PreviewArea);
        Assert.Contains("repo-mark--2", Row(pane, row).ClassName);
    }

    [Fact]
    public async Task ARowCanBeUnfiledFromTheList()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        Assert.Null(host.GitHub.Settings.SetShowRepositoryColours(true));
        var row = await host.WriteEntryAsync("# Provision the box\n`task` `@backlog`\n");

        var pane = host.Render();
        await Picker(pane, row, "row-area-badge").ChangeAsync(new() { Value = string.Empty });

        // The empty option is "No repo", and it has to mean it: a picker that could
        // only ever move an entry between repositories would be one that made the
        // first filing permanent.
        Assert.Null(row.PreviewArea);
        Assert.DoesNotContain("repo-mark", Row(pane, row).ClassName);
    }

    [Fact]
    public async Task TheRepositoryPickerIsAbsentWhenNoRepositoryIsConfigured()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Provision the box\n`task` `@errands`\n");

        var pane = host.Render();

        // Nothing to pick. An empty picker offering only "No repo" would be a
        // control whose one option is the state it is already in — and the pane
        // makes the same call in the panel, from the same list.
        Assert.Null(Row(pane, row).QuerySelector("[data-testid='row-area-badge']"));
        Assert.NotNull(Row(pane, row).QuerySelector("[data-testid='row-status-badge']"));
    }

    /// <summary>
    /// Reaching for a picker is not reaching for the row it is on.
    /// <para>
    /// Opening a row, renaming it and re-ranking it all start from gestures the
    /// pointer or the keyboard could plausibly make while aiming at a control at the
    /// end of it, and each is stopped by a different arrangement: the click by the
    /// action slot, which already stops <c>click</c> and <c>mousedown</c> before the
    /// row sees either; the keys by where the handlers are, since the row's own key
    /// handling sits on its title button and the list's on its add field, both of
    /// which are siblings of the picker rather than ancestors of it.
    /// </para>
    /// <para>
    /// Asserted as "nothing up the tree is listening", because that is literally the
    /// claim. bUnit bubbles an event the way the browser does and stops where the
    /// markup says to, so a gesture from the picker that reaches no handler at all is
    /// the strongest form of the guarantee — there is no handler left to have been
    /// suppressed by luck. The same gesture on the row's own body is the control
    /// case, and it does what it has always done.
    /// </para>
    /// </summary>
    [Fact]
    public async Task OperatingAPickerDoesNotOpenRenameOrReorderTheRow()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var first = await host.WriteEntryAsync("# Provision the box\n`task`\n");
        var second = await host.WriteEntryAsync("# Write it up\n`task`\n");

        var pane = host.Render();

        // The second entry is the open one — writing an entry opens it — so the row
        // under test is deliberately not the selected one, and a click that reached
        // the row would be visible as the panel changing entries.
        Assert.Same(second, host.State.SelectedRow);

        var picker = Picker(pane, first, "row-status-badge");

        await Assert.ThrowsAsync<MissingEventHandlerException>(() => picker.ClickAsync(new()));
        await Assert.ThrowsAsync<MissingEventHandlerException>(
            () => picker.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" }));
        await Assert.ThrowsAsync<MissingEventHandlerException>(
            () => picker.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowUp", AltKey = true }));

        Assert.Same(second, host.State.SelectedRow);
        Assert.Null(Row(pane, first).QuerySelector($"[data-testid='{RowTestId(first)}-rename']"));
        Assert.Equal([first, second], host.State.FilteredRows);

        // Drag needs no guard of its own here. Reordering is a pointer gesture the
        // shared library runs from the document, and it declines any press landing
        // in the action slot — so what keeps these pickers out of it is where they
        // are rendered, not an attribute on them. The pair used to carry a
        // draggable="false" and a dragstart stopPropagation for the native drag the
        // row no longer uses; this asserts the slot, which is what now answers.
        var pickers = Row(pane, first).QuerySelector(".task-item__actions .entry-row__pickers");

        Assert.NotNull(pickers);
        Assert.False(pickers.HasAttribute("draggable"));

        // And the row itself still opens, so what the picker is not reaching is a
        // live control rather than one that had already gone.
        await pane.Find($"[data-testid='{RowTestId(first)}-open']").ClickAsync(new());
        Assert.Same(first, host.State.SelectedRow);
    }

    [Fact]
    public async Task TheStatusIsNotAlsoDrawnAsAReadOnlyBadge()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Provision the box\n`task` `!in-progress`\n");

        var pane = host.Render();

        // The picker states it. A badge saying the same word two elements to the
        // left would be the row telling the reader twice, and the one they cannot
        // act on would be the one their eye lands on first.
        Assert.Null(pane.Find($"[data-testid='{RowTestId(row)}']").QuerySelector($"[data-testid='{RowTestId(row)}-status']"));
        Assert.Empty(pane.FindAll(".task-item__status"));
    }

    [Fact]
    public async Task TheAreaLeavesTheMetadataLineOnlyWhenThePickerIsSayingIt()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var filed = await host.WriteEntryAsync("# Provision the box\n`task` `@backlog`\n");
        var piled = await host.WriteEntryAsync("# Buy milk\n`task` `@errands`\n");

        var pane = host.Render();

        // An area that names a repository is what the picker beside it is set to, so
        // the metadata line drops it. An area that names a pile is not — nothing else
        // on the row says "errands", so the line keeps saying it.
        Assert.Null(Row(pane, filed).QuerySelector(".task-item__meta"));
        Assert.Contains("errands", Row(pane, piled).QuerySelector(".task-item__meta")!.TextContent);
    }
}
