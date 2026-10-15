using AngleSharp.Dom;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.UI.Components.Tasks;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// An archived repository in the Tasks pane (local ADR 0029, §3;
/// <c>requirements.md#archiving-a-repository</c>): while show archived is off it has no
/// Board column, no place in a picker, and the tasks filed only under it leave the list,
/// the Board and the Calendar with it. Turning show archived on brings all of it back,
/// marked Archived. docs is archived in every test here; backlog stays active.
/// <para>
/// The state is built without a show-archived port, as a host that composes none, so it
/// holds the choice itself: what is tested is what the pane draws for each answer, not
/// where the answer is kept (<see cref="ShowArchivedRepositoriesTests"/> covers that).
/// </para>
/// </summary>
public sealed class TasksArchivedRepositoryTests
{
    private static async Task<TasksPaneHost> CreateAsync()
    {
        var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs");
        host.Features.SetEnabled(TasksFeatures.AdditionalRepositories, enabled: true);
        Assert.Null(host.GitHub.Settings.SetRepositoryArchived("docs", archived: true));
        return host;
    }

    private static IRenderedComponent<TasksPane> RenderBoardByRepository(TasksPaneHost host) =>
        host.Context.Render<TasksPane>(parameters => parameters
            .Add(pane => pane.Layout, TasksLayout.Board)
            .Add(pane => pane.BoardGrouping, TaskBoardGrouping.Repository));

    private static string[] ColumnTitles(IRenderedComponent<TasksPane> pane) =>
        [.. pane.FindAll("[data-testid='board-column-title']").Select(title => title.TextContent)];

    private static string[] CardIdsIn(IRenderedComponent<TasksPane> pane, string columnKey) =>
        [.. pane.Find($"[data-board-column='{columnKey}']")
            .QuerySelectorAll("[data-testid='board-card']")
            .Select(card => card.GetAttribute("data-task-id")!)];

    private static string[] AllCardIds(IRenderedComponent<TasksPane> pane) =>
        [.. pane.FindAll("[data-testid='board-card']").Select(card => card.GetAttribute("data-task-id")!)];

    private static IElement RowPicker(IRenderedComponent<TasksPane> pane, EntryRow row) =>
        pane.Find($"[data-testid='entry-list-{(row.Id ?? row.Key)}'] [data-testid='row-repo-badge'] select");

    private static string[] OptionLabels(IElement select) =>
        [.. select.QuerySelectorAll("option").Select(option => option.TextContent)];

    /// <summary>Scenario "The Board by repository": no column titled docs, and the task
    /// filed under it is in no other column.</summary>
    [Fact]
    public async Task The_board_by_repository_has_no_column_for_an_archived_repository_and_its_task_is_in_none()
    {
        using var host = await CreateAsync();
        var active = await host.WriteEntryAsync("# Banner copy\n`task` `repo:backlog`\n");
        var archived = await host.WriteEntryAsync("# Retire the docs site\n`task` `repo:docs`\n");

        var pane = RenderBoardByRepository(host);

        Assert.Equal(["backlog"], ColumnTitles(pane));
        Assert.Equal([active.TaskId], CardIdsIn(pane, "repo:backlog"));
        Assert.DoesNotContain(archived.TaskId, AllCardIds(pane));
    }

    /// <summary>A task that also names an active repository stays in view, under the
    /// active one, even when the archived one is named first.</summary>
    [Fact]
    public async Task A_task_naming_an_archived_and_an_active_repository_sits_under_the_active_one()
    {
        using var host = await CreateAsync();
        var both = await host.WriteEntryAsync("# Move the guides\n`task` `repo:docs` `repo:backlog`\n");

        var pane = RenderBoardByRepository(host);

        Assert.Equal(["backlog"], ColumnTitles(pane));
        Assert.Equal([both.TaskId], CardIdsIn(pane, "repo:backlog"));
        Assert.Contains(host.State.FilteredRows, row => row.TaskId == both.TaskId);
    }

    /// <summary>
    /// Turning show archived off while the scope is the archived repository empties the
    /// scope, and the rows follow at once: the list is every repository again, not the
    /// archived repository's rows under chips that say every repository (review S4
    /// round 1, first blocker).
    /// </summary>
    [Fact]
    public async Task Turning_show_archived_off_while_scoped_to_the_archived_repository_lists_every_repository_again()
    {
        using var host = await CreateAsync();
        var both = await host.WriteEntryAsync("# Move the guides\n`task` `repo:docs` `repo:backlog`\n");
        var active = await host.WriteEntryAsync("# Banner copy\n`task` `repo:backlog`\n");
        host.State.SetShowArchivedRepositories(true);
        host.State.SetRepositoryFilter("docs");
        Assert.Equal([both.TaskId], host.State.FilteredRows.Select(row => row.TaskId));

        host.State.SetShowArchivedRepositories(false);

        Assert.Empty(host.State.SelectedRepositoryAliases);
        Assert.Contains(host.State.FilteredRows, row => row.TaskId == both.TaskId);
        Assert.Contains(host.State.FilteredRows, row => row.TaskId == active.TaskId);
        Assert.Contains(host.State.RowsInRepositoryScope, row => row.TaskId == active.TaskId);
    }

