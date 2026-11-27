using Backlog.Infrastructure.GitHub.Repositories;
using Backlog.Modules.Repositories.Abstractions.Environments;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The registry's named environments as this device keeps them: in
/// <c>environments.json</c> beside <c>github.json</c>, keyed on the repository's
/// <c>owner/name</c> and read through the registry's renames.
/// </summary>
public sealed class EnvironmentsFileStoreTests : IDisposable
{
    private readonly TemporaryDirectory _temp = new("environments-file-store-tests");

    private string LocalPath => _temp.Combine("local", "github.json");

    private GitHubSettingsStore Registry() => new(LocalPath, () => _temp.Combine("workspace"));

    public void Dispose() => _temp.Dispose();

    private static StoredEnvironment Stored(EnvironmentHome home, string name, LaunchLink link, params string[] hints) =>
        new(home, new RepositoryEnvironment(Guid.NewGuid(), name, EnvironmentType.Staging, link, [.. hints.Select(h => new AccessHint(h))], Pinned: true));

    [Fact]
    public void The_file_sits_beside_the_registrys_per_device_file()
    {
        var store = new EnvironmentsFileStore(Registry());

        Assert.Equal(_temp.Combine("local", EnvironmentsFileStore.FileName), store.FilePath);
    }

    [Fact]
    public void Nothing_is_kept_at_first()
    {
        Assert.Empty(new EnvironmentsFileStore(Registry()).Load()!);
    }

    [Fact]
    public void Every_environment_and_its_home_survive_a_restart()
    {
        var registry = Registry();
        var written = new[]
        {
            Stored(EnvironmentHome.Repository("JSdotNet/Backlog"), "Staging", new LaunchLink(LaunchLinkKind.Url, "https://staging.example.com"), "Key Vault kv-backlog"),
            Stored(EnvironmentHome.NoRepository, "Portal", new LaunchLink(LaunchLinkKind.ResourceId, "/subscriptions/0000")),
            Stored(EnvironmentHome.Repository("JSdotNet/Backlog"), "Local", new LaunchLink(LaunchLinkKind.WorkspacePath, "src"))
        };

        Assert.Null(new EnvironmentsFileStore(registry).Save(written));
        var read = new EnvironmentsFileStore(registry).Load()!;

        Assert.Equal(written.Length, read.Count);
        for (var i = 0; i < written.Length; i++)
        {
            Assert.True(written[i].Home.Is(read[i].Home));
            Assert.Equal(written[i].Environment with { AccessHints = [] }, read[i].Environment with { AccessHints = [] });
            Assert.Equal(written[i].Environment.AccessHints, read[i].Environment.AccessHints);
        }
    }

    [Fact]
    public void A_renamed_repository_keeps_its_environments()
    {
        var registry = Registry();
        Assert.Null(registry.SetRepositories([new GitHubRepositoryRef("almanac", "JSdotNet", "Almanac")]));
        var store = new EnvironmentsFileStore(registry);
        store.Save([Stored(EnvironmentHome.Repository("JSdotNet/Almanac"), "Staging", new LaunchLink(LaunchLinkKind.Url, "https://staging.example.com"))]);
        var announced = 0;
        store.Changed += () => announced++;

        Assert.Null(registry.RenameRepository("almanac", "JSdotNet/Almanac2", out _));

        Assert.True(announced > 0);
        Assert.Equal("JSdotNet/Almanac2", Assert.Single(store.Load()!).Home.RepositoryId);
    }

    [Fact]
    public void A_corrupt_file_reads_as_unreadable_rather_than_empty()
    {
        var store = new EnvironmentsFileStore(Registry());
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, "{ not json");

        Assert.Null(store.Load());
    }

    [Fact]
    public void A_row_with_a_type_this_build_does_not_know_is_dropped_alone()
    {
        var store = new EnvironmentsFileStore(Registry());
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, """
            { "environments": [
              { "id": "6f1d2c1e-0000-0000-0000-000000000001", "name": "Future", "type": "quantum", "linkKind": "url", "link": "https://a.example.com" },
              { "id": "6f1d2c1e-0000-0000-0000-000000000002", "name": "Local", "type": "local", "linkKind": "workspace-path", "link": "src" }
            ] }
            """);

        var stored = Assert.Single(store.Load()!);
        Assert.Equal("Local", stored.Environment.Name);
        Assert.Equal(LaunchLinkKind.WorkspacePath, stored.Environment.Link.Kind);
        Assert.True(stored.Home.IsNoRepository);
    }
}
