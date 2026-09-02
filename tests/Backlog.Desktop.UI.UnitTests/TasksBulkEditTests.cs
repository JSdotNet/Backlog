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

    private static Task PickAsync(IRenderedComponent<TasksPane> pane, EntryRow row) =>
        pane.Find($"[data-testid='{RowTestId(row)}-select'] input").ClickAsync(new());

    private static IElement Select(IRenderedComponent<TasksPane> pane, string testId) =>
        pane.Find($"[data-testid='{testId}'] select");

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

        await Select(pane, "bulk-repo").ChangeAsync(new() { Value = "docs" });

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

        await Select(pane, "bulk-status").ChangeAsync(new() { Value = nameof(EntryStatus.InProgress) });

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

        await Select(pane, "bulk-priority").ChangeAsync(new() { Value = nameof(Priority.Critical) });

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
        await Select(pane, "bulk-type").ChangeAsync(new() { Value = nameof(EntryType.Prompt) });

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

        await pane.Find("[data-testid='bulk-myday-set']").ClickAsync(new());

        Assert.Contains($"`myday:{TodayToken}`", one.RawText, StringComparison.Ordinal);
        Assert.Contains($"`myday:{TodayToken}`", two.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Taking_my_day_off_removes_only_that_token()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var (pane, one, two) = await TwoPickedAsync(host);

        await pane.Find("[data-testid='bulk-myday-set']").ClickAsync(new());
        await pane.Find("[data-testid='bulk-myday-clear']").ClickAsync(new());

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

        await Select(pane, "bulk-tag-remove").ChangeAsync(new() { Value = "q4" });

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

        var offered = Select(pane, "bulk-tag-remove")
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

        await Select(pane, "bulk-status").ChangeAsync(new() { Value = nameof(EntryStatus.Archived) });

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

        await Select(pane, "bulk-priority").ChangeAsync(new() { Value = nameof(Priority.Low) });

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

        await Select(pane, "bulk-status").ChangeAsync(new() { Value = nameof(EntryStatus.Ready) });

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
        host.State.SetSelection([one.TaskId, two.TaskId]);

        await Select(pane, "bulk-priority").ChangeAsync(new() { Value = nameof(Priority.Critical) });

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
