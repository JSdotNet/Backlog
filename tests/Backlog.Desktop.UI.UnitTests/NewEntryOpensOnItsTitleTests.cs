using Microsoft.AspNetCore.Components.Web;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What "+ New entry" opens on.
/// <para>
/// The title, with the caret in it — and <em>not</em> the raw-markdown escape
/// hatch. <c>.design/content-editing.md#raw-markdown-escape-hatch</c> puts the
/// canonical markdown behind Ctrl+Shift+M precisely so that it is not "the primary
/// surface <c>#editing-model</c> rules out", and opening a new entry straight into
/// it put two writing surfaces on one entry: a mono textarea with the placeholder
/// template in it, under the entry's own empty body editor.
/// </para>
/// <para>
/// The caret is asserted through the interop the app moves it with, the same way
/// <see cref="BacklogPaneFocusTests"/> does: a bUnit render has no focus of its
/// own, so "the focus went there" is only observable as "that element was named".
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class NewEntryOpensOnItsTitleTests
{
    private const string ExistingEntry = "# Deploy SpecManager\n`task` `!ready` `@repos`\n";

    private static IReadOnlyList<string?> Focused(BacklogPaneHost host) =>
        [.. host.Context.JSInterop.Invocations["backlogFocus"].Select(call => call.Arguments[0] as string)];

    /// <summary>Presses the control a reader presses, and hands back the draft it
    /// appended. Through the button rather than through the state, because half of
    /// what is under test happens in the render the click causes.</summary>
    private static async Task<EntryRow> AddEntryAsync(BacklogPaneHost host, IRenderedComponent<BacklogPane> pane)
    {
        await pane.Find("[data-testid='new-entry-button']").ClickAsync(new());
        return host.State.Rows[^1];
    }

    // --- The hatch is not there -------------------------------------------

    /// <summary>The bug, stated as the two facts it is: the hatch is closed, and
    /// nothing of it is on screen. Not rendered rather than rendered-and-hidden —
    /// a textarea nobody can see is still a textarea the caret can land in.</summary>
    [Fact]
    public async Task A_new_entry_does_not_open_the_raw_markdown_hatch()
    {
        using var host = await BacklogPaneHost.CreateAsync();

        var pane = host.Render();
        await AddEntryAsync(host, pane);

        Assert.False(host.State.RawHatchOpen);
        Assert.Empty(pane.FindAll("[data-testid='entry-raw-input']"));

        // And with it the "reads as" hint, which is the hatch's own line and not
        // the pane's: it read as an error message on an entry with nothing in it.
        Assert.Empty(pane.FindAll("[data-testid='entry-meta-reading']"));
    }

    // --- What it opens on instead -----------------------------------------

    /// <summary>The entry is open in the pane and the caret is in its title field.
    /// The field is the panel's own — the pane asks the panel to open it rather than
    /// hand-rolling a second title input beside the one the library already
    /// draws.</summary>
    [Fact]
    public async Task A_new_entry_opens_with_the_caret_in_its_title()
    {
        using var host = await BacklogPaneHost.CreateAsync();

        var pane = host.Render();
        var row = await AddEntryAsync(host, pane);

        Assert.Same(row, host.State.SelectedRow);
        Assert.Contains(row, host.State.FilteredRows);

        // The id is read off the element that carries it. TaskPanel mints its own,
        // so asking the rendered panel which element it is is the only honest way
        // to name it.
        var title = pane.Find("[data-testid='entry-panel-rename']");
        Assert.Contains(title.Id, Focused(host));
    }

    // --- The hatch is still one keystroke away ----------------------------

    /// <summary>Not rendered is not unreachable. The shortcut works on an entry
    /// with nothing in it — which is the entry a reader most often wants the format
    /// spelled out for — and Escape gives the fields back.</summary>
    [Fact]
    public async Task Ctrl_shift_m_still_opens_the_hatch_on_a_brand_new_entry()
    {
        using var host = await BacklogPaneHost.CreateAsync();

        var pane = host.Render();
        var row = await AddEntryAsync(host, pane);

        await pane.Find("[data-testid='entry-detail']")
            .KeyDownAsync(new KeyboardEventArgs { Key = "M", CtrlKey = true, ShiftKey = true });

        Assert.True(host.State.RawHatchOpen);
        Assert.Single(pane.FindAll("[data-testid='entry-raw-input']"));

        await pane.Find("[data-testid='entry-detail']")
            .KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.False(host.State.RawHatchOpen);
        Assert.Empty(pane.FindAll("[data-testid='entry-raw-input']"));

        // The draft survived the round trip. Escape with the hatch open is about
        // the hatch, so the entry it was a view of is still open and still in the
        // list — a keystroke about one control may not discard the document.
        Assert.Same(row, host.State.SelectedRow);
        Assert.Contains(row, host.State.Rows);
    }

    // --- A titleless draft is still an entry you can write ----------------

    /// <summary>Typing the title is what saves the entry: the domain requires one,
    /// so the draft is held locally until there is one and persisted the moment
    /// there is. Through the panel's field, because that is now the first thing the
    /// caret is in.</summary>
    [Fact]
    public async Task Typing_the_title_on_a_fresh_draft_saves_it()
    {
        using var host = await BacklogPaneHost.CreateAsync();

        var pane = host.Render();
        var row = await AddEntryAsync(host, pane);

        await pane.Find("[data-testid='entry-panel-rename']").InputAsync(new() { Value = "Provision the box" });
        await pane.Find("[data-testid='entry-panel-rename']").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.True(row.IsPersisted);
        Assert.Equal("Provision the box", row.PreviewTitle);
        Assert.StartsWith("# Provision the box", row.RawText, StringComparison.Ordinal);
    }

    /// <summary>With an area filtered, a new entry starts already filed there — and
    /// the rename fills the empty heading the seed left rather than pushing a second
    /// one in front of it, so the seeded metadata line survives.</summary>
    [Fact]
    public async Task Typing_the_title_keeps_the_area_a_new_entry_was_seeded_with()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        await host.WriteEntryAsync(ExistingEntry);
        host.State.SetAreaFilter("repos");

        var pane = host.Render();
        var row = await AddEntryAsync(host, pane);

        Assert.Contains("`@repos`", row.RawText, StringComparison.Ordinal);

        await pane.Find("[data-testid='entry-panel-rename']").InputAsync(new() { Value = "Provision the box" });
        await pane.Find("[data-testid='entry-panel-rename']").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.True(row.IsPersisted);
        Assert.Equal("Provision the box", row.PreviewTitle);
        Assert.Equal("repos", row.PreviewArea);
    }

    /// <summary>A new entry stays in view under a filter it does not match. It used
    /// to stay because it was the row with an editor open, which the filter pins; it
    /// has no editor now, and an unsaved draft exists nowhere but this list — so a
    /// filter that dropped it would not be hiding an entry, it would be losing
    /// one.</summary>
    [Fact]
    public async Task A_new_entry_stays_in_view_under_a_filter_it_does_not_match()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        await host.WriteEntryAsync(ExistingEntry);
        host.State.SetStatusFilter("ready");

        var pane = host.Render();
        var row = await AddEntryAsync(host, pane);

        Assert.Same(row, host.State.SelectedRow);
        Assert.Contains(row, host.State.FilteredRows);
        Assert.Single(pane.FindAll("[data-testid='entry-detail']"));
    }

    /// <summary>With a repository scoped, a new entry starts already targeting it —
    /// the same bargain <see cref="Typing_the_title_keeps_the_area_a_new_entry_was_seeded_with"/>
    /// documents for an area filter, extended to the other scope
    /// <see cref="BacklogDesktopState.RowBelongsToSelectedRepository"/> reads the
    /// same field to decide. Without the seed, a repository's rows are exactly the
    /// rows whose `repo:` names it — so an entry created targeting nothing would
    /// pass the filter only while pinned as an unpersisted draft, then drop out of
    /// <c>FilteredRows</c> the moment its title saved and closed the pane on it.</summary>
    [Fact]
    public async Task A_new_entry_is_seeded_with_the_scoped_repository()
    {
        using var host = await BacklogPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        await host.WriteEntryAsync("# Deploy SpecManager\n`task` `!ready` `repo:backlog`\n");
        host.State.SetRepositoryFilter("backlog");

        var pane = host.Render();
        var row = await AddEntryAsync(host, pane);

        // A `repo:` seed rather than an `@area` one: the scope reads the entry's
        // targets, and filing it under a pile named after the repository would be
        // the list writing the reader's own taxonomy for them.
        Assert.Contains("`repo:backlog`", row.RawText, StringComparison.Ordinal);
        Assert.Null(row.PreviewArea);
    }

    /// <summary>The regression itself: typing the title on a repository-scoped draft
    /// must not vanish it from the list or close the pane on it. Before the seed
    /// above, the draft passed the filter only as an unpersisted, selected row — a
    /// pin that <see cref="BacklogDesktopState.ApplyFilter"/> withdraws the instant
    /// the save gives it an id, so the very act of naming the entry made it
    /// disappear.</summary>
    [Fact]
    public async Task Typing_the_title_keeps_a_repository_scoped_entry_visible()
    {
        using var host = await BacklogPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        await host.WriteEntryAsync("# Deploy SpecManager\n`task` `!ready` `repo:backlog`\n");
        host.State.SetRepositoryFilter("backlog");

        var pane = host.Render();
        var row = await AddEntryAsync(host, pane);

        await pane.Find("[data-testid='entry-panel-rename']").InputAsync(new() { Value = "Provision the box" });
        await pane.Find("[data-testid='entry-panel-rename']").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        Assert.True(row.IsPersisted);
        Assert.Equal(["backlog"], row.PreviewRepoIds);
        Assert.Same(row, host.State.SelectedRow);
        Assert.Contains(row, host.State.FilteredRows);
        Assert.Single(pane.FindAll("[data-testid='entry-detail']"));
    }

    // --- And an abandoned one is not an entry at all ----------------------

    /// <summary>A draft nobody wrote in goes when the reader moves on, rather than
    /// leaving an "Untitled" husk in the list. It used to be the editor closing that
    /// said so; a new entry no longer has one open, so leaving the pane is the
    /// moment that does.</summary>
    [Fact]
    public async Task A_draft_nobody_wrote_in_goes_when_the_pane_moves_on()
    {
        using var host = await BacklogPaneHost.CreateAsync();

        var pane = host.Render();
        var row = await AddEntryAsync(host, pane);

        await host.State.SelectAsync(null);

        Assert.DoesNotContain(row, host.State.Rows);
        Assert.Null(host.State.SelectedRow);
    }
}
