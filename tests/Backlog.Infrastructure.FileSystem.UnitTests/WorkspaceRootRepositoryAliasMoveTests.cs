using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The backup repository named in the workspace settings follows a relabelled
/// repository (local ADR 0060 §3, local stores): its alias moves, its owner and name
/// stay. The settings file is this device's own, so the store is local.
/// </summary>
public sealed class WorkspaceRootRepositoryAliasMoveTests : IDisposable
{
    private readonly TemporaryDirectory _temp = new("workspace-root-repository-alias-move-tests");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string SettingsFile => _temp.Combine("settings.json");

    private WorkspaceSettingsStore Store() => new(_temp.Path, SettingsFile, _ => null);

    private static IRepositoryAliasKeyedStore Port(WorkspaceSettingsStore store) => store;

    public void Dispose() => _temp.Dispose();

    private static Dictionary<string, string> Moving(params (string From, string To)[] moves) =>
        moves.ToDictionary(move => move.From, move => move.To, StringComparer.Ordinal);

    [Fact]
    public void The_store_stays_on_this_device() =>
        Assert.Equal(AliasKeyedStoreKind.Local, Port(Store()).Kind);

    [Fact]
    public void It_holds_the_backup_repository_alias_and_nothing_while_none_is_named()
    {
        Assert.Empty(Port(Store()).HeldAliases());

        var store = Store();
        store.TrySetRepository("JSdotNet/Project-Guidelines-MCP");

        Assert.Equal(["project-guidelines-mcp"], Port(store).HeldAliases());
    }

    [Fact]
    public async Task The_backup_repository_takes_the_new_alias_and_keeps_its_owner_and_name()
    {
        var store = Store();
        store.TrySetRepository("project-guidelines-mcp = JSdotNet/app-defaults");
        var backups = 0;
        store.BackupChanged += () => backups++;

        var moved = await Port(store).MoveAliasesAsync(Moving(("project-guidelines-mcp", "app-defaults")), Cancellation);

        Assert.True(moved);
        Assert.Equal("app-defaults", store.RootRepository!.Alias);
        Assert.Equal("JSdotNet", store.RootRepository.Owner);
        Assert.Equal("app-defaults", store.RootRepository.Name);
        Assert.Equal("app-defaults", Store().RootRepository!.Alias);
        // The repository backed up to is the same one, so the schedule is not re-armed.
        Assert.Equal(0, backups);
    }

    [Fact]
    public async Task A_backup_repository_the_mapping_does_not_name_stays_and_nothing_is_written()
    {
        var store = Store();
        store.TrySetRepository("JSdotNet/Notes");
        var bytes = File.ReadAllBytes(SettingsFile);

        var moved = await Port(store).MoveAliasesAsync(Moving(("backlog", "backlog-renamed")), Cancellation);

        Assert.False(moved);
        Assert.Equal("notes", store.RootRepository!.Alias);
        Assert.Equal(bytes, File.ReadAllBytes(SettingsFile));
    }

    [Fact]
    public async Task No_backup_repository_changes_nothing()
    {
        var moved = await Port(Store()).MoveAliasesAsync(Moving(("backlog", "backlog-renamed")), Cancellation);

        Assert.False(moved);
    }

    [Fact]
    public async Task A_swap_moves_the_backup_repository_once()
    {
        var store = Store();
        store.TrySetRepository("alpha = JSdotNet/Notes");

        await Port(store).MoveAliasesAsync(Moving(("alpha", "beta"), ("beta", "alpha")), Cancellation);

        Assert.Equal("beta", store.RootRepository!.Alias);
    }

    [Fact]
    public async Task A_second_call_with_the_same_mapping_changes_nothing()
    {
        var store = Store();
        store.TrySetRepository("project-guidelines-mcp = JSdotNet/app-defaults");
        var mapping = Moving(("project-guidelines-mcp", "app-defaults"));

        Assert.True(await Port(store).MoveAliasesAsync(mapping, Cancellation));
        Assert.False(await Port(store).MoveAliasesAsync(mapping, Cancellation));
        Assert.Equal("app-defaults", store.RootRepository!.Alias);
    }
}
