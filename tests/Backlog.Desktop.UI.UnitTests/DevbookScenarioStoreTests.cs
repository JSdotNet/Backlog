using Backlog.Infrastructure.Devbook.Scenarios;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The scenario catalog of a repository: which pages are scenarios — from the
/// committed register when there is one, from the pages themselves when not — and
/// each page's run and state.
/// </summary>
public sealed class DevbookScenarioStoreTests : IDisposable
{
    private const string Page = "# Place an order\n\n```meta\ntype: scenario\n```\n\n## The cart is filled\n- **Given** an empty cart\n\n![The cart](shot:cart-filled)\n";

    private readonly List<string> _tempDirs = [];

    [Fact]
    public async Task Pages_that_say_type_scenario_are_found_without_a_register()
    {
        var repo = Repository();
        WritePage(repo, "orders/place-an-order.md", Page);
        WritePage(repo, "orders/features.md", "# Features\n\n```meta\ntype: features\n```\n\nNo scenario here.\n");

        var catalog = await Store(repo).LoadAsync("backlog", TestContext.Current.CancellationToken);

        var scenario = Assert.Single(catalog.Scenarios);
        Assert.False(catalog.FromRegister);
        Assert.Equal("place-an-order", scenario.Stem);
        Assert.Equal("orders/place-an-order.md", scenario.DomainPath);
        Assert.Equal(ScenarioState.NeverRun, scenario.State);
        Assert.Same(scenario, catalog.ForPath(".domain/orders/place-an-order.md"));
        Assert.Same(scenario, catalog.ForPath("orders/place-an-order.md"));
        Assert.Null(catalog.ForPath(".domain/orders/features.md"));
    }

    [Fact]
    public async Task A_committed_register_names_the_pages()
    {
        var repo = Repository();
        WritePage(repo, "orders/place-an-order.md", Page);
        WritePage(repo, "orders/not-listed.md", Page.Replace("Place an order", "Not listed", StringComparison.Ordinal));
        Directory.CreateDirectory(Path.Combine(repo, ".domain", "_meta"));
        File.WriteAllText(Path.Combine(repo, ".domain", "_meta", "scenarios.json"), """
            { "schemaVersion": 1, "scope": ".domain", "scenarios": [
              { "stem": "place-an-order", "path": ".domain/orders/place-an-order.md", "title": "Place an order", "status": null }
            ] }
            """);

        var catalog = await Store(repo).LoadAsync("backlog", TestContext.Current.CancellationToken);

        Assert.True(catalog.FromRegister);
        Assert.Equal(["place-an-order"], catalog.Scenarios.Select(scenario => scenario.Stem));
    }

    [Fact]
    public async Task A_run_whose_signature_is_the_page_s_passes_and_a_version_1_run_is_stale()
    {
        var repo = Repository();
        WritePage(repo, "orders/place-an-order.md", Page);
        var signature = ScenarioSignature.Of(ScenarioPageParser.Parse(Page, "x.md"));
        WriteRun(repo, "place-an-order", $$"""
            { "version": 2, "signature": "{{signature}}", "ranAt": "2026-10-07T08:30:00Z",
              "parts": [ { "title": "The cart is filled", "anchor": "the-cart-is-filled", "outcome": "passed" } ],
              "shots": { "cart-filled": { "file": "cart-filled.png", "part": "the-cart-is-filled", "ranAt": "2026-10-07T08:30:00Z" } } }
            """);

        var store = Store(repo);
        var catalog = await store.LoadAsync("backlog", TestContext.Current.CancellationToken);

        var scenario = Assert.Single(catalog.Scenarios);
        Assert.Equal(ScenarioState.Passed, scenario.State);
        var shot = catalog.Resolve("scenario:place-an-order#cart-filled", null);
        Assert.NotNull(shot);
        Assert.Equal(DevbookScenarioShotKind.Shown, shot.Kind);
        Assert.Equal(DevbookScenarioShotKind.Shown, catalog.Resolve("shot:cart-filled", ".domain/orders/place-an-order.md")!.Kind);
        Assert.Null(catalog.Resolve("diagram.png", ".domain/orders/place-an-order.md"));

        WriteRun(repo, "place-an-order", """
            { "versie": 1, "feature": "Place an order", "gedraaidOp": "2026-09-01T10:00:00Z",
              "scenarios": [ { "titel": "The cart is filled", "slug": "the-cart-is-filled", "uitkomst": "geslaagd", "duurMs": 10, "afdrukken": [] } ] }
            """);
        store.Invalidate();

        Assert.Equal(ScenarioState.Stale, Assert.Single((await store.LoadAsync("backlog", TestContext.Current.CancellationToken)).Scenarios).State);
    }

