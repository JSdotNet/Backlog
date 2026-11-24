using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// What the shell remembers on this device follows a relabelled repository (local
/// ADR 0060 §3, local stores): the header's repository scope, the atlas's scope and the
/// surface in their <c>repo:</c> and <c>ghost:</c> forms, and the roadmap bands folded by
/// alias. Each device re-keys its own copy, so the store is local.
/// </summary>
public sealed class ShellNavigationAliasMoveTests : IDisposable
{
    private readonly TemporaryDirectory _temp = new("shell-navigation-alias-move-tests");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string ShellPath => _temp.Combine(ShellNavigationStore.FileName);

    private ShellNavigationStore Store() => new(ShellPath);

    private static IRepositoryAliasKeyedStore Port(ShellNavigationStore store) => store;

    public void Dispose() => _temp.Dispose();

    private static Dictionary<string, string> Moving(params (string From, string To)[] moves) =>
        moves.ToDictionary(move => move.From, move => move.To, StringComparer.Ordinal);

    /// <summary>A reopened store, once every queued write has landed.</summary>
    private ShellNavigationStore Reopened(ShellNavigationStore store)
    {
        store.Flush();
        return Store();
    }

    [Fact]
    public void The_store_stays_on_this_device() =>
        Assert.Equal(AliasKeyedStoreKind.Local, Port(Store()).Kind);

