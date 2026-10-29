using Backlog.Desktop.UI.Tasks;
using Backlog.Infrastructure.FileSystem;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Tasks and Board surfaces follow the query they are shown with (local ADR 0041
/// §6): <c>plan=&lt;tag&gt;</c> opens on that plan chip, and <c>entry=&lt;id&gt;</c>, on
/// Tasks, opens the entry in its editor, widening the window's scope to the entry's
/// repository when the entry is outside it. An id the backlog does not hold opens
/// Tasks and says so in a toast.
/// </summary>
public sealed class TasksSurfaceQueryTests
{
    [Theory]
    [InlineData("release-q4")]
    [InlineData("+release-q4")]
    [InlineData("+Release-Q4")]
    public async Task Plan_opens_tasks_with_that_plan_chip_selected(string plan)
    {
        using var host = await CreateHostAsync();
        var release = await host.WriteEntryAsync("# Ship it +release-q4\n`task` `!ready`\n");
        var bare = await host.WriteEntryAsync("# Filed before the sigil\n`task` `!ready` `#release-q4`\n");
        await host.WriteEntryAsync("# Import it +roadmap-import\n`task` `!ready`\n");
        await host.State.SelectAsync(null);

        var surface = Render<TasksSurface>(host, TasksSurfaceQuery.ForPlan(plan));

        surface.WaitForAssertion(() =>
        {
            Assert.Equal("+release-q4", host.State.SelectedPlan);
            // The tag with or without its +, as PlanTagMatch.Carries reads it.
            Assert.Equal(new[] { release, bare }.Select(row => row.Id).Order(), host.State.FilteredRows.Select(row => row.Id).Order());
            Assert.NotEmpty(surface.FindAll("[data-testid='entry-list']"));
        });
    }

    [Fact]
    public async Task Plan_opens_the_board_with_that_plan_chip_selected()
    {
        using var host = await CreateHostAsync();
        var release = await host.WriteEntryAsync("# Ship it +release-q4\n`task` `!ready`\n");
        await host.WriteEntryAsync("# Import it +roadmap-import\n`task` `!ready`\n");
        await host.State.SelectAsync(null);

        var board = Render<BoardSurface>(host, TasksSurfaceQuery.ForPlan("release-q4"));

        board.WaitForAssertion(() =>
        {
            Assert.Equal("+release-q4", host.State.SelectedPlan);
            Assert.Equal([release.Id], host.State.FilteredRows.Select(row => row.Id));
            Assert.NotEmpty(board.FindAll("[data-testid='task-board']"));
        });
    }

    /// <summary>The query is a request: drawing the surface again with the same query
    /// does not press the plan again over a chip the reader has since let go.</summary>
    [Fact]
    public async Task The_same_query_drawn_again_is_not_followed_twice()
    {
        using var host = await CreateHostAsync();
        await host.WriteEntryAsync("# Ship it +release-q4\n`task` `!ready`\n");
        await host.State.SelectAsync(null);

        var query = TasksSurfaceQuery.ForPlan("release-q4");
        var surface = Render<TasksSurface>(host, query);
        surface.WaitForAssertion(() => Assert.Equal("+release-q4", host.State.SelectedPlan));

        await surface.InvokeAsync(() => host.State.ToggleTagFilter("+release-q4"));
        surface.Render(parameters => parameters.Add(component => component.Query, query));

        Assert.Null(host.State.SelectedPlan);
    }

    [Fact]
    public async Task A_plan_that_is_no_plan_tag_is_said_out_loud()
    {
        using var host = await CreateHostAsync();

        Render<TasksSurface>(host, TasksSurfaceQuery.ForPlan("@bob"));

        Assert.Contains(host.Toasts.Visible, toast => toast.TestId == TasksSurface.PlanInvalidTestId);
        Assert.Null(host.State.SelectedPlan);
    }

    [Fact]
    public async Task Entry_opens_that_entry_in_its_editor()
    {
        using var host = await CreateHostAsync();
        var wanted = await host.WriteEntryAsync("# Provision the box\n`task` `repo:backlog`\n");
        await host.WriteEntryAsync("# Write it up\n`task` `repo:backlog`\n");
        await host.State.SelectAsync(null);

        var surface = Render<TasksSurface>(host, TasksSurfaceQuery.ForEntry(wanted.Id!.Value.ToString()));

        surface.WaitForAssertion(() =>
        {
            Assert.Same(wanted, host.State.SelectedRow);
            Assert.NotEmpty(surface.FindAll("[data-testid='entry-detail']"));
        });
    }

    /// <summary>The entry is in <c>docs</c> and the window is scoped to <c>backlog</c>:
    /// <c>docs</c> joins the scope, and <c>backlog</c> stays.</summary>
    [Fact]
    public async Task Entry_outside_the_scope_widens_the_scope_to_its_repository()
    {
        using var host = await CreateHostAsync();
        var theirs = await host.WriteEntryAsync("# Write it up\n`task` `repo:docs`\n");
        await host.WriteEntryAsync("# Provision the box\n`task` `repo:backlog`\n");
        await host.State.SelectAsync(null);
        host.State.SetRepositoryFilter("backlog");
        Assert.DoesNotContain(theirs, host.State.FilteredRows);

        var surface = Render<TasksSurface>(host, TasksSurfaceQuery.ForEntry(theirs.Id!.Value.ToString()));

        surface.WaitForAssertion(() =>
        {
            Assert.Equal(["backlog", "docs"], host.State.SelectedRepositoryAliases);
            Assert.Contains(theirs, host.State.FilteredRows);
            Assert.Same(theirs, host.State.SelectedRow);
        });
    }

    /// <summary>An entry already in the scope leaves the scope as it was.</summary>
    [Fact]
    public async Task Entry_inside_the_scope_leaves_the_scope_alone()
    {
        using var host = await CreateHostAsync();
        var mine = await host.WriteEntryAsync("# Provision the box\n`task` `repo:backlog`\n");
        await host.State.SelectAsync(null);
        host.State.SetRepositoryFilter("backlog");

        var surface = Render<TasksSurface>(host, TasksSurfaceQuery.ForEntry(mine.Id!.Value.ToString()));

        surface.WaitForAssertion(() => Assert.Same(mine, host.State.SelectedRow));
        Assert.Equal(["backlog"], host.State.SelectedRepositoryAliases);
    }

    [Theory]
    [InlineData("8d5b1c3e-0000-4000-8000-000000000000")]
    [InlineData("not-an-id")]
    public async Task An_entry_the_backlog_does_not_hold_opens_tasks_with_a_toast(string entry)
    {
        using var host = await CreateHostAsync();
        await host.WriteEntryAsync("# Provision the box\n`task`\n");
        await host.State.SelectAsync(null);

        var surface = Render<TasksSurface>(host, TasksSurfaceQuery.ForEntry(entry));

        surface.WaitForAssertion(() =>
        {
            var toast = Assert.Single(host.Toasts.Visible, message => message.TestId == TasksSurface.EntryMissingTestId);
            Assert.Equal($"The entry {entry} is not in this backlog.", toast.Message);
        });
        Assert.Null(host.State.SelectedRow);
        Assert.NotEmpty(surface.FindAll("[data-testid='entry-list']"));
    }

    /// <summary>An entry asked for before the window's list has loaded — a popped-out
    /// window loads it as it opens — waits for the load and is followed once it lands.</summary>
    [Fact]
    public async Task An_entry_asked_before_the_list_has_loaded_is_followed_once_it_has()
    {
        using var host = await CreateHostAsync();
        var wanted = await host.WriteEntryAsync("# Provision the box\n`task`\n");
        await host.State.SelectAsync(null);

        // A second window's state over the same backlog, which nothing has loaded.
        var root = Path.GetDirectoryName(host.StorageRoot)!;
        var store = new WorkspaceSettingsStore(root, Path.Combine(root, "settings.json"));
        var fresh = TasksTestHost.StateFor(store, host.GitHub, copilot: null, toasts: host.Toasts);
        Assert.False(fresh.HasLoaded);
        host.Context.Services.AddSingleton(fresh);

        var surface = host.Context.Render<TasksSurface>(parameters => parameters
            .Add(component => component.Query, TasksSurfaceQuery.ForEntry(wanted.Id!.Value.ToString())));

        Assert.Null(fresh.SelectedRow);

        await surface.InvokeAsync(fresh.InitializeAsync);

        surface.WaitForAssertion(() => Assert.Equal(wanted.Id, fresh.SelectedRow?.Id));
    }

    private static async Task<TasksPaneHost> CreateHostAsync()
    {
        var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs");
        host.Context.Services.AddScoped<ISurfaceNavigator, SurfaceNavigator>();
        return host;
    }

    private static IRenderedComponent<TSurface> Render<TSurface>(TasksPaneHost host, IReadOnlyDictionary<string, string> query)
        where TSurface : TasksSurface =>
        host.Context.Render<TSurface>(parameters => parameters.Add(component => component.Query, query));
}
