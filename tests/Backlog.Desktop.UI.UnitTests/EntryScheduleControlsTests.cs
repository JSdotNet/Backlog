using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The five scheduling and dependency controls on an entry: due date, reminder,
/// repeat, My Day, and what it waits on.
/// <para>
/// Every one of them writes by rewriting the metadata line, which is why almost
/// every assertion here is about <c>RawText</c>. That is not indirection: the text
/// <em>is</em> the entry, so a control that changed a field without changing the
/// text would have changed nothing that survives a save. Pressing the control and
/// typing the token are the same edit arriving by two routes, and these tests are
/// what says so.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class EntryScheduleControlsTests
{
    /// <summary>Writing an entry leaves it open in the detail pane, which is where
    /// all five controls live. Body text is no longer load-bearing — see
    /// <see cref="A_title_only_entry_reaches_the_controls_too"/> — but it keeps this
    /// entry recognisable as the one the other tests are about.</summary>
    private const string ExpandedEntry =
        "# Deploy SpecManager\n" +
        "`task` `*high` `!ready` `@backlog`\n\n" +
        "Ship it before the demo.\n";

    private static string TodayToken =>
        DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public async Task The_open_entry_offers_all_five()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync(ExpandedEntry);

        var pane = host.Render();

        foreach (var testId in new[]
                 {
                     "entry-action-myday",
                     "entry-action-due",
                     "entry-action-remind",
                     "entry-action-repeat",
                     "entry-action-depends"
                 })
        {
            Assert.Single(pane.FindAll($"[data-testid='{testId}']"));
        }
    }

    /// <summary>
    /// An entry with nothing but a title and a metadata line reaches the controls.
    /// <para>
    /// This is the gap the move closed. The five rows used to live under an expanded
    /// entry in the list, and a one-line entry — the ordinary shape of a quick
    /// capture — had no expanded state to put them in, so the only way to give one a
    /// due date was to type the token. Which row is open is now a fact about the pane
    /// beside the list rather than about how much has been written in the row.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_title_only_entry_reaches_the_controls_too()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Ask about the trial length\n`task` `*medium` `!draft`\n");

        Assert.False(row.HasExpandableContent);

        var pane = host.Render();

        Assert.Single(pane.FindAll("[data-testid='entry-action-myday']"));
        Assert.Single(pane.FindAll("[data-testid='entry-action-due']"));
    }

    /// <summary>Nothing open, nothing offered. The controls belong to one entry, so
    /// a pane with no entry in it has none of them — and the list beside it stays a
    /// list rather than becoming five rows per entry.</summary>
    [Fact]
    public async Task A_closed_pane_offers_none_of_them()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync(ExpandedEntry);
        await host.State.SelectAsync(null);

        var pane = host.Render();

        Assert.Empty(pane.FindAll("[data-testid='entry-action-myday']"));
        Assert.Empty(pane.FindAll("[data-testid='entry-action-due']"));

        // Not a placeholder either: the pane is gone rather than empty, which is
        // asserted in full by TasksDetailPaneTests.
        Assert.Empty(pane.FindAll("[data-testid='entry-detail']"));
    }

    // --- My Day ------------------------------------------------------------

    [Fact]
    public async Task My_day_stamps_today_and_pressing_again_clears_it()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(ExpandedEntry);

        var pane = host.Render();
        await pane.Find("[data-testid='entry-action-myday-set']").ClickAsync(new());

        Assert.Equal(DateOnly.FromDateTime(DateTime.Now), row.PreviewInMyDayOn);
        Assert.Contains($"`myday:{TodayToken}`", row.RawText, StringComparison.Ordinal);

        // Togglable, so no ✕ — pressing it again is how it comes off.
        Assert.Empty(pane.FindAll("[data-testid='entry-action-myday-clear']"));

        await pane.Find("[data-testid='entry-action-myday-set']").ClickAsync(new());

        Assert.Null(row.PreviewInMyDayOn);
        Assert.DoesNotContain("myday:", row.RawText, StringComparison.Ordinal);
    }

    /// <summary>A stamp from another day is not "in My Day": the decision expires
    /// by arithmetic against today's date rather than by anything sweeping the
    /// list. The control reads as unset, and pressing it writes today.</summary>
    [Fact]
    public async Task A_stamp_from_another_day_reads_as_not_in_my_day()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(
            "# Deploy SpecManager\n`task` `myday:2020-01-01`\n\nShip it.\n");

        var pane = host.Render();
        var myDay = pane.Find("[data-testid='entry-action-myday-set']");

        Assert.Equal("false", myDay.GetAttribute("aria-pressed"));
        Assert.Contains("Add to My Day", myDay.TextContent, StringComparison.Ordinal);

        await myDay.ClickAsync(new());

        Assert.Equal(DateOnly.FromDateTime(DateTime.Now), row.PreviewInMyDayOn);
    }

    // --- Due date ----------------------------------------------------------

    [Fact]
    public async Task The_due_row_opens_a_picker_that_writes_the_token()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(ExpandedEntry);

        var pane = host.Render();

        // Unset, so the row says what it would do and there is no picker yet.
        Assert.Empty(pane.FindAll("[data-testid='entry-due-input']"));

        await pane.Find("[data-testid='entry-action-due-set']").ClickAsync(new());
        await pane.Find("[data-testid='entry-due-input']").ChangeAsync(new() { Value = "2026-08-21" });

        Assert.Equal(new DateOnly(2026, 8, 21), row.PreviewDueOn);
        Assert.Contains("`due:2026-08-21`", row.RawText, StringComparison.Ordinal);

        // Picking closes it: the question the picker was asking is answered.
        Assert.Empty(pane.FindAll("[data-testid='entry-due-input']"));
    }

    [Fact]
    public async Task A_set_due_row_shows_its_value_and_offers_a_clear()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(
            "# Deploy SpecManager\n`task` `due:2026-08-21`\n\nShip it.\n");

        var pane = host.Render();

        // The host formats, never the component: the value on the row is the
        // current culture's short date, while the token stays invariant.
        Assert.Contains(
            new DateOnly(2026, 8, 21).ToString("d", CultureInfo.CurrentCulture),
            pane.Find("[data-testid='entry-action-due']").TextContent,
            StringComparison.Ordinal);

        await pane.Find("[data-testid='entry-action-due-clear']").ClickAsync(new());

        Assert.Null(row.PreviewDueOn);
        Assert.DoesNotContain("due:", row.RawText, StringComparison.Ordinal);
    }

    // --- Reminder ----------------------------------------------------------

    [Fact]
    public async Task The_reminder_picker_writes_an_unzoned_wall_clock_time()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(ExpandedEntry);

        var pane = host.Render();
        await pane.Find("[data-testid='entry-action-remind-set']").ClickAsync(new());
        await pane.Find("[data-testid='entry-remind-input']").ChangeAsync(new() { Value = "2026-08-21T09:00" });

        Assert.Contains("`remind:2026-08-21T09:00`", row.RawText, StringComparison.Ordinal);

        // 09:00 wherever the reader is, not the instant 09:00 once meant
        // somewhere else — which is exactly what an Unspecified kind says.
        var remindAt = Assert.IsType<DateTime>(row.PreviewRemindAt);
        Assert.Equal(DateTimeKind.Unspecified, remindAt.Kind);
        Assert.Equal(new TimeOnly(9, 0), TimeOnly.FromDateTime(remindAt));
    }

    [Fact]
    public async Task Clearing_a_reminder_removes_the_token()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(
            "# Deploy SpecManager\n`task` `remind:2026-08-21T09:00`\n\nShip it.\n");

        await host.Render().Find("[data-testid='entry-action-remind-clear']").ClickAsync(new());

        Assert.Null(row.PreviewRemindAt);
        Assert.DoesNotContain("remind:", row.RawText, StringComparison.Ordinal);
    }

    // --- Repeat ------------------------------------------------------------

    [Theory]
    [InlineData("weekly", 1, RecurrenceUnit.Week)]
    [InlineData("daily", 1, RecurrenceUnit.Day)]
    [InlineData("monthly", 1, RecurrenceUnit.Month)]
    [InlineData("yearly", 1, RecurrenceUnit.Year)]
    public async Task The_repeat_select_writes_the_shape_it_offered(string token, int interval, RecurrenceUnit unit)
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(ExpandedEntry);

        var pane = host.Render();
        await pane.Find("[data-testid='entry-action-repeat-set']").ClickAsync(new());
        await pane.Find("[data-testid='entry-repeat-select'] select").ChangeAsync(new() { Value = token });

        Assert.Equal(new Recurrence(interval, unit), row.PreviewRecurrence);
        Assert.Contains($"`repeat:{token}`", row.RawText, StringComparison.Ordinal);
    }

    /// <summary>"Every weekday" is the one weekday-restricted shape this grammar
    /// can spell, and it reads back as Monday to Friday rather than as a bare
    /// weekly repeat.</summary>
    [Fact]
    public async Task Every_weekday_is_expressible()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(ExpandedEntry);

        var pane = host.Render();
        await pane.Find("[data-testid='entry-action-repeat-set']").ClickAsync(new());
        await pane.Find("[data-testid='entry-repeat-select'] select").ChangeAsync(new() { Value = "weekdays" });

        Assert.Contains("`repeat:weekdays`", row.RawText, StringComparison.Ordinal);
        Assert.Equal(
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
            row.PreviewRecurrence!.Weekdays!.OrderBy(day => day));

        Assert.Contains("Every weekday", pane.Find("[data-testid='entry-action-repeat']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>Picking the empty option is how a repeat stops. It has to be
    /// reachable from the select as well as from the ✕, because a reader who opened
    /// the picker to change the shape may well decide the answer is "never".</summary>
    [Fact]
    public async Task Choosing_never_clears_the_repeat()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(
            "# Weekly review\n`task` `repeat:weekly`\n\nRead the week.\n");

        var pane = host.Render();
        await pane.Find("[data-testid='entry-action-repeat-set']").ClickAsync(new());
        await pane.Find("[data-testid='entry-repeat-select'] select").ChangeAsync(new() { Value = string.Empty });

        Assert.Null(row.PreviewRecurrence);
        Assert.DoesNotContain("repeat:", row.RawText, StringComparison.Ordinal);
    }

    // --- Dependencies ------------------------------------------------------

    [Fact]
    public async Task The_dependency_picker_offers_the_other_entries_and_writes_their_ids()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var first = await host.WriteEntryAsync("# Provision the box\n`task`\n\nGet a machine.\n");
        var second = await host.WriteEntryAsync(ExpandedEntry);

        var pane = host.Render();

        // One control, not two: the pane is open on one entry, and the one it is
        // open on is the entry that was just written.
        await pane.Find("[data-testid='entry-action-depends-set']").ClickAsync(new());

        // TagMultiSelect opens its listbox when the input takes focus, here as
        // everywhere else it is used — the row above only puts the picker on
        // screen.
        await pane.Find("[data-testid='entry-depends-select'] input").FocusAsync(new());

        // Its own row is not on offer: an entry that waits on itself is a cycle of
        // one, and there is no reason to make that a click away.
        var options = pane.FindAll("[data-testid='entry-depends-select'] [role='option']");
        Assert.Equal(["Provision the box"], options.Select(option => option.TextContent));

        await options[0].ClickAsync(new());

        Assert.Equal([first.Id!.Value.ToString()], second.PreviewDependsOn);
        Assert.Contains($"`after:{first.Id!.Value}`", second.RawText, StringComparison.Ordinal);
    }

    /// <summary>A finished entry is not offered as something to wait on. Waiting on
    /// work that is already done is not a dependency anyone can have — the picker
    /// would be offering a predecessor that can never block.</summary>
    [Fact]
    public async Task The_dependency_picker_excludes_completed_entries()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var done = await host.WriteEntryAsync("# Provision the box\n`task` `!done`\n\nAlready shipped.\n");
        var open = await host.WriteEntryAsync(ExpandedEntry);

        var pane = host.Render();

        await pane.Find("[data-testid='entry-action-depends-set']").ClickAsync(new());
        await pane.Find("[data-testid='entry-depends-select'] input").FocusAsync(new());

        var options = pane.FindAll("[data-testid='entry-depends-select'] [role='option']");

        Assert.DoesNotContain(options, option => option.TextContent == "Provision the box");
        Assert.Equal(EntryStatus.Done, done.PreviewStatus);
        Assert.NotNull(open);
    }

    /// <summary>Each option in the open list names the repository it is filed
    /// under, when it has one. With the repository filter showing more than one
    /// repository at a time, two candidates that happen to share a title would
    /// otherwise be indistinguishable in the list — the hint is what tells them
    /// apart without adding a second control to read.</summary>
    [Fact]
    public async Task The_dependency_picker_names_each_options_repository()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs");
        var filed = await host.WriteEntryAsync("# Provision the box\n`task` `@backlog`\n\nGet a machine.\n");
        var unfiled = await host.WriteEntryAsync("# Write the changelog\n`task`\n\nSay what shipped.\n");
        var open = await host.WriteEntryAsync(ExpandedEntry);

        var pane = host.Render();

        await pane.Find("[data-testid='entry-action-depends-set']").ClickAsync(new());
        await pane.Find("[data-testid='entry-depends-select'] input").FocusAsync(new());

        var options = pane.FindAll("[data-testid='entry-depends-select'] [role='option']");

        var filedOption = options.Single(option => option.TextContent.Contains("Provision the box", StringComparison.Ordinal));
        var unfiledOption = options.Single(option => option.TextContent.Contains("Write the changelog", StringComparison.Ordinal));

        // Filed shows the repository it is filed under; unfiled draws no hint at
        // all, exactly as an option looked before hints existed.
        Assert.Equal("backlog", filedOption.QuerySelector(".tag-select__hint")?.TextContent);
        Assert.Null(unfiledOption.QuerySelector(".tag-select__hint"));

        Assert.Equal("backlog", filed.PreviewArea);
        Assert.Null(unfiled.PreviewArea);
        Assert.NotNull(open);
    }

    /// <summary>What it is waiting for, said by title rather than by id. An entry
    /// that only reported "blocked" would leave the reader to go and find out by
    /// what.</summary>
    [Fact]
    public async Task A_waiting_entry_names_what_it_waits_for()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var first = await host.WriteEntryAsync("# Provision the box\n`task`\n\nGet a machine.\n");
        var second = await host.WriteEntryAsync(ExpandedEntry);

        await host.State.ChangeDependsOnAsync(second, [first.Id!.Value.ToString()]);

        var pane = host.Render();
        var waiting = pane.Find("[data-testid='entry-action-depends']");

        Assert.Contains("Provision the box", waiting.TextContent, StringComparison.Ordinal);
    }

    /// <summary>An id naming nothing in view still blocks, and still shows. Quietly
    /// dropping it would make a chain report a shorter wait than it has, which is
    /// the one failure that looks exactly like success.</summary>
    [Fact]
    public async Task An_unresolvable_dependency_is_shown_as_its_id()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(
            "# Deploy SpecManager\n`task` `after:a1b2c3`\n\nShip it.\n");

        Assert.Equal(["a1b2c3"], row.PreviewDependsOn);
        Assert.Contains("a1b2c3", host.Render().Find("[data-testid='entry-action-depends']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clearing_dependencies_removes_every_after_token()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(
            "# Deploy SpecManager\n`task` `after:a1b2c3` `after:d4e5f6`\n\nShip it.\n");

        await host.Render().Find("[data-testid='entry-action-depends-clear']").ClickAsync(new());

        Assert.Empty(row.PreviewDependsOn);
        Assert.DoesNotContain("after:", row.RawText, StringComparison.Ordinal);
    }

    // --- The raw-markdown escape hatch ------------------------------------

    /// <summary>
    /// Raw markdown survived the move, as an escape hatch rather than as the way in.
    /// <para>
    /// <c>.design/content-editing.md#editing-model</c> asks for edit-in-place and
    /// says explicitly that the primary mode must not be "a split write-raw /
    /// preview"; <c>#raw-markdown-escape-hatch</c> asks that a raw view be *always
    /// available* and toggleable. This pane used to have those the wrong way round.
    /// Both halves are asserted here: closed to begin with, and one press away.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_raw_markdown_surface_is_an_escape_hatch_rather_than_the_default()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync(ExpandedEntry);

        var pane = host.Render();

        // Fields first, source behind the shortcut — not the other way round. The
        // body block here is the entry's markdown edited in place, which is a
        // different surface from the raw hatch: it has no metadata line in it, no
        // frontmatter, and no "reads as" line under it.
        Assert.Single(pane.FindAll("[data-testid='entry-body-editor']"));
        Assert.Empty(pane.FindAll("[data-testid='entry-raw-input']"));

        // And no control of its own. The row that used to open it read "Markdown"
        // under a body switch that already said "Markdown".
        Assert.Empty(pane.FindAll("[data-testid='entry-raw-toggle']"));

        await pane.Find("[data-testid='entry-detail']")
            .KeyDownAsync(new KeyboardEventArgs { Key = "M", CtrlKey = true, ShiftKey = true });

        Assert.True(host.State.RawHatchOpen);
        Assert.Single(pane.FindAll("[data-testid='entry-raw-input']"));
        Assert.Single(pane.FindAll("[data-testid='entry-meta-reading']"));
    }

    /// <summary>The hatch shows the entry entire — steps included — because what it
    /// promises is "the exact canonical Markdown that will be stored", and an
    /// entry's steps are stored in it. The editor beside the list used to scope
    /// itself to the parent chapter, which was right for a surface with sub-item
    /// cards laid out below it and wrong for one claiming to be the source.</summary>
    [Fact]
    public async Task The_hatch_shows_the_whole_entry_including_its_steps()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync(
            "# Ship the sync spike\n`task`\n\nNotes on the parent.\n\n## Wire up the store\nHow it gets wired.\n");

        var pane = host.Render();
        await pane.Find("[data-testid='entry-detail']")
            .KeyDownAsync(new KeyboardEventArgs { Key = "M", CtrlKey = true, ShiftKey = true });

        var source = pane.Find("[data-testid='entry-raw-input']").TextContent;

        Assert.Contains("## Wire up the store", source, StringComparison.Ordinal);
        Assert.Contains("Notes on the parent.", source, StringComparison.Ordinal);
    }

    /// <summary>The shortcut <c>#raw-markdown-escape-hatch</c> asks for, alongside
    /// the command. It is on the pane rather than on the button, because a shortcut
    /// that only worked while its own control had focus would be a shortcut for
    /// people who had already found the control.</summary>
    [Fact]
    public async Task Ctrl_shift_m_toggles_the_hatch()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync(ExpandedEntry);

        var pane = host.Render();
        var detail = pane.Find("[data-testid='entry-detail']");

        await detail.KeyDownAsync(new KeyboardEventArgs { Key = "M", CtrlKey = true, ShiftKey = true });

        Assert.True(host.State.RawHatchOpen);
        Assert.Single(pane.FindAll("[data-testid='entry-raw-input']"));

        await pane.Find("[data-testid='entry-detail']").KeyDownAsync(new KeyboardEventArgs { Key = "M", CtrlKey = true, ShiftKey = true });

        Assert.False(host.State.RawHatchOpen);
        Assert.Empty(pane.FindAll("[data-testid='entry-raw-input']"));
    }

    /// <summary>Typed source is still the entry. The hatch writes through the same
    /// debounce-and-flush the editor beside the list used, so a token typed here and
    /// a control pressed above it are one edit arriving by two routes.</summary>
    [Fact]
    public async Task Typing_in_the_hatch_still_writes_the_entry()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(ExpandedEntry);

        var pane = host.Render();
        await pane.Find("[data-testid='entry-detail']")
            .KeyDownAsync(new KeyboardEventArgs { Key = "M", CtrlKey = true, ShiftKey = true });

        // Per keystroke, which is what the editing surface reports: the hatch is
        // debounced text, not a field that commits on change.
        await pane.Find("[data-testid='entry-raw-input']")
            .InputAsync(new() { Value = "# Deploy SpecManager\n`task` `due:2026-08-21`\n\nShip it.\n" });

        await host.State.ToggleRawHatchAsync();

        Assert.Equal(new DateOnly(2026, 8, 21), row.PreviewDueOn);
        Assert.False(host.State.RawHatchOpen);
    }
}