    /// <summary>
    /// A task naming an archived repository first and an active one second shows the
    /// active one in its row and detail pickers while show archived is off, the column
    /// the Board files it under, rather than a value no option holds (review S4 round 1,
    /// second blocker).
    /// </summary>
    [Fact]
    public async Task The_pickers_of_a_task_naming_an_archived_and_an_active_repository_show_the_active_one()
    {
        using var host = await CreateAsync();
        var both = await host.WriteEntryAsync("# Move the guides\n`task` `repo:docs` `repo:backlog`\n");

        var pane = host.Render();

        Assert.Equal("backlog", RowPicker(pane, both).GetAttribute("value"));

        await host.OpenAsync(both);
        pane.Render();

        pane.WaitForAssertion(() =>
            Assert.Equal("backlog", pane.Find("[data-testid='repo-badge'] select").GetAttribute("value")));
    }

    /// <summary>
    /// A pick replaces the repository the picker shows, not the hidden archived one
    /// named before it: on a task filed under docs and backlog, picking web while show
    /// archived is off files it under docs and web, keeping docs (review S4 round 2).
    /// </summary>
    [Fact]
    public async Task Picking_a_repository_replaces_the_one_the_picker_shows_and_keeps_the_hidden_archived_one()
    {
        using var host = await TasksPaneHost.CreateAsync(
            "backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs", "web = JSdotNet/Web");
        host.Features.SetEnabled(TasksFeatures.AdditionalRepositories, enabled: true);
        Assert.Null(host.GitHub.Settings.SetRepositoryArchived("docs", archived: true));
        var both = await host.WriteEntryAsync("# Move the guides\n`task` `repo:docs` `repo:backlog`\n");

        await host.State.ChangeRepositoryAsync(both, "web");

        var written = Assert.Single(host.State.Rows, row => row.TaskId == both.TaskId);
        Assert.Equal(["docs", "web"], written.PreviewRepoIds.Select(id => id.ToLowerInvariant()));
    }

    /// <summary>Scenario "Show archived is on", for the Board: docs gets its column back
    /// and the task is in it.</summary>
    [Fact]
    public async Task With_show_archived_on_the_archived_repository_has_its_column_again()
    {
        using var host = await CreateAsync();
        var archived = await host.WriteEntryAsync("# Retire the docs site\n`task` `repo:docs`\n");
        var pane = RenderBoardByRepository(host);
        Assert.Empty(ColumnTitles(pane));

        await pane.InvokeAsync(() => host.State.SetShowArchivedRepositories(true));

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["docs"], ColumnTitles(pane));
            Assert.Equal([archived.TaskId], CardIdsIn(pane, "repo:docs"));
        });
    }

    /// <summary>The person's answer on the task list: with show archived off, a task
    /// filed only under an archived repository leaves the list, and the Calendar, which
    /// draws the same filtered rows. A task filed under no repository is the Brain's and
    /// stays.</summary>
    [Fact]
    public async Task A_task_filed_only_under_an_archived_repository_leaves_the_list_until_show_archived_is_on()
    {
        using var host = await CreateAsync();
        var archived = await host.WriteEntryAsync("# Retire the docs site\n`task` `repo:docs` `due:2026-10-20`\n");
        var brain = await host.WriteEntryAsync("# Read the paper\n`task`\n");

        var pane = host.Render();

        Assert.DoesNotContain(host.State.FilteredRows, row => row.TaskId == archived.TaskId);
        Assert.Contains(host.State.FilteredRows, row => row.TaskId == brain.TaskId);
        Assert.Empty(pane.FindAll($"[data-testid='entry-list-{archived.Id}']"));

        await pane.InvokeAsync(() => host.State.SetShowArchivedRepositories(true));

        Assert.Contains(host.State.FilteredRows, row => row.TaskId == archived.TaskId);
        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll($"[data-testid='entry-list-{archived.Id}']")));
    }

    /// <summary>Scenario "Show archived is on", for the pickers: the repository picker
    /// offers docs only while it is on, and then names it archived.</summary>
    [Fact]
    public async Task The_repository_picker_offers_an_archived_repository_only_while_show_archived_is_on()
    {
        using var host = await CreateAsync();
        var row = await host.WriteEntryAsync("# Banner copy\n`task` `repo:backlog`\n");

        var pane = host.Render();

        Assert.Equal(["Brain", "backlog"], OptionLabels(RowPicker(pane, row)));

        await pane.InvokeAsync(() => host.State.SetShowArchivedRepositories(true));

        pane.WaitForAssertion(() =>
            Assert.Equal(["Brain", "backlog", "docs (archived)"], OptionLabels(RowPicker(pane, row))));
    }

    /// <summary>The scope can be pressed on an archived repository only while its chip
    /// is offered: a press that names it while show archived is off changes nothing.</summary>
    [Fact]
    public async Task An_archived_repository_cannot_be_scoped_while_show_archived_is_off()
    {
        using var host = await CreateAsync();

        host.State.SetRepositoryFilter("docs");
        Assert.Empty(host.State.SelectedRepositoryAliases);
        Assert.Equal(["backlog"], host.State.Repositories.Select(repository => repository.Alias));

        host.State.SetShowArchivedRepositories(true);
        host.State.SetRepositoryFilter("docs");

        Assert.Equal(["docs"], host.State.SelectedRepositoryAliases);
        Assert.Equal(["backlog", "docs"], host.State.Repositories.Select(repository => repository.Alias));
    }
}
