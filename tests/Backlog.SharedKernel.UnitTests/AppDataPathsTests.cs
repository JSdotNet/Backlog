using Backlog.SharedKernel.Storage;

namespace Backlog.SharedKernel.UnitTests;

/// <summary>
/// The two per-user folders every settings store resolves its default file in. The
/// folder names are persisted locations, so what is pinned is that they are the ones the
/// stores used before the helper existed.
/// </summary>
public sealed class AppDataPathsTests
{
    private static readonly string LocalAppData =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    [Fact]
    public void Shared_is_the_Backlog_folder_in_every_build()
    {
        Assert.Equal("Backlog", AppDataPaths.SharedFolderName);
        Assert.Equal(Path.Combine(LocalAppData, "Backlog", "spend-budgets.json"), AppDataPaths.Shared("spend-budgets.json"));
    }

    [Fact]
    public void Head_is_this_builds_own_folder()
    {
#if DEBUG
        const string expected = "Backlog.Debug";
#else
        const string expected = "Backlog";
#endif
        Assert.Equal(expected, AppDataPaths.HeadFolderName);
        Assert.Equal(Path.Combine(LocalAppData, expected, "device.json"), AppDataPaths.Head("device.json"));
    }

    [Fact]
    public void No_parts_is_the_folder_itself()
    {
        Assert.Equal(Path.Combine(LocalAppData, AppDataPaths.SharedFolderName), AppDataPaths.Shared());
        Assert.Equal(Path.Combine(LocalAppData, AppDataPaths.HeadFolderName), AppDataPaths.Head());
    }

    [Fact]
    public void Several_parts_nest_under_the_folder()
    {
        Assert.Equal(
            Path.Combine(LocalAppData, AppDataPaths.SharedFolderName, "hooks", "ask.ps1"),
            AppDataPaths.Shared("hooks", "ask.ps1"));
    }
}
