using System.Text.Json;

using Backlog.Desktop.UI.Shell;
using Backlog.Modules.Work.UI.Tasks;
using Backlog.Infrastructure.FileSystem;

using Microsoft.AspNetCore.Components;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Tasks list's Plans / Flat switch is the window's own (local ADR 0072 §1, ADR 0043
/// §5): the main window keeps its choice in the shell's navigation file under
/// <c>tasksGrouping</c>, and a popped-out window opens on that choice and never writes
/// back.
/// </summary>
public sealed class ShellTasksViewPreferencesTests
{
    [Fact]
    public void A_flat_set_in_a_popped_out_window_leaves_the_main_window_on_plans()
    {
        using var file = new TempShellNavigationFile();
        var store = new ShellNavigationStore(file.Path);
        var main = new ShellTasksViewPreferences(store, new CurrentShellWindow());
        var popped = new ShellTasksViewPreferences(store, new CurrentShellWindow { Key = "tasks-1" });

        popped.SetListGrouping(TaskListGrouping.Flat);

        Assert.Equal(TaskListGrouping.Flat, popped.ListGrouping);
        Assert.Equal(TaskListGrouping.Plans, main.ListGrouping);
        Assert.Equal(TaskListGrouping.Plans, new ShellTasksViewPreferences(new ShellNavigationStore(file.Path)).ListGrouping);
    }

    [Fact]
    public void The_main_window_writes_its_choice_under_tasksGrouping_and_reads_it_back_at_the_next_start()
    {
        using var file = new TempShellNavigationFile();
        var main = new ShellTasksViewPreferences(new ShellNavigationStore(file.Path), new CurrentShellWindow());

        main.SetListGrouping(TaskListGrouping.Flat);

        using (var written = JsonDocument.Parse(ShellNavigationStoreTests.Written(file.Path)))
            Assert.Equal("Flat", written.RootElement.GetProperty("tasksGrouping").GetString());

        var restarted = new ShellTasksViewPreferences(new ShellNavigationStore(file.Path), new CurrentShellWindow());
        Assert.Equal(TaskListGrouping.Flat, restarted.ListGrouping);
    }

    [Fact]
    public void A_popped_out_window_opens_on_the_main_windows_remembered_choice()
    {
        using var file = new TempShellNavigationFile();
        var store = new ShellNavigationStore(file.Path);
        new ShellTasksViewPreferences(store, new CurrentShellWindow()).SetListGrouping(TaskListGrouping.Flat);

        var popped = new ShellTasksViewPreferences(store, new CurrentShellWindow { Key = "tasks-1" });

        Assert.Equal(TaskListGrouping.Flat, popped.ListGrouping);
    }

    /// <summary>The window's key is written as its root renders, which may follow the
    /// service's construction, so it is read when the grouping is used.</summary>
    [Fact]
    public void The_window_key_is_read_when_the_grouping_is_set_not_when_the_service_is_built()
    {
        using var file = new TempShellNavigationFile();
        var store = new ShellNavigationStore(file.Path);
        var window = new CurrentShellWindow();
        var popped = new ShellTasksViewPreferences(store, window);

        window.Key = "tasks-1";
        popped.SetListGrouping(TaskListGrouping.Flat);

        Assert.Equal(TaskListGrouping.Flat, popped.ListGrouping);
        Assert.Equal(TaskListGrouping.Plans, new ShellTasksViewPreferences(new ShellNavigationStore(file.Path)).ListGrouping);
    }
    /// <summary>The harness and the desktop app's served pages render a popped-out window
    /// at <c>/window/{surfaceId}</c> with no key, so the route alone says it is popped out.</summary>
    [Fact]
    public void A_window_on_the_window_route_never_writes_the_main_windows_choice_even_without_a_key()
    {
        using var file = new TempShellNavigationFile();
        var store = new ShellNavigationStore(file.Path);
        var popped = new ShellTasksViewPreferences(store, new CurrentShellWindow(), new FixedNavigation("window/tasks"));

        popped.SetListGrouping(TaskListGrouping.Flat);

        Assert.Equal(TaskListGrouping.Flat, popped.ListGrouping);
        Assert.Equal(TaskListGrouping.Plans, new ShellTasksViewPreferences(new ShellNavigationStore(file.Path)).ListGrouping);
    }

    [Fact]
    public void A_window_on_any_other_route_with_no_key_is_the_main_window()
    {
        using var file = new TempShellNavigationFile();
        var main = new ShellTasksViewPreferences(
            new ShellNavigationStore(file.Path), new CurrentShellWindow(), new FixedNavigation(string.Empty));

        main.SetListGrouping(TaskListGrouping.Flat);

        Assert.Equal(TaskListGrouping.Flat, new ShellTasksViewPreferences(new ShellNavigationStore(file.Path)).ListGrouping);
    }

    private sealed class FixedNavigation : NavigationManager
    {
        public FixedNavigation(string relativePath) => Initialize("http://localhost/", "http://localhost/" + relativePath);
    }
}