    [Fact]
    public void It_holds_the_aliases_the_scope_the_atlas_and_the_folded_bands_name()
    {
        var store = Store();
        store.SetRepositoryScope(["backlog", "", "Devbook"]);
        store.SetAtlasPlace(null, "ghost:homelab");
        store.SetRoadmapCollapsedGroups(["specmanager"]);

        Assert.Equal(
            ["backlog", "devbook", "homelab", "specmanager"],
            Port(store).HeldAliases().Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_repository_scope_takes_the_new_alias_in_its_place_and_keeps_the_brain()
    {
        var store = Store();
        store.SetRepositoryScope(["devbook", "project-guidelines-mcp", ""]);
        var changes = 0;
        store.Changed += () => changes++;

        var moved = await Port(store).MoveAliasesAsync(Moving(("project-guidelines-mcp", "app-defaults")), Cancellation);

        Assert.True(moved);
        Assert.Equal(["devbook", "app-defaults", ""], store.RepositoryScope);
        Assert.Equal(["devbook", "app-defaults", ""], Reopened(store).RepositoryScope);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task A_scope_that_already_named_the_new_alias_names_it_once()
    {
        var store = Store();
        store.SetRepositoryScope(["backlog", "devbook", "backlog-old"]);

        await Port(store).MoveAliasesAsync(Moving(("backlog-old", "backlog")), Cancellation);

        Assert.Equal(["backlog", "devbook"], store.RepositoryScope);
    }

    /// <summary>Both entries name the same repository after the relabel, so the list
    /// keeps the first place either held — here the old alias's, the anchor.</summary>
    [Fact]
    public async Task A_scope_that_named_the_new_alias_after_the_old_one_keeps_the_old_one_place()
    {
        var store = Store();
        store.SetRepositoryScope(["backlog-old", "devbook", "backlog"]);

        await Port(store).MoveAliasesAsync(Moving(("backlog-old", "backlog")), Cancellation);

        Assert.Equal(["backlog", "devbook"], store.RepositoryScope);
    }

    /// <summary>The store serializes its changes behind a gate, and announces them after
    /// leaving it: a handler that sets a choice from another thread and waits for it
    /// must not deadlock against the re-key that raised it.</summary>
    [Fact]
    public async Task A_change_is_announced_outside_the_store_gate()
    {
        var store = Store();
        store.SetRepositoryScope(["project-guidelines-mcp"]);
        var setElsewhere = false;
        store.Changed += () =>
        {
            if (store.BoardColumns == "Priority") return;
            setElsewhere = Task.Run(() => store.SetBoardColumns("Priority")).Wait(TimeSpan.FromSeconds(10));
        };

        await Task.Run(
            () => Port(store).MoveAliasesAsync(Moving(("project-guidelines-mcp", "app-defaults")), Cancellation),
            Cancellation);

        Assert.True(setElsewhere);
        Assert.Equal(["app-defaults"], store.RepositoryScope);
        Assert.Equal("Priority", Reopened(store).BoardColumns);
    }

    [Fact]
    public async Task The_scope_is_matched_without_regard_to_case()
    {
        var store = Store();
        store.SetRepositoryScope(["Project-Guidelines-MCP"]);

        var moved = await Port(store).MoveAliasesAsync(Moving(("project-guidelines-mcp", "app-defaults")), Cancellation);

        Assert.True(moved);
        Assert.Equal(["app-defaults"], store.RepositoryScope);
    }

    [Theory]
    [InlineData("repo:project-guidelines-mcp", "repo:app-defaults")]
    [InlineData("ghost:project-guidelines-mcp", "ghost:app-defaults")]
    public async Task The_atlas_scope_follows_in_its_repo_and_ghost_forms(string before, string after)
    {
        var store = Store();
        store.SetAtlasPlace("inbox", before);

        var moved = await Port(store).MoveAliasesAsync(Moving(("project-guidelines-mcp", "app-defaults")), Cancellation);

        Assert.True(moved);
        Assert.Equal(after, store.AtlasScope);
        Assert.Equal("inbox", store.AtlasReturnSurface);
        Assert.Equal(after, Reopened(store).AtlasScope);
    }

    [Theory]
    [InlineData("repo:project-guidelines-mcp", "repo:app-defaults")]
    [InlineData("ghost:project-guidelines-mcp", "ghost:app-defaults")]
    public async Task The_last_surface_follows_in_its_repo_and_ghost_forms(string before, string after)
    {
        var store = Store();
        store.SetLastSurface(before);

        await Port(store).MoveAliasesAsync(Moving(("project-guidelines-mcp", "app-defaults")), Cancellation);

        Assert.Equal(after, store.LastSurface);
        Assert.Equal(after, Reopened(store).LastSurface);
    }

    [Fact]
    public async Task A_surface_or_scope_that_names_no_repository_stays()
    {
        var store = Store();
        store.SetLastSurface("tools");
        store.SetAtlasPlace(null, "brain");

        var moved = await Port(store).MoveAliasesAsync(Moving(("tools", "app-defaults"), ("brain", "app-defaults")), Cancellation);

        Assert.False(moved);
        Assert.Equal("tools", store.LastSurface);
        Assert.Equal("brain", store.AtlasScope);
    }

    [Fact]
    public async Task A_folded_band_stays_folded_under_the_new_alias()
    {
        var store = Store();
        store.SetRoadmapCollapsedGroups(["no-repository", "project-guidelines-mcp"]);

        await Port(store).MoveAliasesAsync(Moving(("project-guidelines-mcp", "app-defaults")), Cancellation);

        Assert.Equal(["app-defaults", "no-repository"], store.RoadmapCollapsedGroups);
        Assert.Equal(["app-defaults", "no-repository"], Reopened(store).RoadmapCollapsedGroups);
    }

    [Fact]
    public async Task A_folded_band_already_under_the_new_alias_is_kept_once()
    {
        var store = Store();
        store.SetRoadmapCollapsedGroups(["backlog", "backlog-old"]);

        await Port(store).MoveAliasesAsync(Moving(("backlog-old", "backlog")), Cancellation);

        Assert.Equal(["backlog"], store.RoadmapCollapsedGroups);
    }

    [Fact]
    public async Task A_swap_exchanges_the_two_aliases_rather_than_running_one_move_into_the_other()
    {
        var store = Store();
        store.SetRepositoryScope(["alpha", "beta"]);
        store.SetAtlasPlace(null, "repo:alpha");
        store.SetLastSurface("ghost:beta");
        store.SetRoadmapCollapsedGroups(["alpha"]);

        var moved = await Port(store).MoveAliasesAsync(Moving(("alpha", "beta"), ("beta", "alpha")), Cancellation);

        Assert.True(moved);
        Assert.Equal(["beta", "alpha"], store.RepositoryScope);
        Assert.Equal("repo:beta", store.AtlasScope);
        Assert.Equal("ghost:alpha", store.LastSurface);
        Assert.Equal(["beta"], store.RoadmapCollapsedGroups);
    }

    [Fact]
    public async Task A_mapping_that_names_no_held_alias_changes_nothing_and_writes_nothing()
    {
        var store = Store();
        store.SetRepositoryScope(["backlog"]);
        store.Flush();
        var bytes = File.ReadAllBytes(ShellPath);
        var changes = 0;
        store.Changed += () => changes++;

        var moved = await Port(store).MoveAliasesAsync(Moving(("devbook", "devbook-stack")), Cancellation);

        Assert.False(moved);
        Assert.Equal(0, changes);
        store.Flush();
        Assert.Equal(bytes, File.ReadAllBytes(ShellPath));
    }

    [Fact]
    public async Task A_second_call_with_the_same_mapping_changes_nothing()
    {
        var store = Store();
        store.SetRepositoryScope(["project-guidelines-mcp"]);
        store.SetAtlasPlace(null, "repo:project-guidelines-mcp");
        store.SetRoadmapCollapsedGroups(["project-guidelines-mcp"]);
        var mapping = Moving(("project-guidelines-mcp", "app-defaults"));

        Assert.True(await Port(store).MoveAliasesAsync(mapping, Cancellation));
        Assert.False(await Port(store).MoveAliasesAsync(mapping, Cancellation));
        Assert.Equal(["app-defaults"], store.RepositoryScope);
    }
}
