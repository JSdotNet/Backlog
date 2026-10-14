using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What an entry waits on is read once per change rather than once per row, and
/// these are the changes that have to reach the next read: a step's <c>id:</c>
/// typed but not yet saved, and a step that did not exist when the chain was
/// last read. Either one left unseen would show a slug where the step's title
/// belongs until something else happened to refresh the list.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class ResolvedDependsOnFreshnessTests
{
    private const string Waiting = "# First deployment\n`task` `after:sync-tasks`\n\nDeploy it.\n";

    [Fact]
    public async Task A_local_id_typed_into_a_step_names_it_before_the_step_is_saved()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var waiting = await host.WriteEntryAsync(Waiting);
        var step = await host.WriteEntryAsync("# Task sync endpoints\n`task`\n\nShip it.\n");
        Assert.Equal(["sync-tasks"], host.State.ResolvedDependsOn(waiting));

        host.State.OnRawTextInput(step, "# Task sync endpoints\n`task` `id:sync-tasks`\n\nShip it.\n");

        Assert.Equal([step.Id!.Value.ToString()], host.State.ResolvedDependsOn(waiting));
    }

    [Fact]
    public async Task A_step_written_after_the_chain_was_read_is_found_on_the_next_read()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var waiting = await host.WriteEntryAsync(Waiting);
        Assert.Equal(["sync-tasks"], host.State.ResolvedDependsOn(waiting));

        var step = await host.WriteEntryAsync("# Task sync endpoints\n`task` `id:sync-tasks`\n\nShip it.\n");

        Assert.Equal([step.Id!.Value.ToString()], host.State.ResolvedDependsOn(waiting));
    }

    /// <summary>
    /// Two steps of one plan carry the same <c>id:</c>, and the first in the list is
    /// the one a chain names. Moving the second above the first keeps the list, its
    /// count and both texts as they were — and the chain names the one now first,
    /// already while the new order is on its way to the store, where a render can
    /// land.
    /// </summary>
    [Fact]
    public async Task Moving_a_same_named_step_above_the_other_moves_which_one_a_chain_names()
    {
        var calls = new TaskStoreCalls();
        using var host = await TasksPaneHost.CreateAsync(roadmapTags: null, [], storeCalls: calls);
        var first = await host.WriteEntryAsync("# Task sync endpoints\n`task` `id:sync-tasks`\n\nShip it.\n");
        var second = await host.WriteEntryAsync("# Task sync endpoints, again\n`task` `id:sync-tasks`\n\nShip it again.\n");
        var waiting = await host.WriteEntryAsync(Waiting);
        foreach (var row in new[] { first, second, waiting }) row.ImportPlanId = "go-live";
        Assert.Equal([first.Id!.Value.ToString()], host.State.ResolvedDependsOn(waiting));

        IReadOnlyList<string>? whileSaving = null;
        calls.Before = name =>
        {
            if (name == nameof(ITaskItems.ReorderAsync)) whileSaving = host.State.ResolvedDependsOn(waiting);
        };

        await host.State.MoveEntryAsync(second, first);

        Assert.Equal([second.Id!.Value.ToString()], whileSaving);
        Assert.Equal([second.Id!.Value.ToString()], host.State.ResolvedDependsOn(waiting));
    }

    [Fact]
    public async Task A_local_id_taken_off_a_step_stops_naming_it()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var step = await host.WriteEntryAsync("# Task sync endpoints\n`task` `id:sync-tasks`\n\nShip it.\n");
        var waiting = await host.WriteEntryAsync(Waiting);
        Assert.Equal([step.Id!.Value.ToString()], host.State.ResolvedDependsOn(waiting));

        host.State.OnRawTextInput(step, "# Task sync endpoints\n`task`\n\nShip it.\n");

        Assert.Equal(["sync-tasks"], host.State.ResolvedDependsOn(waiting));
    }
}
