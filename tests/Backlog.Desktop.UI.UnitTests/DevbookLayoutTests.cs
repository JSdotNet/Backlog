using Backlog.Infrastructure.Devbook;
using Backlog.Infrastructure.GitHub;
using Backlog.SharedKernel.Devbook;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The devbook folders default to the devbook layout — <c>.devbook/arc42</c>
/// rather than <c>.arc42</c> at the repository root — and a repository still on
/// the root layout keeps reading as it did, because an unconfigured folder falls
/// back to its legacy spot. Remark keys stay on the legacy spelling either way.
/// </summary>
public sealed class DevbookLayoutTests : IDisposable
{
    private readonly TemporaryDirectory _temp = new("backlog-devbook-layout-tests");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void An_unconfigured_folder_resolves_under_devbook()
    {
        Directory.CreateDirectory(_temp.Combine(".devbook", "arc42"));

        var location = Source().Resolve(".arc42", "backlog");

        Assert.True(location.Available);
        Assert.Equal(_temp.Combine(".devbook", "arc42"), location.FullPath);
        Assert.Null(location.Folder!.ResolvedPath);
        Assert.Equal(".devbook/arc42", location.Folder.EffectivePath);
    }

    [Fact]
    public void The_devbook_folder_wins_when_both_layouts_are_present()
    {
        Directory.CreateDirectory(_temp.Combine(".devbook", "arc42"));
        Directory.CreateDirectory(_temp.Combine(".arc42"));

        var location = Source().Resolve(".arc42", "backlog");

        Assert.Equal(_temp.Combine(".devbook", "arc42"), location.FullPath);
    }

    [Fact]
    public void A_repository_on_the_root_layout_falls_back_to_the_legacy_folder()
    {
        Directory.CreateDirectory(_temp.Combine(".arc42"));

        var location = Source().Resolve(".arc42", "backlog");

        Assert.True(location.Available);
        Assert.Equal(_temp.Combine(".arc42"), location.FullPath);
        Assert.Equal(".arc42", location.Folder!.ResolvedPath);
        Assert.Equal(".arc42", location.Folder.EffectivePath);
    }

    [Fact]
    public void A_folder_in_neither_layout_names_both_places_looked()
    {
        var location = Source().Resolve(".arc42", "backlog");

        Assert.False(location.Available);
        Assert.Contains(_temp.Combine(".devbook", "arc42"), location.Message, StringComparison.Ordinal);
        Assert.Contains(_temp.Combine(".arc42"), location.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_override_is_not_second_guessed_by_the_fallback()
    {
        Directory.CreateDirectory(_temp.Combine(".arc42"));
        var settings = Settings();
        settings.SetDevbookFolder("backlog", ".arc42", enabled: true, path: "docs/arch");

        var location = new DevbookFolderSource(settings).Resolve(".arc42", "backlog");

        Assert.False(location.Available);
        Assert.DoesNotContain(_temp.Combine(".arc42") + " ", location.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Architecture_under_devbook_counts_decision_records()
    {
        var arc42 = _temp.Combine(".devbook", "arc42");
        Directory.CreateDirectory(Path.Combine(arc42, "adr"));
        File.WriteAllText(Path.Combine(arc42, "adr", "0001-use-sqlite.md"), """
            # Use SQLite

            ```meta
            status: proposed
            ```

            Decision text.
            """);
        var knowledge = new Arc42DevbookStore(Source());

        var catalog = await knowledge.LoadAsync("backlog");
        var document = Assert.Single(catalog.Documents);
        Assert.Equal(".devbook/arc42/adr/0001-use-sqlite.md", document.Path);
        Assert.Equal(1, catalog.DecisionRecordCount);
    }

    /// <summary>Remarks already synced are keyed <c>.arc42/…</c>; a folder moving
    /// under <c>.devbook/</c> must not give the same chapter a second key.</summary>
    [Theory]
    [InlineData(".devbook/arc42/adr/0001.md", ".arc42/adr/0001.md")]
    [InlineData(".devbook/domain/tasks/domain.md", ".domain/tasks/domain.md")]
    [InlineData(".arc42/adr/0001.md", ".arc42/adr/0001.md")]
    public void Chapter_keys_stay_on_the_legacy_spelling(string path, string expected)
    {
        Assert.Equal(expected, DevbookChapterKey.Canonical(path));
        Assert.Equal(expected, DevbookChapterKey.Canonical(path, DevbookFolderSetting.Defaults()));
    }

    [Theory]
    [InlineData(".devbook/arc42/adr/0001.md", DevbookFolder.Arc42)]
    [InlineData(".devbook/tech/runtime.md", DevbookFolder.Tech)]
    [InlineData(".devbook/ai", DevbookFolder.Ai)]
    [InlineData(".tech/runtime.md", DevbookFolder.Tech)]
    public void A_path_in_either_layout_names_its_folder(string path, DevbookFolder expected)
    {
        Assert.Equal(expected, DevbookFolders.FromPath(path));
    }

    [Fact]
    public void The_database_is_keyed_to_the_repository_root_above_devbook()
    {
        var arc42 = _temp.Combine(".devbook", "arc42");
        Directory.CreateDirectory(arc42);

        Assert.Equal(DevbookDatabaseLocation.ForRepositoryRoot(_temp.Path), DevbookDatabaseLocation.ForDevbookFolder(arc42));
    }

    private DevbookFolderSource Source() => new(Settings());

    private GitHubSettingsStore Settings()
    {
        var settings = new GitHubSettingsStore(_temp.Combine("github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        settings.SetRepositories(repositories);
        settings.SetCloneDirectory("backlog", _temp.Path);
        return settings;
    }
}
