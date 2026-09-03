using System.Globalization;

using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// One field, changed on every row a reader has picked.
/// <para>
/// There is no field setter anywhere behind this pane: every change rewrites the
/// entry's metadata line and saves the text (ADR 0002 — the module owns the entry
/// text language). A bulk edit is therefore N rewrites of N different texts, and
/// that is exactly what these tests are here to pin down: <c>ApplyToExisting</c>
/// reads an absent token as "clear that field", so one metadata line applied to
/// twenty rows would quietly wipe every other field on nineteen of them. Almost
/// every assertion below is about <c>RawText</c> for that reason, and at least one
/// per field is about a token the edit was not supposed to touch.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class TasksBulkEditTests
{
    /// <summary>Two entries with deliberately different metadata on them, so a
    /// rewrite that used one row's line for both would show up as the second row
    /// wearing the first one's facts.</summary>
    private const string First =
        "# Provision the box\n" +
        "`task` `*high` `!ready` `@platform` `due:2026-08-21` `#infra`\n\n" +
        "Rack it first.\n";

    private const string Second =
        "# Write the runbook\n" +
        "`idea` `*low` `!draft` `@docs` `remind:2026-09-01T09:00` `#writing`\n\n" +
        "Then say how.\n";

    private static string TodayToken =>
        DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // --- Getting into a selection ------------------------------------------

    private static string RowTestId(EntryRow row) => $"entry-list-{row.TaskId}";

    /// <summary>
    /// Ticks a row's gutter box, asking for the gutter first if it is not there
    /// yet.
    /// <para>
    /// Selection is a mode the reader turns on rather than a column of boxes
    /// every list carries, so there is no box to tick until the chip above the
    /// list has been pressed. Folded in here rather than written out in twenty
    /// tests, because none of them is about how the mode is entered — and the
    /// pressed check keeps a second row from turning it back off again.
    /// </para>
    /// </summary>
    private static async Task PickAsync(IRenderedComponent<TasksPane> pane, EntryRow row)
    {
        if (pane.FindAll("[data-testid='bulk-select-toggle'][aria-pressed='true']").Count == 0)
        {
            await EnterSelectionModeAsync(pane);
        }

        await pane.Find($"[data-testid='{RowTestId(row)}-select'] input").ClickAsync(new());
    }

    private static Task EnterSelectionModeAsync(IRenderedComponent<TasksPane> pane) =>
        pane.Find("[data-testid='bulk-select-toggle']").ClickAsync(new());

    /// <summary>
    /// Presses one of the bar's group triggers, which is what puts that group's
    /// controls on screen.
    /// <para>
    /// The resting bar is five triggers on one line and no live control at all, so
    /// every field test says which group it is about before it can reach a field.
    /// One group is open at a time, so this also closes whatever was open.
    /// </para>
    /// </summary>
    private static Task OpenGroupAsync(IRenderedComponent<TasksPane> pane, string group) =>
        pane.Find($"[data-testid='bulk-action-{group}-set']").ClickAsync(new());

    /// <summary>A select in one of the bar's groups, with the group opened on the
    /// way. The group a field belongs to is named here rather than derived, because
    /// which subject a field sits under is exactly what the band is asserting.</summary>
    private static async Task<IElement> OpenSelectAsync(IRenderedComponent<TasksPane> pane, string field)
    {
        await OpenGroupAsync(pane, GroupOf(field));
        return pane.Find($"[data-testid='bulk-{field}'] select");
    }

    private static string GroupOf(string field) => field switch
    {
        "repo" => "repo",
        "status" or "priority" or "type" => "classification",
        "due" or "remind" => "scheduling",
        "tags" or "tag-remove" => "tags",
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "No group holds that field.")
    };

    private static string Result(IRenderedComponent<TasksPane> pane) =>
        pane.Find("[data-testid='bulk-result']").TextContent;

    /// <summary>The pane, two entries written into it, and both of them picked.
    /// Every field test starts here, because every one of them is about what a
    /// change does to two rows rather than to one.</summary>
    private static async Task<(IRenderedComponent<TasksPane> Pane, EntryRow One, EntryRow Two)> TwoPickedAsync(
        TasksPaneHost host)
    {
        var one = await host.WriteEntryAsync(First);
        var two = await host.WriteEntryAsync(Second);

        // Nothing open beside the list: a bulk edit is a decision about a set of
        // rows, and leaving a detail pane open would make it ambiguous which of
        // the two surfaces a control belonged to.
        await host.State.SelectAsync(null);

        var pane = host.Render();

        await PickAsync(pane, one);
        await PickAsync(pane, two);

        return (pane, one, two);
    }

    // --- The bar itself (AC1) ----------------------------------------------

    [Fact]
    public async Task With_nothing_picked_there_is_no_bar()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync(First);
        await host.State.SelectAsync(null);

        var pane = host.Render();

        Assert.Empty(pane.FindAll("[data-testid='bulk-bar']"));
    }

    [Fact]
    public async Task The_bar_counts_what_is_picked()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var one = await host.WriteEntryAsync(First);
        await host.WriteEntryAsync(Second);
        await host.State.SelectAsync(null);

        var pane = host.Render();
        await PickAsync(pane, one);

        Assert.Equal("1 task selected", pane.Find("[data-testid='bulk-bar-count']").TextContent.Trim());

        // Partial, so the select-all box is mixed rather than either state —
        // .design/interaction-guidelines.md#focus-and-selection asks for it by
        // name.
        Assert.Equal("mixed", pane.Find("[data-testid='bulk-bar-select-all'] input").GetAttribute("aria-checked"));

        // Visibly mixed, and this is the assertion that would have caught it: the
        // aria value above was right all along while the box on screen looked
        // simply unchecked, because the only rule keyed off `checkbox--mixed`
        // styled a label the bar does not render. Asked through the stylesheet's
        // own selector, so the test fails if the paint loses its target again.
        Assert.Single(pane.FindAll("[data-testid='bulk-bar'] .checkbox--mixed .checkbox__input"));
    }

    [Fact]
    public async Task Select_all_takes_every_row_in_view_and_reads_as_checked()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var one = await host.WriteEntryAsync(First);
        await host.WriteEntryAsync(Second);
        await host.State.SelectAsync(null);

        var pane = host.Render();
        await PickAsync(pane, one);
        await pane.Find("[data-testid='bulk-bar-select-all'] input").ChangeAsync(new() { Value = true });

        Assert.Equal("2 tasks selected", pane.Find("[data-testid='bulk-bar-count']").TextContent.Trim());
        Assert.Equal("true", pane.Find("[data-testid='bulk-bar-select-all'] input").GetAttribute("aria-checked"));

        // Every row taken, so the box is checked and the mixed paint is gone.
        Assert.Empty(pane.FindAll("[data-testid='bulk-bar'] .checkbox--mixed .checkbox__input"));
    }

    [Fact]
    public async Task The_bar_offers_a_way_back_out()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, _, _) = await TwoPickedAsync(host);

        await pane.Find("[data-testid='bulk-bar-clear']").ClickAsync(new());

        Assert.Equal(0, host.State.SelectionCount);
        Assert.Empty(pane.FindAll("[data-testid='bulk-bar']"));
    }

    /// <summary>Picking a row is not opening it. The gutter box sits outside the
    /// row's own button, and the detail pane stays shut — which is what keeps
    /// "one entry, in detail" and "a set of entries, in bulk" two surfaces rather
    /// than one that fights itself.</summary>
    [Fact]
    public async Task Picking_a_row_does_not_open_the_detail_pane()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, _, _) = await TwoPickedAsync(host);

        Assert.Null(host.State.SelectedRow);
        Assert.Empty(pane.FindAll("[data-testid='entry-detail']"));
    }

    // --- Selection is a mode the reader asks for ---------------------------
    //
    // The gutter used to be there on every row of this pane, revealed by hover
    // and by focus. That made a checkbox reachable on any row a reader put the
    // pointer near, in a column whose job is scanning — so what is asked for
    // below is the whole of the mode: no boxes until the chip is pressed, boxes
    // on every row while it is, and both the boxes and the bar gone the moment it
    // is pressed again.

    [Fact]
    public async Task With_the_mode_off_no_row_offers_a_box()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var one = await host.WriteEntryAsync(First);
        await host.WriteEntryAsync(Second);
        await host.State.SelectAsync(null);

        var pane = host.Render();

        Assert.Empty(pane.FindAll("[data-testid='entry-list'] .task-item__gutter"));
        Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(one)}-select']"));
        Assert.Equal("false", pane.Find("[data-testid='bulk-select-toggle']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public async Task Pressing_select_puts_a_box_on_every_row()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var one = await host.WriteEntryAsync(First);
        var two = await host.WriteEntryAsync(Second);
        await host.State.SelectAsync(null);

        var pane = host.Render();
        await EnterSelectionModeAsync(pane);

        Assert.True(host.State.SelectionMode);
        Assert.Equal("true", pane.Find("[data-testid='bulk-select-toggle']").GetAttribute("aria-pressed"));

        // Every row, with nothing picked yet: a gutter that arrived one row at a
        // time under the pointer is the shape the mode replaces.
        Assert.Single(pane.FindAll($"[data-testid='{RowTestId(one)}-select']"));
        Assert.Single(pane.FindAll($"[data-testid='{RowTestId(two)}-select']"));

        // And still no bar, because nothing is picked. Asking to pick is not
        // picking.
        Assert.Empty(pane.FindAll("[data-testid='bulk-bar']"));
    }

    /// <summary>Leaving the mode empties the selection with it. A mode that came
    /// off and left five rows picked would leave the bar up over a column with no
    /// boxes in it — the reader would have no way to see, or to change, what the
    /// next act would land on.</summary>
    [Fact]
    public async Task Un_pressing_select_puts_the_boxes_and_the_bar_away()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, one, _) = await TwoPickedAsync(host);

        Assert.Equal(2, host.State.SelectionCount);

        await pane.Find("[data-testid='bulk-select-toggle']").ClickAsync(new());

        Assert.False(host.State.SelectionMode);
        Assert.Equal(0, host.State.SelectionCount);
        Assert.Empty(pane.FindAll("[data-testid='bulk-bar']"));
        Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(one)}-select']"));
    }

    /// <summary>The bar's own way out is the same way out. It said "clear
    /// selection" and emptied the set while leaving a column of boxes behind, so
    /// the bar vanished and the gutters stayed — one control, two thirds of a
    /// job.</summary>
    [Fact]
    public async Task The_bars_own_way_out_leaves_the_mode_as_well()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, one, _) = await TwoPickedAsync(host);

        await pane.Find("[data-testid='bulk-bar-clear']").ClickAsync(new());

        Assert.False(host.State.SelectionMode);
        Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(one)}-select']"));
        Assert.Equal("false", pane.Find("[data-testid='bulk-select-toggle']").GetAttribute("aria-pressed"));
    }

    /// <summary>
    /// Escape on the chip is the same press again, in one press.
    /// <para>
    /// No group step up here, unlike the bar: innermost-first is a rule about where
    /// the reader is standing, and from the chip the open group may be scrolled out
    /// of view behind the rows. A first press that closed something invisible would
    /// read as a key that had not worked.
    /// </para>
    /// <para>
    /// Only on the chip and on the bar — see the comment on
    /// <c>OnSelectionBarKeyDown</c> for why it is not on the list, where a row
    /// being renamed already owns the key.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Escape_on_the_chip_leaves_the_mode()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, _, _) = await TwoPickedAsync(host);

        // With a group open, which is the case that used to cost two presses.
        await OpenGroupAsync(pane, "classification");

        await pane.Find("[data-testid='bulk-select-toggle']")
            .KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.False(host.State.SelectionMode);
        Assert.Equal(0, host.State.SelectionCount);
    }

    /// <summary>Escape on the bar's own chrome puts an open group away first, and
    /// only leaves the mode once there is nothing else for it to close. Two
    /// decisions out of one keystroke is what the detail pane's own Escape
    /// refuses, and the bar owes the reader the same.</summary>
    [Fact]
    public async Task Escape_on_the_bar_closes_an_open_group_before_it_leaves()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, _, _) = await TwoPickedAsync(host);

        await OpenGroupAsync(pane, "classification");
        Assert.Single(pane.FindAll("[data-testid='bulk-priority']"));

        await pane.Find("[data-testid='bulk-bar']").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(pane.FindAll("[data-testid='bulk-priority']"));
        Assert.True(host.State.SelectionMode);

        await pane.Find("[data-testid='bulk-bar']").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.False(host.State.SelectionMode);
    }

    /// <summary>
    /// Escape inside one of a group's own controls belongs to that control, and
    /// stops there.
    /// <para>
    /// The test above cannot see this, and no test dispatching on the bar root ever
    /// could: it fires the handler directly, where the defect was entirely about
    /// what happened on the way up. A reader types a tag, the suggestion popup
    /// opens, they press Escape to dismiss the popup —
    /// <c>TagMultiSelect.OnKeyDownAsync</c> closes it and prevents the default but
    /// does not stop the key propagating, so the bar's own handler heard it too and
    /// took the whole group down: the picker, the chips already accumulated in it,
    /// the Add-tags button and the Remove-tag select. One keystroke, three
    /// decisions, two of them nobody asked for.
    /// </para>
    /// <para>
    /// So what is asserted is that the group is still open and the mode still on
    /// after an Escape dispatched on the picker's own input. Every one of a group's
    /// controls sits behind the same boundary; the tag field and a select stand for
    /// the rest.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Escape_inside_a_groups_control_leaves_the_group_alone()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, _, _) = await TwoPickedAsync(host);

        await OpenGroupAsync(pane, "tags");

        var field = pane.Find("[data-testid='bulk-tags-add'] input");
        await field.InputAsync(new() { Value = "q4" });
        await field.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        // A chip the reader has paid for, and the thing the old behaviour threw
        // away without being asked.
        Assert.Single(pane.FindAll("[data-testid='bulk-tags-add'] .tag-chip"));

        await pane.Find("[data-testid='bulk-tags-add'] input")
            .KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Single(pane.FindAll("[data-testid='bulk-tags-add']"));
        Assert.Single(pane.FindAll("[data-testid='bulk-tags-add'] .tag-chip"));
        Assert.True(host.State.SelectionMode);

        // The tag field could be dispatched on because it has a handler of its
        // own. A select has none — Escape there closes the list the browser
        // opened, which is the browser's business and not a Blazor event — so the
        // same fact is stated structurally for the rest of them: every control a
        // group opens is inside the boundary, so there is nothing above any of
        // them for a key to reach.
        await OpenGroupAsync(pane, "classification");

        var boundary = pane.Find("#bulk-controls-classification");

        foreach (var control in new[] { "bulk-status", "bulk-priority", "bulk-type" })
        {
            Assert.NotNull(boundary.QuerySelector($"[data-testid='{control}']"));
        }

        await OpenGroupAsync(pane, "scheduling");

        boundary = pane.Find("#bulk-controls-scheduling");

        foreach (var control in new[] { "bulk-due-input", "bulk-remind-input" })
        {
            Assert.NotNull(boundary.QuerySelector($"[data-testid='{control}']"));
        }
    }

    /// <summary>A group's trigger sits outside that boundary, so Escape there is
    /// the group's — which is the way out for a reader who opened a group and
    /// changed their mind without ever entering it.</summary>
    [Fact]
    public async Task Escape_on_a_groups_trigger_closes_that_group()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, _, _) = await TwoPickedAsync(host);

        await OpenGroupAsync(pane, "classification");

        await pane.Find("[data-testid='bulk-action-classification-set']")
            .KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(pane.FindAll("[data-testid='bulk-priority']"));
        Assert.True(host.State.SelectionMode);
    }

    /// <summary>The chip is not one of a set. Both filter strips beside it are
    /// radiogroups and this is not a filter — it changes what the rows offer
    /// rather than which rows are there — so it must not be announced as a third
    /// option in either of them.</summary>
    [Fact]
    public async Task The_chip_is_not_offered_as_a_filter()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync(First);
        await host.State.SelectAsync(null);

        var pane = host.Render();

        Assert.Empty(pane.FindAll("[role='radiogroup'] [data-testid='bulk-select-toggle']"));
        Assert.Null(pane.Find("[data-testid='bulk-select-toggle']").GetAttribute("role"));
    }

    // --- What the bar's acts look like -------------------------------------

    /// <summary>
    /// Five labelled groups rather than one line of fourteen controls.
    /// <para>
    /// <c>.design/interaction-guidelines.md#action-density-and-overflow</c> sets a
    /// visible budget of four on a toolbar and says the budgets are deliberately
    /// smaller than the act set: "a busy surface's resting state is a short row
    /// and a menu". Fourteen unlabelled controls abreast was the rule being
    /// broken, and nine labelled ones would have been the same mistake with names
    /// on it — so the band offers one act per group and a group opens its own
    /// controls.
    /// </para>
    /// <para>
    /// The groups are asserted here rather than a class, because they are the part
    /// that has to survive a layout change: they were balanced columns and are now
    /// a horizontal band, and either way a reader who cannot see the layout gets
    /// the grouping from <c>role</c> and <c>aria-labelledby</c> and nowhere else.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_bars_acts_arrive_grouped_rather_than_on_one_line()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var (pane, _, _) = await TwoPickedAsync(host);

        Assert.Single(pane.FindAll("[data-testid='bulk-bar-actions'] [data-testid='bulk-actions']"));

        foreach (var group in new[] { "myday", "filing", "classification", "scheduling", "tags" })
        {
            var element = pane.Find($"[data-testid='bulk-group-{group}']");

            // Named through aria-labelledby, which is the only grouping a reader
            // who cannot see the layout gets.
            Assert.Equal("group", element.GetAttribute("role"));
            Assert.False(string.IsNullOrWhiteSpace(element.GetAttribute("aria-labelledby")));
        }
    }

    /// <summary>
    /// The resting bar is five acts and no live control.
    /// <para>
    /// This is the height claim, asserted rather than eyeballed. The bar stood at
    /// roughly three hundred and eighty pixels when every field had a row of its
    /// own: nine rows at the action pane's reserved line height, in three stacked
    /// bands, with the right half of the bar empty. Five triggers and nothing else
    /// is one line.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_resting_bar_is_five_acts_and_no_live_control()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var (pane, _, _) = await TwoPickedAsync(host);

        Assert.Equal(5, pane.FindAll("[data-testid='bulk-actions'] .task-action").Count);

        // Nothing to type into or pick from until a group is asked for. The
        // select-all box and the way out of the selection belong to SelectionBar
        // rather than to the acts, so they sit outside what is counted here.
        Assert.Empty(pane.FindAll("[data-testid='bulk-actions'] select"));
        Assert.Empty(pane.FindAll("[data-testid='bulk-actions'] input"));
    }

    /// <summary>
    /// A group's trigger says it is a disclosure, and names what it opened.
    /// <para>
    /// <c>aria-expanded</c> and not <c>aria-pressed</c>: pressed says "this thing
    /// is on", and these are not on, they are open. The distinction is the reason
    /// <c>TaskAction</c> grew <c>Expanded</c> beside <c>Togglable</c> — the detail
    /// pane's one togglable row is a real in-or-out toggle, and its pickers pass
    /// <c>Set</c> without it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_groups_trigger_announces_itself_as_a_disclosure()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, _, _) = await TwoPickedAsync(host);

        var trigger = pane.Find("[data-testid='bulk-action-classification-set']");

        Assert.Equal("false", trigger.GetAttribute("aria-expanded"));
        Assert.False(trigger.HasAttribute("aria-pressed"));

        var controls = trigger.GetAttribute("aria-controls");
        Assert.False(string.IsNullOrWhiteSpace(controls));

        // Nothing to point at until it is open, which is honest: aria-controls on
        // a collapsed disclosure names an element that is not there yet.
        Assert.Empty(pane.FindAll($"#{controls}"));

        await OpenGroupAsync(pane, "classification");

        trigger = pane.Find("[data-testid='bulk-action-classification-set']");
        Assert.Equal("true", trigger.GetAttribute("aria-expanded"));
        Assert.Single(pane.FindAll($"#{controls}"));

        // My Day opens nothing, so it claims no disclosure at all.
        var myDay = pane.Find("[data-testid='bulk-action-myday-set']");
        Assert.False(myDay.HasAttribute("aria-expanded"));
    }

    /// <summary>A group's controls arrive when the group is asked for and leave
    /// when another one is. One open group is what keeps the band two lines rather
    /// than however many groups a reader has poked.</summary>
    [Fact]
    public async Task A_group_shows_no_control_until_it_is_pressed()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, _, _) = await TwoPickedAsync(host);

        Assert.Empty(pane.FindAll("[data-testid='bulk-status']"));
        Assert.Empty(pane.FindAll("[data-testid='bulk-priority']"));
        Assert.Empty(pane.FindAll("[data-testid='bulk-due-input']"));

        await OpenGroupAsync(pane, "classification");

        // All three of the group's controls, and not one of them behind a second
        // press: two presses reach any value that way, where a trigger per field
        // inside a trigger per group would have cost three.
        Assert.Single(pane.FindAll("[data-testid='bulk-status']"));
        Assert.Single(pane.FindAll("[data-testid='bulk-priority']"));
        Assert.Single(pane.FindAll("[data-testid='bulk-type']"));

        await OpenGroupAsync(pane, "scheduling");

        Assert.Empty(pane.FindAll("[data-testid='bulk-status']"));
        Assert.Single(pane.FindAll("[data-testid='bulk-due-input']"));
        Assert.Single(pane.FindAll("[data-testid='bulk-remind-input']"));

        // And pressing the open group again puts it back.
        await OpenGroupAsync(pane, "scheduling");

        Assert.Empty(pane.FindAll("[data-testid='bulk-due-input']"));
    }

    /// <summary>
    /// Every control in the bar wears the library's own classes and none of the
    /// detail panel's.
    /// <para>
    /// This is the test that would have caught the defect the band shipped with,
    /// and it is worth saying exactly what that was: the bar reached the shared
    /// components but dressed them in <c>entry-doc__schedule-select</c> and
    /// <c>entry-doc__tag-picker</c>, two of the detail panel's private classes.
    /// The first sets <c>font-family: var(--font-family-mono)</c> — Fira Code,
    /// then Courier New — and supplies no border or background, so the three
    /// selects rendered as Courier text with no affordance at all, which reads
    /// exactly like a control the product's stylesheet never reached. The second
    /// strips the tag picker's border and fill until the pointer is on it. Both
    /// are right where they were written, on a picker indented under a row in a
    /// narrow panel; neither is a skin the library should have needed a host to
    /// supply.
    /// </para>
    /// <para>
    /// So what is asserted is the composition rule rather than a paint: the
    /// component's own class is present, and no element in the bar carries a class
    /// belonging to another screen. A computed font or a border colour is not
    /// something bUnit can see — there is no layout and no cascade behind it --
    /// so the appearance itself stays a review-and-QA concern. What is mechanical
    /// is whose classes are on the element, and that is where this went wrong.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_bars_controls_wear_the_librarys_classes_and_not_another_screens()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var (pane, _, _) = await TwoPickedAsync(host);

        foreach (var group in new[] { "repo", "classification", "scheduling", "tags" })
        {
            await OpenGroupAsync(pane, group);

            var borrowed = pane
                .FindAll("[data-testid='bulk-actions'] *")
                .SelectMany(element => element.ClassList)
                .Where(name => name.StartsWith("entry-doc__", StringComparison.Ordinal)
                    || name.StartsWith("entry-row__", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            Assert.Empty(borrowed);
        }

        // And each control is the library's, named by the class the stylesheet
        // dresses it through.
        await OpenGroupAsync(pane, "classification");
        Assert.Contains("badge--unset", pane.Find("[data-testid='bulk-status']").ClassList);
        Assert.Contains("metadata-editor", pane.Find("[data-testid='bulk-priority']").ClassList);
        Assert.Contains("metadata-editor", pane.Find("[data-testid='bulk-type']").ClassList);

        await OpenGroupAsync(pane, "scheduling");
        Assert.Contains("field__input--compact", pane.Find("[data-testid='bulk-due-input']").ClassList);
        Assert.Contains("field__input--compact", pane.Find("[data-testid='bulk-remind-input']").ClassList);

        await OpenGroupAsync(pane, "tags");
        Assert.Single(pane.FindAll("[data-testid='bulk-tags-add'] .tag-select__control"));
    }

    /// <summary>Status is the badge, not a plain select. It is the one control the
    /// two panels disagreed on — the detail pane has shown a <c>StatusSelector</c>
    /// on the heading line since it adopted the shared panel — and a status drawn
    /// as a bare list in one place and as a coloured pill in the other tells the
    /// reader they are two different kinds of thing.</summary>
    [Fact]
    public async Task The_status_the_bar_offers_is_the_same_badge_the_detail_pane_shows()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, _, _) = await TwoPickedAsync(host);

        await OpenGroupAsync(pane, "classification");

        var badge = pane.Find("[data-testid='bulk-status']");

        Assert.Contains("badge", badge.ClassList);
        Assert.Contains("badge--status", badge.ClassList);
        Assert.Single(badge.QuerySelectorAll(".status-editor__select"));

        // No status to show, because a selection has as many as it has rows — so
        // the badge wears no value's colour and the list opens on a prompt rather
        // than on whichever status happens to sort first.
        Assert.DoesNotContain("badge--status-draft", badge.ClassList);
        Assert.Equal("Status", badge.QuerySelector("option")!.TextContent);
    }

    // --- Repository (AC2, AC3) ---------------------------------------------

    /// <summary>Every target is replaced rather than one of them edited. The
    /// single-row picker edits the target it is showing and leaves the rest, which
    /// is right for a control speaking about one repository — a bulk control says
    /// "these all belong to this project now", and leaving a second target behind
    /// would make that false on exactly the rows that had one.</summary>
    [Fact]
    public async Task Setting_the_repository_replaces_every_target_on_every_row()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs");
        var one = await host.WriteEntryAsync(
            "# Provision the box\n`task` `*high` `!ready` `@platform` `repo:backlog` `repo:docs`\n");
        var two = await host.WriteEntryAsync("# Write the runbook\n`idea` `*low` `@docs`\n");
        await host.State.SelectAsync(null);

        var pane = host.Render();
        await PickAsync(pane, one);
        await PickAsync(pane, two);

        var repo = await OpenSelectAsync(pane, "repo");
        await repo.ChangeAsync(new() { Value = "docs" });

        Assert.Equal(["docs"], one.PreviewRepoIds);
        Assert.Equal(["docs"], two.PreviewRepoIds);

        // AC3: the areas, the priorities and the statuses are the rows' own and
        // stay that way. The area is the one worth naming — it is spelled like a
        // repository on the second row and is still not one.
        Assert.Equal("platform", one.PreviewArea);
        Assert.Equal("docs", two.PreviewArea);
        Assert.Equal(Priority.High, one.PreviewPriority);
        Assert.Equal(Priority.Low, two.PreviewPriority);
    }

    // --- Status (AC2, AC3) -------------------------------------------------

    [Fact]
    public async Task Setting_the_status_lands_on_every_row_and_leaves_the_rest_alone()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, one, two) = await TwoPickedAsync(host);

        var status = await OpenSelectAsync(pane, "status");
        await status.ChangeAsync(new() { Value = nameof(EntryStatus.InProgress) });

        Assert.Contains("`!in-progress`", one.RawText, StringComparison.Ordinal);
        Assert.Contains("`!in-progress`", two.RawText, StringComparison.Ordinal);

        // AC3, and the whole reason each row is rewritten from its own text: the
        // two rows carry different everything, and a shared metadata line would
        // have left them carrying the same.
        Assert.Contains("`due:2026-08-21`", one.RawText, StringComparison.Ordinal);
        Assert.Contains("`#infra`", one.RawText, StringComparison.Ordinal);
        Assert.Contains("`@platform`", one.RawText, StringComparison.Ordinal);
        Assert.Equal(EntryType.Task, one.PreviewType);

        Assert.Contains("`remind:2026-09-01T09:00`", two.RawText, StringComparison.Ordinal);
        Assert.Contains("`#writing`", two.RawText, StringComparison.Ordinal);
        Assert.Contains("`@docs`", two.RawText, StringComparison.Ordinal);
        Assert.Equal(EntryType.Idea, two.PreviewType);

        // And the prose under each title is still each row's own.
        Assert.Contains("Rack it first.", one.RawText, StringComparison.Ordinal);
        Assert.Contains("Then say how.", two.RawText, StringComparison.Ordinal);
    }

    /// <summary>Status is set-only. There is no "no status" for an entry to be in
    /// — the field is not nullable and `!draft` is what a silent entry means — so
    /// the bar offers no ✕ beside it, exactly as the detail pane offers none
    /// beside priority.</summary>
    [Fact]
    public async Task Status_priority_and_type_are_offered_without_a_clear()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, _, _) = await TwoPickedAsync(host);

        await OpenGroupAsync(pane, "classification");

        Assert.Empty(pane.FindAll("[data-testid='bulk-status-clear']"));
        Assert.Empty(pane.FindAll("[data-testid='bulk-priority-clear']"));
        Assert.Empty(pane.FindAll("[data-testid='bulk-type-clear']"));
    }

    // --- Priority and type (AC2, AC3) --------------------------------------

    [Fact]
    public async Task Setting_the_priority_lands_on_every_row()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, one, two) = await TwoPickedAsync(host);

        var priority = await OpenSelectAsync(pane, "priority");
        await priority.ChangeAsync(new() { Value = nameof(Priority.Critical) });

        Assert.Contains("`*critical`", one.RawText, StringComparison.Ordinal);
        Assert.Contains("`*critical`", two.RawText, StringComparison.Ordinal);

        Assert.Equal(EntryStatus.Ready, one.PreviewStatus);
        Assert.Equal(EntryStatus.Draft, two.PreviewStatus);
    }

    [Fact]
    public async Task Setting_the_type_lands_on_every_row()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, one, two) = await TwoPickedAsync(host);

        // Prompt because the two rows start as a task and an idea, so neither is
        // already what the bulk change sets — an assertion that passed on a row
        // the edit never had to touch would prove nothing about that row.
        var type = await OpenSelectAsync(pane, "type");
        await type.ChangeAsync(new() { Value = nameof(EntryType.Prompt) });

        Assert.Equal(EntryType.Prompt, one.PreviewType);
        Assert.Equal(EntryType.Prompt, two.PreviewType);

        Assert.Equal(Priority.High, one.PreviewPriority);
        Assert.Equal(Priority.Low, two.PreviewPriority);
    }

    // --- My Day (AC2, AC4) -------------------------------------------------

    [Fact]
    public async Task Adding_to_my_day_stamps_today_on_every_row()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, one, two) = await TwoPickedAsync(host);

        await pane.Find("[data-testid='bulk-action-myday-set']").ClickAsync(new());

        Assert.Contains($"`myday:{TodayToken}`", one.RawText, StringComparison.Ordinal);
        Assert.Contains($"`myday:{TodayToken}`", two.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Taking_my_day_off_removes_only_that_token()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, one, two) = await TwoPickedAsync(host);

        await pane.Find("[data-testid='bulk-action-myday-set']").ClickAsync(new());
        await pane.Find("[data-testid='bulk-action-myday-clear']").ClickAsync(new());

        Assert.DoesNotContain("myday:", one.RawText, StringComparison.Ordinal);
        Assert.DoesNotContain("myday:", two.RawText, StringComparison.Ordinal);

        // AC4: only that token. Everything else each row said is still said.
        Assert.Contains("`due:2026-08-21`", one.RawText, StringComparison.Ordinal);
        Assert.Contains("`remind:2026-09-01T09:00`", two.RawText, StringComparison.Ordinal);
    }

    // --- Due date (AC2, AC4) -----------------------------------------------

    [Fact]
    public async Task Setting_a_due_date_writes_the_token_on_every_row()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, one, two) = await TwoPickedAsync(host);

        await OpenGroupAsync(pane, "scheduling");
        await pane.Find("[data-testid='bulk-due-input']").ChangeAsync(new() { Value = "2026-12-24" });

        Assert.Equal(new DateOnly(2026, 12, 24), one.PreviewDueOn);
        Assert.Equal(new DateOnly(2026, 12, 24), two.PreviewDueOn);

        Assert.Contains("`#infra`", one.RawText, StringComparison.Ordinal);
        Assert.Contains("`remind:2026-09-01T09:00`", two.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clearing_the_due_date_removes_only_that_token()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, one, two) = await TwoPickedAsync(host);

        await OpenGroupAsync(pane, "scheduling");
        await pane.Find("[data-testid='bulk-due-clear']").ClickAsync(new());

        Assert.Null(one.PreviewDueOn);
        Assert.Null(two.PreviewDueOn);

        Assert.Contains("`#infra`", one.RawText, StringComparison.Ordinal);
        Assert.Contains("`@platform`", one.RawText, StringComparison.Ordinal);
        Assert.Contains("`remind:2026-09-01T09:00`", two.RawText, StringComparison.Ordinal);
    }

    // --- Reminder (AC2, AC4) -----------------------------------------------

    [Fact]
    public async Task Setting_a_reminder_writes_an_unzoned_wall_clock_time_on_every_row()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, one, two) = await TwoPickedAsync(host);

        await OpenGroupAsync(pane, "scheduling");
        await pane.Find("[data-testid='bulk-remind-input']").ChangeAsync(new() { Value = "2026-08-21T09:00" });

        Assert.Contains("`remind:2026-08-21T09:00`", one.RawText, StringComparison.Ordinal);
        Assert.Contains("`remind:2026-08-21T09:00`", two.RawText, StringComparison.Ordinal);
        Assert.Equal(DateTimeKind.Unspecified, one.PreviewRemindAt!.Value.Kind);
    }

    [Fact]
    public async Task Clearing_the_reminder_removes_only_that_token()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, one, two) = await TwoPickedAsync(host);

        await OpenGroupAsync(pane, "scheduling");
        await pane.Find("[data-testid='bulk-remind-clear']").ClickAsync(new());

        Assert.Null(one.PreviewRemindAt);
        Assert.Null(two.PreviewRemindAt);

        Assert.Contains("`due:2026-08-21`", one.RawText, StringComparison.Ordinal);
        Assert.Contains("`#writing`", two.RawText, StringComparison.Ordinal);
        Assert.Contains("`@docs`", two.RawText, StringComparison.Ordinal);
    }

    // --- Tags (AC2, AC3) ---------------------------------------------------

    /// <summary>Tags are added rather than replaced, which is the one field where
    /// that is the whole point: a bulk edit that wrote the picked set would take
    /// every other tag off every row, and tags are how this backlog is
    /// cross-cut.</summary>
    [Fact]
    public async Task Adding_tags_leaves_each_rows_own_tags_where_they_were()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, one, two) = await TwoPickedAsync(host);

        await AddPickedTagAsync(pane, "q4");
        await pane.Find("[data-testid='bulk-tags-apply']").ClickAsync(new());

        Assert.Contains("`#q4`", one.RawText, StringComparison.Ordinal);
        Assert.Contains("`#q4`", two.RawText, StringComparison.Ordinal);

        Assert.Contains("`#infra`", one.RawText, StringComparison.Ordinal);
        Assert.Contains("`#writing`", two.RawText, StringComparison.Ordinal);
    }

    /// <summary>Removing takes one named tag off every row and nothing else. The
    /// options are scoped to what the selection actually wears, because an option
    /// that could not remove anything is an option that does nothing.</summary>
    [Fact]
    public async Task Removing_a_tag_takes_it_off_every_row_that_had_it()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, one, two) = await TwoPickedAsync(host);

        await AddPickedTagAsync(pane, "q4");
        await pane.Find("[data-testid='bulk-tags-apply']").ClickAsync(new());

        var removal = await OpenSelectAsync(pane, "tag-remove");
        await removal.ChangeAsync(new() { Value = "q4" });

        Assert.DoesNotContain("`#q4`", one.RawText, StringComparison.Ordinal);
        Assert.DoesNotContain("`#q4`", two.RawText, StringComparison.Ordinal);

        Assert.Contains("`#infra`", one.RawText, StringComparison.Ordinal);
        Assert.Contains("`#writing`", two.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_tags_offered_for_removal_are_the_ones_the_selection_wears()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, _, _) = await TwoPickedAsync(host);

        var offered = (await OpenSelectAsync(pane, "tag-remove"))
            .QuerySelectorAll("option")
            .Select(option => option.GetAttribute("value"))
            .Where(value => !string.IsNullOrEmpty(value))
            .ToList();

        Assert.Equal(["infra", "writing"], offered.OrderBy(tag => tag, StringComparer.Ordinal));
    }

    /// <summary>Types a tag into the bar's picker and commits it. Picking is not
    /// applying: the tags accumulate as chips first, so a reader adds two at once
    /// rather than paying for a save per tag.</summary>
    private static async Task AddPickedTagAsync(IRenderedComponent<TasksPane> pane, string tag)
    {
        await OpenGroupAsync(pane, "tags");

        var field = pane.Find("[data-testid='bulk-tags-add'] input");
        await field.InputAsync(new() { Value = tag });
        await field.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
    }

    // --- What the bar reports (AC5) ----------------------------------------

    [Fact]
    public async Task The_bar_says_how_many_rows_it_wrote()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, _, _) = await TwoPickedAsync(host);

        var status = await OpenSelectAsync(pane, "status");
        await status.ChangeAsync(new() { Value = nameof(EntryStatus.Archived) });

        Assert.Contains("2 tasks updated", Result(pane), StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_row_written_reads_in_the_singular()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var one = await host.WriteEntryAsync(First);
        await host.State.SelectAsync(null);

        var pane = host.Render();
        await PickAsync(pane, one);

        var priority = await OpenSelectAsync(pane, "priority");
        await priority.ChangeAsync(new() { Value = nameof(Priority.Low) });

        Assert.Contains("1 task updated", Result(pane), StringComparison.Ordinal);
    }

    /// <summary>A row already at the target value is skipped rather than saved
    /// again, and the sentence says so — the same skip-if-unchanged the reorder
    /// and repository-reconcile handlers do. "8 tasks updated" over eight rows
    /// where nothing changed would be the pane claiming work it did not do.</summary>
    [Fact]
    public async Task Rows_already_at_the_target_are_counted_rather_than_written()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, one, two) = await TwoPickedAsync(host);

        var status = await OpenSelectAsync(pane, "status");
        await status.ChangeAsync(new() { Value = nameof(EntryStatus.Ready) });

        Assert.Contains("1 task updated", Result(pane), StringComparison.Ordinal);
        Assert.Contains("already up to date", Result(pane), StringComparison.Ordinal);

        Assert.Equal(EntryStatus.Ready, one.PreviewStatus);
        Assert.Equal(EntryStatus.Ready, two.PreviewStatus);
    }

    /// <summary>A failure is a value, never a throw
    /// (<c>.arc42/adr/guidelines/0004-result-objects-for-expected-failures.md</c>).
    /// A row whose text no longer parses to a title is the failure the module
    /// actually reports, and the outcome carries it back per row instead of the
    /// pane learning about it from an exception halfway through a batch.</summary>
    [Fact]
    public async Task A_row_that_cannot_be_saved_comes_back_as_a_value()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var one = await host.WriteEntryAsync(First);
        var two = await host.WriteEntryAsync(Second);
        await host.State.SelectAsync(null);

        // Pointed at an entry that is not in the store, which is the refusal the
        // module actually has for a row being updated: `entry.not_found`, the
        // shape a row deleted from under the list arrives in. It comes back as an
        // Error rather than as a throw, which is the whole point.
        two.Id = Guid.NewGuid();

        host.State.SetSelection([one.TaskId, two.TaskId]);

        var outcome = await host.State.BulkChangePriorityAsync(Priority.Critical);

        Assert.Equal(1, outcome.Updated);
        Assert.Single(outcome.Failures);
        Assert.Equal(two.TaskId, outcome.Failures[0].Id);
        Assert.NotEqual(string.Empty, outcome.Failures[0].Error.Code);
    }

    [Fact]
    public async Task A_partial_failure_reads_differently_from_a_clean_run()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var one = await host.WriteEntryAsync(First);
        var two = await host.WriteEntryAsync(Second);
        await host.State.SelectAsync(null);
        two.Id = Guid.NewGuid();

        var pane = host.Render();

        // Through the chip and the boxes, like every other test here. Handing the
        // state a selection directly used to leave the mode off, which put the bar
        // on screen over a column with nothing ticked in it — a state no gesture
        // can produce, and one the state now refuses: a selection arriving turns
        // the mode on.
        await PickAsync(pane, one);
        await PickAsync(pane, two);

        var priority = await OpenSelectAsync(pane, "priority");
        await priority.ChangeAsync(new() { Value = nameof(Priority.Critical) });

        Assert.Contains("1 task updated", Result(pane), StringComparison.Ordinal);
        Assert.Contains("could not be saved", Result(pane), StringComparison.Ordinal);
    }

    // --- Selection follows the list (AC6) ----------------------------------

    /// <summary>A row the filters have taken out of view leaves the selection with
    /// it. Filtered out is the same fact as gone as far as this half of the split
    /// is concerned — which is already why it closes the detail pane — and a
    /// selection holding rows nobody can see would apply a change to work the
    /// reader had lost track of.</summary>
    [Fact]
    public async Task A_row_that_leaves_the_filtered_set_leaves_the_selection()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var one = await host.WriteEntryAsync(First);
        var two = await host.WriteEntryAsync(Second);
        await host.State.SelectAsync(null);

        host.State.SetSelection([one.TaskId, two.TaskId]);
        Assert.Equal(2, host.State.SelectionCount);

        // Only the first row is ready, so the second drops out of view.
        host.State.SetStatusFilter("ready");

        Assert.Equal([one.TaskId], host.State.SelectedIds);
    }

    [Fact]
    public async Task A_selection_emptied_by_a_filter_takes_the_bar_with_it()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, _, _) = await TwoPickedAsync(host);

        // A My Day scope for a day nothing is stamped for. The area filter would
        // not do it: an area no entry is filed under stops existing, and
        // ApplyFilter drops the filter rather than the rows.
        host.State.SetMyDayFilter(new DateOnly(2020, 1, 1));
        pane.Render();

        Assert.Equal(0, host.State.SelectionCount);
        Assert.Empty(pane.FindAll("[data-testid='bulk-bar']"));
    }

    /// <summary>Nothing selected, nothing written. Every bulk method is safe to
    /// call over an empty selection, because the alternative is a pane that has to
    /// remember to check before every one of nine calls.</summary>
    [Fact]
    public async Task A_bulk_change_over_nothing_writes_nothing()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var one = await host.WriteEntryAsync(First);
        var before = one.RawText;

        var outcome = await host.State.BulkChangeStatusAsync(EntryStatus.Done);

        Assert.Equal(0, outcome.Updated);
        Assert.Empty(outcome.Failures);
        Assert.Equal(before, one.RawText);
    }
}