    [Fact]
    public async Task Two_pages_with_one_stem_make_a_reference_to_it_ambiguous()
    {
        var repo = Repository();
        WritePage(repo, "orders/place-an-order.md", Page);
        WritePage(repo, "billing/place-an-order.md", Page);

        var catalog = await Store(repo).LoadAsync("backlog", TestContext.Current.CancellationToken);

        Assert.Equal(2, catalog.Scenarios.Count);
        Assert.All(catalog.Scenarios, scenario => Assert.True(scenario.SharesStem));
        var resolution = catalog.Resolve("scenario:place-an-order#cart-filled", null);
        Assert.Equal(DevbookScenarioShotKind.AmbiguousStem, resolution!.Kind);
        Assert.Equal(2, resolution.Alternatives.Count);
    }

    /// <summary>A run is committed text: a screenshot it names outside its own folder,
    /// or one that is not an image, is no screenshot, and nothing is read for it.</summary>
    [Theory]
    [InlineData("../../../outside.png")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("notes.txt")]
    public async Task A_run_cannot_point_a_screenshot_outside_its_folder(string file)
    {
        var repo = Repository();
        WritePage(repo, "orders/place-an-order.md", Page);
        var signature = ScenarioSignature.Of(ScenarioPageParser.Parse(Page, "x.md"));
        WriteRun(repo, "place-an-order", $$"""
            { "version": 2, "signature": "{{signature}}",
              "parts": [ { "title": "The cart is filled", "anchor": "the-cart-is-filled", "outcome": "failed", "failureShot": "{{file}}" } ],
              "shots": { "cart-filled": { "file": "{{file}}", "part": "the-cart-is-filled" } } }
            """);

        var catalog = await Store(repo).LoadAsync("backlog", TestContext.Current.CancellationToken);

        var scenario = Assert.Single(catalog.Scenarios);
        Assert.Null(scenario.ShotFile("cart-filled"));
        Assert.Null(scenario.FailureFile("the-cart-is-filled"));
        Assert.Equal(DevbookScenarioShotKind.NotCaptured, catalog.Resolve("scenario:place-an-order#cart-filled", null)!.Kind);
    }

    /// <summary>A run that names another page — two pages sharing a stem share a run
    /// folder — is not this page's run.</summary>
    [Fact]
    public async Task A_run_of_another_page_with_the_same_stem_is_not_this_page_s()
    {
        var repo = Repository();
        WritePage(repo, "orders/place-an-order.md", Page);
        WriteRun(repo, "place-an-order", """
            { "version": 2, "page": ".domain/billing/place-an-order.md", "signature": "00000000", "parts": [], "shots": {} }
            """);

        var catalog = await Store(repo).LoadAsync("backlog", TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(catalog.Scenarios).Run);
    }

    private DevbookScenarioStore Store(string repo)
    {
        var settings = new GitHubSettingsStore(Path.Combine(TempDir(), "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        settings.SetRepositories(repositories);
        settings.SetCloneDirectory("backlog", repo);
        return new DevbookScenarioStore(new DevbookFolderSource(settings));
    }

    private string Repository()
    {
        var repo = TempDir();
        Directory.CreateDirectory(Path.Combine(repo, ".domain"));
        File.WriteAllText(Path.Combine(repo, ".domain", "context-map.md"), "# Context map\n\n```meta\ntype: context-map\n```\n");
        return repo;
    }

    private static void WritePage(string repo, string relative, string text)
    {
        var path = Path.Combine(repo, ".domain", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static void WriteRun(string repo, string stem, string json)
    {
        var folder = Path.Combine(repo, ".devbook", "scenarios", stem);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "run.json"), json);
        File.WriteAllBytes(Path.Combine(folder, "cart-filled.png"), [0x89, 0x50]);
    }

    private string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "devbook-scenario-store-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        _tempDirs.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
