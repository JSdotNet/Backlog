using Backlog.Infrastructure.Devbook;
using Backlog.Infrastructure.GitHub;
using Backlog.UI.Components.Devbook;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The knowledge folders default to the devbook layout — <c>.devbook/arc42</c>
/// rather than <c>.arc42</c> at the repository root — and a repository still on
/// the root layout keeps reading as it did, because an unconfigured folder falls
/// back to its legacy spot. Remark keys stay on the legacy spelling either way.
/// </summary>
public sealed class DevbookLayoutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-devbook-layout-tests", Guid.NewGuid().ToString("N"));

    public DevbookLayoutTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void An_unconfigured_folder_resolves_under_devbook()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".devbook", "arc42"));

        var location = Source().Resolve(".arc42", "backlog");

        Assert.True(location.Available);
        Assert.Equal(Path.Combine(_root, ".devbook", "arc42"), location.FullPath);
        Assert.Null(location.Folder!.ResolvedPath);
        Assert.Equal(".devbook/arc42", location.Folder.EffectivePath);
    }

    [Fact]
    public void The_devbook_folder_wins_when_both_layouts_are_present()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".devbook", "arc42"));
        Directory.CreateDirectory(Path.Combine(_root, ".arc42"));

        var location = Source().Resolve(".arc42", "backlog");

        Assert.Equal(Path.Combine(_root, ".devbook", "arc42"), location.FullPath);
    }

    [Fact]
    public void A_repository_on_the_root_layout_falls_back_to_the_legacy_folder()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".arc42"));

        var location = Source().Resolve(".arc42", "backlog");

        Assert.True(location.Available);
        Assert.Equal(Path.Combine(_root, ".arc42"), location.FullPath);
        Assert.Equal(".arc42", location.Folder!.ResolvedPath);
        Assert.Equal(".arc42", location.Folder.EffectivePath);
    }

    [Fact]
    public void A_folder_in_neither_layout_names_both_places_looked()
    {
        var location = Source().Resolve(".arc42", "backlog");

        Assert.False(location.Available);
        Assert.Contains(Path.Combine(_root, ".devbook", "arc42"), location.Message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(_root, ".arc42"), location.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_override_is_not_second_guessed_by_the_fallback()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".arc42"));
        var settings = Settings();
        settings.SetDevbookFolder("backlog", ".arc42", enabled: true, path: "docs/arch");

        var location = new DevbookFolderSource(settings).Resolve(".arc42", "backlog");

        Assert.False(location.Available);
        Assert.DoesNotContain(Path.Combine(_root, ".arc42") + " ", location.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Architecture_under_devbook_counts_decision_records_and_writes_status()
    {
        var arc42 = Path.Combine(_root, ".devbook", "arc42");
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

        // `deprecated` rather than `accepted`: under contract 16 `accepted` is a
        // decision rung, domain/'s alone, and the writer refuses it in arc42/.
        await knowledge.UpdateStatusAsync("backlog", document.Path, "deprecated", TestContext.Current.CancellationToken);

        Assert.Contains("status: deprecated", File.ReadAllText(Path.Combine(arc42, "adr", "0001-use-sqlite.md")), StringComparison.Ordinal);
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
    public void The_database_is_found_at_the_repository_root_above_devbook()
    {
        var arc42 = Path.Combine(_root, ".devbook", "arc42");
        Directory.CreateDirectory(arc42);
        Directory.CreateDirectory(Path.Combine(_root, "_meta"));
        var database = Path.Combine(_root, "_meta", DevbookDatabaseLocation.FileName);
        File.WriteAllText(database, string.Empty);

        Assert.Equal(database, DevbookDatabaseLocation.ForDevbookFolder(arc42));
    }

    private DevbookFolderSource Source() => new(Settings());

    private GitHubSettingsStore Settings()
    {
        var settings = new GitHubSettingsStore(Path.Combine(_root, "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        settings.SetRepositories(repositories);
        settings.SetCloneDirectory("backlog", _root);
        return settings;
    }
}
