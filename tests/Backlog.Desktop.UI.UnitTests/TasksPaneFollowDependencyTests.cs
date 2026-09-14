using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Following a "waiting for" name off a blocked row to the entry it names.
/// <para>
/// The list can only take the reader to a row it draws. When a scope has hidden
/// the entry — the repository picker, a status chip, a tag, My Day — the pane is
/// the one that can bring it back, and it does that by widening exactly the
/// scopes that hide it and no other: the reader asked to see one entry, not to
/// lose the filter they were working in. Once it is in view it is opened and
/// focused, the same as if it had been clicked.
/// </para>
/// <para>
/// The alternative — opening the hidden entry in the detail pane while the list
/// goes on not showing it — was what shipped first, and it read as the pane
/// having opened something unrelated: an entry beside a list that does not
/// contain it. The store's own rule is that selection follows the list, so a
/// selected row the list hides is a state it never meant to be in.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class TasksPaneFollowDependencyTests
{
    private static string RowTestId(EntryRow row) => $"entry-list-{row.TaskId}";

    private static string DependencyLink(EntryRow row) => $"[data-testid='{RowTestId(row)}'] .task-item__dependency";

    private static IReadOnlyList<string?> Focused(TasksPaneHost host) =>
        [.. host.Context.JSInterop.Invocations["backlogFocus"].Select(call => call.Arguments[0] as string)];

    [Fact]
    public async Task Following_a_dependency_the_repository_scope_hides_widens_the_scope_and_opens_it()
    {
        using var host = await TasksPaneHost.CreateAsync(
            "repox = JSdotNet/RepoX",
            "repoy = JSdotNet/RepoY");

        var elsewhere = await host.WriteEntryAsync("# Ship the library\n`task` `!ready` `repo:repoy`\n");
        var waiting = await host.WriteEntryAsync(
            $"# Ship the app\n`task` `!ready` `repo:repox` `after:{elsewhere.TaskId}`\n");

        host.State.SetRepositoryFilter("repox");
        await host.State.SelectAsync(null);

        var pane = host.Render();

        // The precondition: the step is out of view, and the row still offers a
        // way to it because the pane opens rows.
        Assert.DoesNotContain(elsewhere, host.State.FilteredRows);
        var link = pane.Find(DependencyLink(waiting));
        Assert.Equal("Go to Ship the library", link.GetAttribute("aria-label"));

        await link.ClickAsync(new MouseEventArgs());

        // The scope that hid it is gone, the row is in the list, and it is open.
        Assert.Equal(string.Empty, host.State.SelectedRepositoryAlias);
        Assert.Contains(host.State.FilteredRows, row => row.TaskId == elsewhere.TaskId);
        Assert.Equal(elsewhere.TaskId, host.State.SelectedRow?.TaskId);
        Assert.NotNull(pane.Find($"[data-testid='{RowTestId(elsewhere)}']"));

        // And the focus went to the row now that there is one, so the reader is
        // looking at it rather than at the link they pressed.
        Assert.Contains(Focused(host), id => id is not null && id.EndsWith(elsewhere.TaskId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Only_the_scopes_that_hide_the_entry_are_widened()
    {
        using var host = await TasksPaneHost.CreateAsync();

        // Both wear #sync; only the status differs. A status chip hides the step,
        // the tag chip does not, and the tag chip is the one that must survive.
        var elsewhere = await host.WriteEntryAsync("# Provision the box\n`task` `!draft` `#sync`\n");
        var waiting = await host.WriteEntryAsync(
            $"# Deploy it\n`task` `!ready` `#sync` `after:{elsewhere.TaskId}`\n");

        host.State.SetStatusFilter("ready");
        host.State.ToggleTagFilter("sync");
        await host.State.SelectAsync(null);

        var pane = host.Render();

        Assert.DoesNotContain(elsewhere, host.State.FilteredRows);

        await pane.Find(DependencyLink(waiting)).ClickAsync(new MouseEventArgs());

        Assert.Equal(string.Empty, host.State.SelectedStatusFilterWire);
        Assert.True(host.State.IsTagSelected("sync"));
        Assert.Equal(elsewhere.TaskId, host.State.SelectedRow?.TaskId);
    }

    [Fact]
    public async Task Every_scope_hiding_the_entry_is_widened_at_once()
    {
        using var host = await TasksPaneHost.CreateAsync("repox = JSdotNet/RepoX");

        var elsewhere = await host.WriteEntryAsync("# Write the runbook\n`task` `!draft`\n");
        var waiting = await host.WriteEntryAsync(
            $"# Publish it\n`task` `!ready` `repo:repox` `#docs` `after:{elsewhere.TaskId}`\n");

        host.State.SetRepositoryFilter("repox");
        host.State.SetStatusFilter("ready");
        host.State.ToggleTagFilter("docs");
        host.State.SetMyDayFilter(null);
        await host.State.SelectAsync(null);

        var pane = host.Render();

        await pane.Find(DependencyLink(waiting)).ClickAsync(new MouseEventArgs());

        Assert.Equal(string.Empty, host.State.SelectedRepositoryAlias);
        Assert.Equal(string.Empty, host.State.SelectedStatusFilterWire);
        Assert.Empty(host.State.SelectedTags);
        Assert.Equal(elsewhere.TaskId, host.State.SelectedRow?.TaskId);
        Assert.Contains(host.State.FilteredRows, row => row.TaskId == elsewhere.TaskId);
    }

    [Fact]
    public async Task Following_a_dependency_already_in_view_touches_no_scope()
    {
        using var host = await TasksPaneHost.CreateAsync();

        var first = await host.WriteEntryAsync("# Provision the box\n`task` `!ready` `#sync`\n");
        var waiting = await host.WriteEntryAsync(
            $"# Deploy it\n`task` `!ready` `#sync` `after:{first.TaskId}`\n");

        host.State.SetStatusFilter("ready");
        host.State.ToggleTagFilter("sync");
        await host.State.SelectAsync(null);

        var pane = host.Render();

        Assert.Contains(first, host.State.FilteredRows);

        await pane.Find(DependencyLink(waiting)).ClickAsync(new MouseEventArgs());

        Assert.Equal("ready", host.State.SelectedStatusFilterWire);
        Assert.True(host.State.IsTagSelected("sync"));
        Assert.Equal(first.TaskId, host.State.SelectedRow?.TaskId);
    }

    /// <summary>The focus the pane moves onto the revealed row is its own doing,
    /// and the focusout it raises must not be read as the reader leaving the entry
    /// that was just opened for them. The check the guard asks is answered "yes,
    /// outside the detail pane" here — the row is outside it — and the selection
    /// has to survive that answer while the move is still in flight.</summary>
    [Fact]
    public async Task The_focus_move_onto_the_revealed_row_does_not_close_the_pane()
    {
        using var host = await TasksPaneHost.CreateAsync("repox = JSdotNet/RepoX", "repoy = JSdotNet/RepoY");

        var elsewhere = await host.WriteEntryAsync("# Ship the library\n`task` `!ready` `repo:repoy`\n");
        var waiting = await host.WriteEntryAsync(
            $"# Ship the app\n`task` `!ready` `repo:repox` `after:{elsewhere.TaskId}`\n");

        host.State.SetRepositoryFilter("repox");
        await host.State.SelectAsync(null);

        // The focus move is left in flight: the focusout it raises reaches the
        // pane before the move reports back, which is the order the browser has.
        var focus = host.Context.JSInterop.SetupVoid("backlogFocus", _ => true);
        host.Context.JSInterop.Setup<bool>("backlogFocusOutside", _ => true).SetResult(true);

        var pane = host.Render();

        await pane.Find(DependencyLink(waiting)).ClickAsync(new MouseEventArgs());

        Assert.Contains(Focused(host), id => id is not null && id.EndsWith(elsewhere.TaskId, StringComparison.Ordinal));

        await pane.Find("[data-testid='backlog-pane']").TriggerEventAsync("onfocusout", new FocusEventArgs());

        Assert.Equal(elsewhere.TaskId, host.State.SelectedRow?.TaskId);

        focus.SetVoidResult();
    }
}
