using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions;
using Microsoft.AspNetCore.Components.Web;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The menu a right-click on an entry row opens.
/// <para>
/// Every item is an act the detail pane already offers, so what is under test is
/// not the write — <see cref="EntryScheduleControlsTests"/> covers those — but
/// that the menu reaches the same write from the row, says the right thing about
/// the row it was opened on, and closes once it has been answered.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class TasksPaneContextMenuTests
{
    private const string Entry = "# Provision the box\n`task` `!in-progress`\n";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private static string RowTestId(EntryRow row) => $"entry-list-{(row.Id ?? row.Key)}";

    private static Task OpenMenuAsync(IRenderedComponent<TasksPane> pane, EntryRow row) =>
        pane.Find($"[data-testid='{RowTestId(row)}']")
            .ContextMenuAsync(new MouseEventArgs { ClientX = 40, ClientY = 60 });

    private static Task ChooseAsync(IRenderedComponent<TasksPane> pane, string item) =>
        pane.Find($"[data-testid='entry-menu-item-{item}']").ClickAsync(new());

    private static string Label(IRenderedComponent<TasksPane> pane, string item) =>
        pane.Find($"[data-testid='entry-menu-item-{item}'] .menu-list__label").TextContent;

    private static bool IsDisabled(IRenderedComponent<TasksPane> pane, string item) =>
        pane.Find($"[data-testid='entry-menu-item-{item}']").HasAttribute("disabled");

    [Fact]
    public async Task A_right_click_opens_the_menu_where_the_pointer_was()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);

        var pane = host.Render();
        Assert.Empty(pane.FindAll("[data-testid='entry-menu']"));

        await OpenMenuAsync(pane, row);

        var menu = pane.Find("[data-testid='entry-menu']");
        Assert.Contains("--context-menu-x: 40px", menu.GetAttribute("style"), StringComparison.Ordinal);
        Assert.Contains("--context-menu-y: 60px", menu.GetAttribute("style"), StringComparison.Ordinal);

        // The order Microsoft To Do settled on: the marks, the dates, the place, the end.
        Assert.Equal(
            ["myday", "important", "done", "due-today", "due-tomorrow", "due-pick", "due-clear", "move-top", "move-up", "move-down", "delete"],
            pane.FindAll("[data-testid='entry-menu'] [role='menuitem']")
                .Select(item => item.GetAttribute("data-testid")!["entry-menu-item-".Length..]));
    }

    [Fact]
    public async Task Due_today_and_due_tomorrow_write_the_date_and_close_the_menu()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        var pane = host.Render();

        await OpenMenuAsync(pane, row);
        await ChooseAsync(pane, "due-today");

        Assert.Equal(Today, row.PreviewDueOn);
        Assert.Empty(pane.FindAll("[data-testid='entry-menu']"));

        await OpenMenuAsync(pane, row);
        await ChooseAsync(pane, "due-tomorrow");

        Assert.Equal(Today.AddDays(1), row.PreviewDueOn);
    }

    [Fact]
    public async Task Remove_due_date_is_offered_disabled_until_there_is_one_to_remove()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        var pane = host.Render();

        await OpenMenuAsync(pane, row);
        Assert.True(IsDisabled(pane, "due-clear"));

        await ChooseAsync(pane, "due-today");
        await OpenMenuAsync(pane, row);
        Assert.False(IsDisabled(pane, "due-clear"));

        await ChooseAsync(pane, "due-clear");

        Assert.Null(row.PreviewDueOn);
    }

    [Fact]
    public async Task Pick_a_date_opens_the_entry_with_its_due_picker_showing()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        await host.WriteEntryAsync("# Deploy it\n`task`\n");
        var pane = host.Render();

        Assert.NotSame(row, host.State.SelectedRow);

        await OpenMenuAsync(pane, row);
        await ChooseAsync(pane, "due-pick");

        // The date field lives in the detail pane, so that is where the reader lands.
        Assert.Same(row, host.State.SelectedRow);
        Assert.NotEmpty(pane.FindAll("[data-testid='entry-due-input']"));
    }

    [Fact]
    public async Task My_Day_is_a_toggle_whose_label_names_the_way_out()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        var pane = host.Render();

        await OpenMenuAsync(pane, row);
        Assert.Equal("Add to My Day", Label(pane, "myday"));
        await ChooseAsync(pane, "myday");

        Assert.Equal(Today, row.PreviewInMyDayOn);

        await OpenMenuAsync(pane, row);
        Assert.Equal("Remove from My Day", Label(pane, "myday"));
        await ChooseAsync(pane, "myday");

        Assert.Null(row.PreviewInMyDayOn);
    }

    [Fact]
    public async Task Important_lifts_the_priority_to_high_and_back_to_medium()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        var pane = host.Render();

        await OpenMenuAsync(pane, row);
        Assert.Equal("Mark as important", Label(pane, "important"));
        await ChooseAsync(pane, "important");

        Assert.Equal(Priority.High, row.PreviewPriority);

        await OpenMenuAsync(pane, row);
        Assert.Equal("Remove importance", Label(pane, "important"));
        await ChooseAsync(pane, "important");

        Assert.Equal(Priority.Medium, row.PreviewPriority);
    }

    [Fact]
    public async Task Mark_as_completed_finishes_the_row()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        var pane = host.Render();

        await OpenMenuAsync(pane, row);
        Assert.Equal("Mark as completed", Label(pane, "done"));
        Assert.NotEqual(EntryStatus.Done, row.PreviewStatus);
        await ChooseAsync(pane, "done");

        // The same move the circle makes: ticked, and Done with it.
        Assert.True(row.IsPreviewCompleted);
        Assert.Equal(EntryStatus.Done, row.PreviewStatus);

        // The row folded away under Completed; open the fold to reach it again.
        pane.Render();
        await pane.Find("[data-testid='entry-list-completed-toggle']").ClickAsync(new());
        await OpenMenuAsync(pane, row);
        Assert.Equal("Mark as not completed", Label(pane, "done"));
    }

    [Fact]
    public async Task Move_up_and_move_down_swap_with_the_neighbour_on_screen()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var first = await host.WriteEntryAsync("# First\n`task`\n");
        var second = await host.WriteEntryAsync("# Second\n`task`\n");
        var third = await host.WriteEntryAsync("# Third\n`task`\n");
        var pane = host.Render();

        Assert.Equal([first, second, third], host.State.Rows);

        await OpenMenuAsync(pane, first);
        Assert.True(IsDisabled(pane, "move-up"));
        Assert.False(IsDisabled(pane, "move-down"));
        await ChooseAsync(pane, "move-down");

        Assert.Equal([second, first, third], host.State.Rows);

        await OpenMenuAsync(pane, third);
        Assert.False(IsDisabled(pane, "move-up"));
        Assert.True(IsDisabled(pane, "move-down"));
        await ChooseAsync(pane, "move-up");

        Assert.Equal([second, third, first], host.State.Rows);
    }

    [Fact]
    public async Task Move_to_top_puts_the_row_first_among_the_open_rows()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var first = await host.WriteEntryAsync("# First\n`task`\n");
        var second = await host.WriteEntryAsync("# Second\n`task`\n");
        var third = await host.WriteEntryAsync("# Third\n`task`\n");
        var pane = host.Render();

        await OpenMenuAsync(pane, first);
        Assert.True(IsDisabled(pane, "move-top"));
        await pane.Find(".context-menu__backdrop").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        await OpenMenuAsync(pane, third);
        Assert.Equal("Move to top", Label(pane, "move-top"));
        Assert.False(IsDisabled(pane, "move-top"));
        await ChooseAsync(pane, "move-top");

        Assert.Equal([third, first, second], host.State.Rows);
        Assert.Empty(pane.FindAll("[data-testid='entry-menu']"));
    }

    [Fact]
    public async Task Delete_task_removes_the_row()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        var pane = host.Render();

        await OpenMenuAsync(pane, row);
        await ChooseAsync(pane, "delete");

        Assert.DoesNotContain(row, host.State.Rows);
        Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(row)}']"));
    }

    [Fact]
    public async Task Escape_closes_the_menu_without_choosing()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        var pane = host.Render();

        await OpenMenuAsync(pane, row);
        await pane.Find(".context-menu__backdrop").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(pane.FindAll("[data-testid='entry-menu']"));
        Assert.Null(row.PreviewDueOn);
    }

    // --- Merging the row's pull request --------------------------------------

    /// <summary>
    /// The row's latest open pull request is offered to GitHub's auto-merge, and
    /// choosing it asks GitHub and reads the pull request back: the next time the
    /// menu opens it offers the way out, because that is what GitHub now holds.
    /// </summary>
    [Fact]
    public async Task Merge_when_checks_pass_hands_the_pull_request_to_auto_merge()
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var row = await host.WriteEntryAsync(Entry);
        WithPullRequest(host, row, 710);
        var pane = host.Render();

        await OpenMenuAsync(pane, row);
        Assert.Equal("Merge #710 when checks pass", Label(pane, "merge-pr"));

        // Its own group, ahead of the one act that ends the row.
        var items = pane.FindAll("[data-testid='entry-menu'] [role='menuitem']")
            .Select(item => item.GetAttribute("data-testid")!["entry-menu-item-".Length..])
            .ToList();
        Assert.Equal(items.IndexOf("delete") - 1, items.IndexOf("merge-pr"));

        await ChooseAsync(pane, "merge-pr");

        Assert.Equal(["enable 710"], host.Client.MergeCalls);
        Assert.Empty(pane.FindAll("[data-testid='entry-menu']"));
        Assert.NotNull(pane.Find("[data-testid='row-pull-request-auto-merge']"));

        await OpenMenuAsync(pane, row);
        Assert.Equal("Cancel auto-merge for #710", Label(pane, "merge-pr"));
        await ChooseAsync(pane, "merge-pr");

        Assert.Equal(["enable 710", "disable 710"], host.Client.MergeCalls);
    }

    /// <summary>GitHub refuses to queue a pull request that can already merge, so
    /// for one the menu offers the merge itself.</summary>
    [Fact]
    public async Task A_pull_request_that_can_already_merge_is_offered_merge_now()
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var row = await host.WriteEntryAsync(Entry);
        var pr = WithPullRequest(host, row, 710, mergeReady: true);
        var pane = host.Render();

        await OpenMenuAsync(pane, row);
        Assert.Equal("Merge #710 now", Label(pane, "merge-pr"));
        await ChooseAsync(pane, "merge-pr");

        Assert.Equal(["merge 710"], host.Client.MergeCalls);
        Assert.Equal(GitHubItemState.Merged, row.PullRequestStates[pr]);
        Assert.Contains("entry-doc__work-link--merged", pane.Find("[data-testid='row-pull-request']").ClassList);
    }

    [Fact]
    public async Task A_refused_merge_is_said_out_loud_and_the_row_keeps_working()
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var row = await host.WriteEntryAsync(Entry);
        WithPullRequest(host, row, 710);
        host.Client.MergeFailure = new GitHubException("Pull request Auto merge is not allowed for this repository");
        var pane = host.Render();

        await OpenMenuAsync(pane, row);
        await ChooseAsync(pane, "merge-pr");

        var toast = Assert.Single(host.Toasts.Visible);
        Assert.Contains("Allow auto-merge", toast.Message, StringComparison.Ordinal);

        // Still answers: the next act goes through.
        await OpenMenuAsync(pane, row);
        await ChooseAsync(pane, "due-today");
        Assert.Equal(Today, row.PreviewDueOn);
    }

    [Fact]
    public async Task No_merge_is_offered_while_the_integration_is_off()
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var row = await host.WriteEntryAsync(Entry);
        WithPullRequest(host, row, 710);
        host.Features.SetEnabled(TasksFeatures.GitHubIntegration, enabled: false);
        var pane = host.Render();

        await OpenMenuAsync(pane, row);

        Assert.Empty(pane.FindAll("[data-testid='entry-menu-item-merge-pr']"));
    }

    [Fact]
    public async Task No_merge_is_offered_while_no_repository_is_configured()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        WithPullRequest(host, row, 710);
        var pane = host.Render();

        await OpenMenuAsync(pane, row);

        Assert.Empty(pane.FindAll("[data-testid='entry-menu-item-merge-pr']"));
    }

    /// <summary>A draft cannot merge, and one whose status nobody has read has no
    /// act to name — so neither is offered.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task No_merge_is_offered_for_a_draft_or_an_unread_pull_request(bool draft, bool unread)
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var row = await host.WriteEntryAsync(Entry);
        WithPullRequest(host, row, 710, draft: draft);
        if (unread) row.PullRequestStatuses = new Dictionary<EntryPullRequestLink, GitHubPullRequestStatus>();
        var pane = host.Render();

        await OpenMenuAsync(pane, row);

        Assert.Empty(pane.FindAll("[data-testid='entry-menu-item-merge-pr']"));
    }

    /// <summary>A row with one recorded pull request, read, and the same status on
    /// the fake so the re-read after an act answers.</summary>
    private static EntryPullRequestLink WithPullRequest(
        TasksPaneHost host,
        EntryRow row,
        int number,
        bool mergeReady = false,
        bool draft = false)
    {
        var pr = new EntryPullRequestLink("JSdotNet/Backlog", number);
        var state = draft ? GitHubItemState.Draft : GitHubItemState.Open;
        var status = new GitHubPullRequestStatus(
            number, pr.Repository, $"PR_{number}", state, GitHubCheckState.Pending,
            AutoMergeEnabled: false, MergeReady: mergeReady, GitHubMergeMethod.Merge);

        host.Client.PullRequestStatuses[number] = status;
        row.PullRequestLinks = [pr];
        row.PullRequestStates = new Dictionary<EntryPullRequestLink, GitHubItemState> { [pr] = state };
        row.PullRequestStatuses = new Dictionary<EntryPullRequestLink, GitHubPullRequestStatus> { [pr] = status };
        return pr;
    }

    /// <summary>The menu's backdrop takes the focus when it opens, and it sits
    /// outside both halves of the split — which is exactly the move the pane reads
    /// as the reader having left the open entry. Asking a row for its menu is not
    /// leaving.</summary>
    [Fact]
    public async Task The_open_entry_survives_its_row_being_right_clicked()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        await host.OpenAsync(row);

        // The focus did land outside the detail pane — on the menu's backdrop.
        host.Context.JSInterop.Setup<bool>("backlogFocusOutside", _ => true).SetResult(true);

        var pane = host.Render();
        await OpenMenuAsync(pane, row);
        await pane.Find("[data-testid='backlog-pane']").TriggerEventAsync("onfocusout", new FocusEventArgs());

        Assert.Same(row, host.State.SelectedRow);
    }
}
