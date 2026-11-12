using Backlog.Modules.Tasks.Abstractions.Services;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The <b>Next actions</b> slice in the filter bar, its area chips, and the open
/// task's Area picker (local ADR 0054 §4 and §5,
/// <c>.devbook/domain/tasks/features.md#next-actions</c>).
/// <para>
/// The slice keeps the open tasks that can be picked up now — readiness ready,
/// waiting on nobody, not marked blocked — grouped by context tag, then the tasks
/// with no context. A scope like My Day beside it: it narrows what the other scopes
/// already left in view.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class NextActionsScopeTests
{
    private const string Chip = "[data-testid='nextactions-filter-option']";
    private const string AreaChip = "[data-testid='area-filter-option']";

    private static string RowTestId(EntryRow row) => $"entry-list-{row.TaskId}";

    private sealed record Seven(
        TasksPaneHost Host,
        EntryRow Calls,
        EntryRow Pc,
        EntryRow PcAndCalls,
        EntryRow NoContext,
        EntryRow Waiting,
        EntryRow Blocked,
        EntryRow Ticked,
        EntryRow After);

    /// <summary>Four next actions — two contexts, one with both, one with none —
    /// and four that are not: one waiting on someone, one marked blocked, one ticked
    /// off, and one waiting on an open entry before it.</summary>
    private static async Task<Seven> SevenAsync(params string[] repositories)
    {
        var host = await TasksPaneHost.CreateAsync(repositories);

        var noContext = await host.WriteEntryAsync("# Read the lease\n`task` `!ready` `@home`\n");
        var pc = await host.WriteEntryAsync("# Print the forms\n`task` `!ready` `@work` `#@pc`\n");
        var calls = await host.WriteEntryAsync("# Call the plumber #@calls\n`task` `!ready` `@home`\n");
        var both = await host.WriteEntryAsync("# Book the venue\n`task` `!ready`\n\nOnline #@pc, or ring them #@calls.\n");
        var waiting = await host.WriteEntryAsync("# Get the quote\n`task` `!ready` `waiting:Jansen Roofing` `#@calls`\n");
        var blocked = await host.WriteEntryAsync("# Fix the roof\n`task` `!ready` `blocked:2026-10-05`\n");
        var ticked = await host.WriteEntryAsync("# Get the keys\n`task` `!done` `completed:2026-11-02` `#@errands`\n");
        var after = await host.WriteEntryAsync($"# Paint the fence\n`task` `!ready` `after:{blocked.TaskId}`\n");

        // Writing leaves the last entry open, and an open entry is pinned into view
        // whatever the filters say.
        await host.State.SelectAsync(null);

        return new Seven(host, calls, pc, both, noContext, waiting, blocked, ticked, after);
    }

    [Fact]
    public async Task The_slice_starts_off_and_counts_the_next_actions()
    {
        var seven = await SevenAsync();
        using var _host = seven.Host;

        var pane = seven.Host.Render();
        var chip = pane.Find(Chip);

        Assert.Equal("false", chip.GetAttribute("aria-pressed"));
        Assert.False(seven.Host.State.NextActionsOnly);
        Assert.Equal("4", chip.QuerySelector(".chip__count")?.TextContent);
        Assert.Empty(pane.FindAll(AreaChip));
    }

    [Fact]
    public async Task The_slice_keeps_the_next_actions_grouped_by_context_then_no_context()
    {
        var seven = await SevenAsync();
        using var _host = seven.Host;

        var pane = seven.Host.Render();
        await pane.Find(Chip).ClickAsync(new());

        Assert.True(seven.Host.State.NextActionsOnly);

        // @calls before @pc by name; the entry with both is listed once, under its
        // first context; the task with none comes last.
        Assert.Equal([seven.Calls, seven.PcAndCalls, seven.Pc, seven.NoContext], seven.Host.State.FilteredRows);
        Assert.Equal(
            ["@calls", "@pc", TasksDesktopState.NoContextGroup],
            pane.FindAll("[data-testid='entry-list-section']").Select(heading => heading.TextContent.Trim()));

        foreach (var outside in new[] { seven.Waiting, seven.Blocked, seven.Ticked, seven.After })
        {
            Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(outside)}']"));
        }
    }

    [Fact]
    public async Task Pressing_it_again_shows_everything_in_rank_order_with_no_headings()
    {
        var seven = await SevenAsync();
        using var _host = seven.Host;

        var pane = seven.Host.Render();
        await pane.Find(Chip).ClickAsync(new());
        await pane.Find(Chip).ClickAsync(new());

        Assert.False(seven.Host.State.NextActionsOnly);
        Assert.Contains(seven.Waiting, seven.Host.State.FilteredRows);
        Assert.Equal(seven.Host.State.Rows.Where(seven.Host.State.FilteredRows.Contains), seven.Host.State.FilteredRows);
        Assert.Empty(pane.FindAll("[data-testid='entry-list-section']"));
    }

    /// <summary>Clearing the waiting-for returns the task to the next actions
    /// (<c>.devbook/domain/tasks/domain.md#waiting-for</c>).</summary>
    [Fact]
    public async Task Clearing_the_waiting_for_returns_a_task_to_the_next_actions()
    {
        var seven = await SevenAsync();
        using var _host = seven.Host;

        seven.Host.State.SetNextActionsFilter(true);
        Assert.DoesNotContain(seven.Waiting, seven.Host.State.FilteredRows);

        await seven.Host.State.ChangeWaitingForAsync(seven.Waiting, null);
        await seven.Host.State.SelectAsync(null);
        seven.Host.State.SetNextActionsFilter(true);

        Assert.Contains(seven.Waiting, seven.Host.State.FilteredRows);
    }

    [Fact]
    public async Task The_area_chips_narrow_the_slice_and_name_the_unfiled_tasks()
    {
        var seven = await SevenAsync();
        using var _host = seven.Host;

        var pane = seven.Host.Render();
        await pane.Find(Chip).ClickAsync(new());

        // The areas the slice holds, by name, then the unfiled.
        Assert.Equal(
            ["home", "work", "No area"],
            pane.FindAll(AreaChip).Select(chip => chip.FirstChild!.TextContent.Trim()));

        await pane.FindAll(AreaChip)[0].ClickAsync(new());

        Assert.Equal("home", seven.Host.State.SelectedArea);
        Assert.Equal([seven.Calls, seven.NoContext], seven.Host.State.FilteredRows);

        await pane.FindAll(AreaChip)[2].ClickAsync(new());

        Assert.Equal(TasksDesktopState.NoArea, seven.Host.State.SelectedArea);
        Assert.Equal([seven.PcAndCalls], seven.Host.State.FilteredRows);

        // Turning the slice off lets go of the area too, and its chips go with it.
        await pane.Find(Chip).ClickAsync(new());

        Assert.Null(seven.Host.State.SelectedArea);
        Assert.Empty(pane.FindAll(AreaChip));
    }

    /// <summary>The slice narrows what the repository scope left in view: the Brain
    /// chip, pressed alone, keeps the next actions filed against no repository.</summary>
    [Fact]
    public async Task The_slice_composes_with_the_repository_scope()
    {
        var seven = await SevenAsync("backlog = JSdotNet/Backlog");
        using var _host = seven.Host;

        var filed = await seven.Host.WriteEntryAsync("# Ship the build #@pc\n`task` `!ready` `repo:backlog`\n");
        await seven.Host.State.SelectAsync(null);

        seven.Host.State.SetNextActionsFilter(true);
        Assert.Contains(filed, seven.Host.State.FilteredRows);

        seven.Host.State.SetBrainFilter();

        Assert.DoesNotContain(filed, seven.Host.State.FilteredRows);
        Assert.Contains(seven.Pc, seven.Host.State.FilteredRows);
    }

    /// <summary>The slice is in context order, not rank order, so a move there would
    /// rewrite a rank the reader cannot see: it changes nothing, and the status sort
    /// is not offered.</summary>
    [Fact]
    public async Task Reordering_and_the_status_sort_are_off_in_the_slice()
    {
        var seven = await SevenAsync();
        using var _host = seven.Host;

        seven.Host.State.SetNextActionsFilter(true);
        var ranks = seven.Host.State.Rows.ToList();

        await seven.Host.State.MoveEntryAsync(seven.NoContext, seven.Calls);

        Assert.Equal(ranks, seven.Host.State.Rows);
        Assert.False(seven.Host.State.CanSortVisibleByStatus);
    }

    /// <summary>A next action waits on nobody, so pressing one of the two slices lets
    /// go of the other rather than listing nothing.</summary>
    [Fact]
    public async Task The_waiting_and_next_actions_slices_let_go_of_each_other()
    {
        var seven = await SevenAsync();
        using var _host = seven.Host;

        seven.Host.State.SetWaitingForFilter(true);
        seven.Host.State.SetNextActionsFilter(true);

        Assert.False(seven.Host.State.WaitingForOnly);
        Assert.NotEmpty(seven.Host.State.FilteredRows);

        seven.Host.State.SetAreaFilter("home");
        seven.Host.State.SetWaitingForFilter(true);

        Assert.False(seven.Host.State.NextActionsOnly);
        Assert.Null(seven.Host.State.SelectedArea);
        Assert.Equal([seven.Waiting], seven.Host.State.FilteredRows);
    }

    /// <summary>A body context is prose: adding a tag through the editor leaves it in
    /// the body and never copies it on to the title.</summary>
    [Fact]
    public async Task A_tag_edit_does_not_copy_a_body_context_on_to_the_title()
    {
        var seven = await SevenAsync();
        using var _host = seven.Host;

        Assert.DoesNotContain("@pc", seven.PcAndCalls.PreviewEditableTags);

        await seven.Host.State.ChangeTagsAsync(seven.PcAndCalls, [.. seven.PcAndCalls.PreviewEditableTags, "q4"]);

        Assert.StartsWith("# Book the venue\n", seven.PcAndCalls.RawText, StringComparison.Ordinal);
        Assert.Contains("@pc", seven.PcAndCalls.PreviewTags);
        Assert.Contains("q4", seven.PcAndCalls.PreviewTags);
    }

    // --- The open task's Area picker -------------------------------------------

    private sealed class FakeBrainAreaNames(params string[] names) : IBrainAreaNames
    {
        public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(names);
    }

    [Fact]
    public async Task The_area_picker_suggests_the_brain_areas_first_then_the_areas_in_use()
    {
        using var host = await TasksPaneHost.CreateAsync();
        host.Context.Services.AddSingleton<IBrainAreaNames>(new FakeBrainAreaNames("projects", "areas", "Home"));
        await host.WriteEntryAsync("# Read the lease\n`task` `@home`\n");
        await host.WriteEntryAsync("# Mow the lawn\n`task` `@garden`\n");
        var row = await host.WriteEntryAsync("# Fix the roof\n`task` `!ready`\n");

        var pane = host.Render();

        Assert.Contains("File under an area", pane.Find("[data-testid='entry-action-area']").TextContent, StringComparison.Ordinal);

        await pane.Find("[data-testid='entry-action-area-set']").ClickAsync(new());
        await pane.Find("[data-testid='entry-area-input'] input").ClickAsync(new());

        // The Brain's areas in its order, then the areas in use not already named —
        // `home` is the Brain's `Home`, without regard to case.
        Assert.Equal(
            ["projects", "areas", "Home", "garden"],
            pane.FindAll("[data-testid='entry-area-input'] [role='option']").Select(option => option.GetAttribute("data-value")));

        await pane.Find("[data-testid='entry-area-input'] [data-value='projects']").ClickAsync(new());

        Assert.Equal("projects", row.PreviewArea);
        Assert.Contains("`@projects`", row.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_area_picker_takes_free_text_and_clears()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Fix the roof\n`task` `!ready`\n");

        var pane = host.Render();

        await pane.Find("[data-testid='entry-action-area-set']").ClickAsync(new());
        var input = pane.Find("[data-testid='entry-area-input'] input");
        await input.InputAsync(new() { Value = "errands" });
        await pane.Find("[data-testid='entry-area-input'] input").ChangeAsync(new() { Value = "errands" });

        Assert.Equal("errands", row.PreviewArea);
        Assert.Contains("errands", pane.Find("[data-testid='entry-action-area']").TextContent, StringComparison.Ordinal);

        await pane.Find("[data-testid='entry-action-area-clear']").ClickAsync(new());

        Assert.Null(row.PreviewArea);
        Assert.DoesNotContain("`@", row.RawText, StringComparison.Ordinal);
    }

    /// <summary>The suggestions as the state orders them, with no Brain composed: the
    /// areas in use alone, by name, each once.</summary>
    [Fact]
    public async Task With_no_brain_the_suggestions_are_the_areas_in_use()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync("# One\n`task` `@work`\n");
        await host.WriteEntryAsync("# Two\n`task` `@Home`\n");
        await host.WriteEntryAsync("# Three\n`task` `@work`\n");

        Assert.Equal(["home", "work"], host.State.AreaSuggestions(null));
        Assert.Equal(["work", "Garden", "home"], host.State.AreaSuggestions(["work", "Garden"]));
    }
}
