using Backlog.Modules.Backlog.DomainModels;
using Microsoft.AspNetCore.Components.Web;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// When the detail pane is on screen, and how the keyboard gets in and out of it.
/// <para>
/// The two facts are the same fact. Selection is what puts the pane there, the
/// focus leaving both halves is what takes it away, and the only reason Tab is
/// intercepted at all is that reading order puts the pane after the entire list —
/// so plain Tab off the open row lands on the next row and the pane is reachable
/// only after every remaining one.
/// </para>
/// <para>
/// The focus itself is asserted through the interop the app uses to move it. A
/// bUnit render has no focus of its own, so "the focus went there" is only
/// observable as "the element to focus was named", which is exactly what the app
/// does: elements are named by id because they are different nodes after every
/// render.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class BacklogPaneFocusTests
{
    private const string Entry = "# Provision the box\n`task`\n";

    private const string OtherEntry = "# Deploy it\n`task`\n";

    /// <summary>The panel's first control, read off the element that carries the id
    /// rather than written down here. <c>TaskPanel</c> mints its own — it is one
    /// instance the pane holds, not a row a list rebuilds — so the honest way to
    /// name it is to ask the rendered panel which element it is.</summary>
    private static string DetailFirstControlId(IRenderedComponent<BacklogPane> pane) =>
        pane.Find("[data-testid='entry-panel-check']").Id!;

    private static string RowTestId(EntryRow row) => $"entry-list-{(row.Id ?? row.Key)}";

    private static string TaskId(EntryRow row) => (row.Id ?? row.Key).ToString();

    private static IReadOnlyList<string?> Focused(BacklogPaneHost host) =>
        [.. host.Context.JSInterop.Invocations["backlogFocus"].Select(call => call.Arguments[0] as string)];

    // --- On screen or not --------------------------------------------------

    /// <summary>Nothing selected, so the list gets the whole width and there is no
    /// placeholder standing in a column it could be using.</summary>
    [Fact]
    public async Task Nothing_selected_hides_the_pane_and_collapses_the_split()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        await host.WriteEntryAsync(Entry);
        await host.State.SelectAsync(null);

        var pane = host.Render();

        Assert.Empty(pane.FindAll("[data-testid='entry-detail']"));

        // Collapsed, not replaced. The split is still the element it was, because
        // swapping it for a bare list would rebuild the list and take the scroll
        // position and the focused row with it.
        var split = pane.Find("[data-testid='backlog-split']");
        Assert.Contains("backlog-split--solo", split.GetAttribute("class") ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Selecting_an_entry_puts_the_pane_back_and_reopens_the_split()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        await host.State.SelectAsync(null);

        var pane = host.Render();
        await pane.Find($"[data-testid='{RowTestId(row)}-open']").ClickAsync(new());

        Assert.Single(pane.FindAll("[data-testid='entry-detail']"));
        Assert.DoesNotContain(
            "backlog-split--solo",
            pane.Find("[data-testid='backlog-split']").GetAttribute("class") ?? string.Empty,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A resized panel stays resized, and the arrow that widens it is Left.
    /// <para>
    /// The split keeps the width the separator gave it in a parameter, so a pane that
    /// handed it a literal would put the column back on the next render — and this
    /// pane now renders on every focus move as well as on every state change, which
    /// is what turned a latent flaw into one a reader would hit while using the
    /// keyboard.
    /// </para>
    /// <para>
    /// The panel is the fixed half of the split now and the list flexes, so the
    /// number under test is the panel's 24rem rather than the list's 30rem — and
    /// Left, which drags the separator left, is what makes a right-hand panel wider.
    /// An arrow read as "less" regardless of which edge the pane is fixed to is
    /// exactly the bug <c>data-pane-anchor</c> was added to fix for the pointer.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_resized_entry_panel_survives_the_pane_re_rendering()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        await host.WriteEntryAsync(Entry);

        var pane = host.Render();

        await pane.Find("[data-testid='backlog-split-separator']")
            .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowLeft" });

        Assert.Contains(
            "26rem",
            pane.Find("[data-testid='backlog-split']").GetAttribute("style") ?? string.Empty,
            StringComparison.Ordinal);

        host.State.SetStatusFilter("ready");
        pane.Render();

        Assert.Contains(
            "26rem",
            pane.Find("[data-testid='backlog-split']").GetAttribute("style") ?? string.Empty,
            StringComparison.Ordinal);
    }

    // --- Tab across and back ----------------------------------------------

    /// <summary>Tab off the open row goes to the pane beside it rather than to the
    /// next row. The control it lands on is the panel's first — its circle — and the
    /// pane never names it: it asks the panel to focus itself, so which control that
    /// is stays the panel's answer.</summary>
    [Fact]
    public async Task Tab_from_the_selected_row_moves_the_focus_into_the_pane()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);

        var pane = host.Render();
        Assert.Same(row, host.State.SelectedRow);

        await pane.Find($"[data-testid='{RowTestId(row)}-open']").KeyDownAsync(new KeyboardEventArgs { Key = "Tab" });

        Assert.Contains(DetailFirstControlId(pane), Focused(host));
    }

    /// <summary>Only the open row hands the keyboard over. Every other row keeps
    /// plain Tab, which is the next row — the hand-off narrows the one case where
    /// document order is the wrong answer rather than taking Tab off the list.</summary>
    [Fact]
    public async Task Tab_from_a_row_that_is_not_open_is_left_to_the_browser()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var first = await host.WriteEntryAsync(Entry);
        await host.WriteEntryAsync(OtherEntry);

        var pane = host.Render();
        var before = Focused(host).Count;

        await pane.Find($"[data-testid='{RowTestId(first)}-open']").KeyDownAsync(new KeyboardEventArgs { Key = "Tab" });

        Assert.Equal(before, Focused(host).Count);
    }

    /// <summary>Shift+Tab off the front of the pane is the way back. The row's
    /// element id is the list's to mint, so what is asserted is that the focus was
    /// aimed at this task's row.</summary>
    [Fact]
    public async Task Shift_tab_from_the_front_of_the_pane_returns_to_the_row()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);

        var pane = host.Render();

        await pane.Find("[data-testid='entry-panel-check']")
            .KeyDownAsync(new KeyboardEventArgs { Key = "Tab", ShiftKey = true });

        Assert.Contains(Focused(host), id => id is not null && id.EndsWith(TaskId(row), StringComparison.Ordinal));
    }

    // --- Leaving ----------------------------------------------------------

    /// <summary>The focus left both halves, so nothing is open. This is what hides
    /// the pane again: there is no second gesture for closing it.</summary>
    [Fact]
    public async Task Focus_leaving_the_pane_clears_the_selection()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        await host.WriteEntryAsync(Entry);

        // The question the handler asks once the focus has settled: did it land on
        // something outside the pane? It did.
        host.Context.JSInterop.Setup<bool>("backlogFocusOutside", _ => true).SetResult(true);

        var pane = host.Render();
        await pane.Find("[data-testid='backlog-pane']").TriggerEventAsync("onfocusout", new FocusEventArgs());

        Assert.Null(host.State.SelectedRow);
    }

    /// <summary>Moving from the row into the pane is a move within the region the
    /// selection lives in, and clearing there would close the pane on its way to
    /// being used. The same answer covers picking a status chip and dragging the
    /// separator.</summary>
    [Fact]
    public async Task Moving_the_focus_from_the_row_into_the_pane_keeps_the_selection()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);

        host.Context.JSInterop.Setup<bool>("backlogFocusOutside", _ => true).SetResult(false);

        var pane = host.Render();
        await pane.Find($"[data-testid='{RowTestId(row)}-open']").KeyDownAsync(new KeyboardEventArgs { Key = "Tab" });
        await pane.Find("[data-testid='backlog-pane']").TriggerEventAsync("onfocusout", new FocusEventArgs());

        Assert.Same(row, host.State.SelectedRow);
        Assert.Single(pane.FindAll("[data-testid='entry-detail']"));
    }

    /// <summary>Escape closes the pane and puts the reader back where they came
    /// from. In that order: the list is asked to focus the row it still has
    /// selected, so clearing first would leave nothing to aim at.</summary>
    [Fact]
    public async Task Escape_closes_the_pane_and_returns_the_focus_to_the_row()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);

        var pane = host.Render();
        Assert.False(host.State.RawHatchOpen);

        await pane.Find("[data-testid='entry-detail']").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Null(host.State.SelectedRow);
        Assert.Contains(Focused(host), id => id is not null && id.EndsWith(TaskId(row), StringComparison.Ordinal));
    }

    /// <summary>With the source open, Escape is about the source. Closing the whole
    /// pane from under a reader who asked to leave one control would be two
    /// decisions taken out of one keystroke.</summary>
    [Fact]
    public async Task Escape_closes_the_markdown_hatch_before_the_pane()
    {
        using var host = await BacklogPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);

        var pane = host.Render();
        await pane.Find("[data-testid='entry-raw-toggle']").ClickAsync(new());
        Assert.True(host.State.RawHatchOpen);

        await pane.Find("[data-testid='entry-detail']").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.False(host.State.RawHatchOpen);
        Assert.Same(row, host.State.SelectedRow);
    }
}
