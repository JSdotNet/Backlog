using Backlog.Infrastructure.Devbook.Scenarios;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The scenario catalog of a repository: which journeys there are — from the
/// committed register when there is one, from the scenarios files themselves when
/// not — and each journey's run and state.
/// </summary>
public sealed class DevbookScenarioStoreTests : IDisposable
{
    private const string Journey =
        "## Place an order\n\n```meta\ntype: scenario\n```\n\n### The cart is filled\n- **Given** an empty cart\n\n![The cart](shot:cart-filled)\n";

    private const string Scenarios = "# Scenarios\n\n```meta\ntype: scenarios\n```\n\n" + Journey;

    private const string RunFolder = ".domain/orders/scenarios/place-an-order";

    private readonly List<string> _tempDirs = [];

    [Fact]
    public async Task Journeys_in_a_scenarios_file_are_found_without_a_register()
    {
        var repo = Repository();
        WritePage(repo, "orders/scenarios.md", Scenarios);
        WritePage(repo, "orders/features.md", "# Features\n\n```meta\ntype: features\n```\n\n## Not a journey\n\n```meta\ntype: scenario\n```\n");

        var catalog = await Store(repo).LoadAsync("backlog", TestContext.Current.CancellationToken);

        var scenario = Assert.Single(catalog.Scenarios);
        Assert.False(catalog.FromRegister);
        Assert.Equal("place-an-order", scenario.Slug);
        Assert.Equal("orders/scenarios.md", scenario.DomainPath);
        Assert.Equal(ScenarioState.NeverRun, scenario.State);
        Assert.Same(scenario, Assert.Single(catalog.ForPath(".domain/orders/scenarios.md")));
        Assert.Same(scenario, Assert.Single(catalog.ForPath("orders/scenarios.md")));
        Assert.Empty(catalog.ForPath(".domain/orders/features.md"));
    }

    /// <summary>A scenarios file holds several journeys, each its own run and state.</summary>
    [Fact]
    public async Task Every_journey_of_a_scenarios_file_is_listed_in_file_order()
    {
        var repo = Repository();
        WritePage(repo, "orders/scenarios.md", Scenarios + "\n" + Journey.Replace("Place an order", "Cancel an order", StringComparison.Ordinal).Replace("cart-filled", "cart-emptied", StringComparison.Ordinal));
        WriteRun(repo, RunFolder, $$"""
            { "version": 3, "journey": ".domain/orders/scenarios.md#place-an-order", "signature": "{{Signature()}}",
              "parts": [ { "title": "The cart is filled", "anchor": "the-cart-is-filled", "outcome": "passed" } ], "shots": {} }
            """);

        var catalog = await Store(repo).LoadAsync("backlog", TestContext.Current.CancellationToken);

        Assert.Equal(["place-an-order", "cancel-an-order"], catalog.ForPath("orders/scenarios.md").Select(scenario => scenario.Slug));
        Assert.Equal([ScenarioState.Passed, ScenarioState.NeverRun], catalog.Scenarios.Select(scenario => scenario.State));
        Assert.Equal(ScenarioState.NeverRun, ScenarioEvidence.Combined(catalog.Scenarios.Select(scenario => scenario.State)));
    }

    [Fact]
    public async Task A_committed_register_names_the_journeys()
    {
        var repo = Repository();
        WritePage(repo, "orders/scenarios.md", Scenarios + "\n" + Journey.Replace("Place an order", "Not listed", StringComparison.Ordinal).Replace("cart-filled", "other", StringComparison.Ordinal));
        Directory.CreateDirectory(Path.Combine(repo, ".domain", "_meta"));
        File.WriteAllText(Path.Combine(repo, ".domain", "_meta", "scenarios.json"), """
            { "schemaVersion": 2, "scope": ".domain", "scenarios": [
              { "id": ".domain/orders/scenarios.md#place-an-order", "path": ".domain/orders/scenarios.md", "anchor": "place-an-order", "title": "Place an order", "status": null }
            ] }
            """);

        var catalog = await Store(repo).LoadAsync("backlog", TestContext.Current.CancellationToken);

        Assert.True(catalog.FromRegister);
        Assert.Equal(["place-an-order"], catalog.Scenarios.Select(scenario => scenario.Slug));
    }

    [Fact]
    public async Task A_run_whose_signature_is_the_journey_s_passes_and_a_version_1_run_is_stale()
    {
        var repo = Repository();
        WritePage(repo, "orders/scenarios.md", Scenarios);
        WriteRun(repo, RunFolder, $$"""
            { "version": 3, "signature": "{{Signature()}}", "ranAt": "2026-10-07T08:30:00Z",
              "parts": [ { "title": "The cart is filled", "anchor": "the-cart-is-filled", "outcome": "passed" } ],
              "shots": { "cart-filled": { "file": "cart-filled.png", "part": "the-cart-is-filled", "ranAt": "2026-10-07T08:30:00Z" } } }
            """);

        var store = Store(repo);
        var catalog = await store.LoadAsync("backlog", TestContext.Current.CancellationToken);

        var scenario = Assert.Single(catalog.Scenarios);
        Assert.Equal(ScenarioState.Passed, scenario.State);
        var shot = catalog.Resolve("scenario:orders/scenarios.md#cart-filled", null);
        Assert.NotNull(shot);
        Assert.Equal(DevbookScenarioShotKind.Shown, shot.Kind);
        Assert.Equal(DevbookScenarioShotKind.Shown, catalog.Resolve("shot:cart-filled", ".domain/orders/scenarios.md")!.Kind);
        Assert.Null(catalog.Resolve("diagram.png", ".domain/orders/scenarios.md"));

        WriteRun(repo, RunFolder, """
            { "versie": 1, "feature": "Place an order", "gedraaidOp": "2026-09-01T10:00:00Z",
              "scenarios": [ { "titel": "The cart is filled", "slug": "the-cart-is-filled", "uitkomst": "geslaagd", "duurMs": 10, "afdrukken": [] } ] }
            """);
        store.Invalidate();

        Assert.Equal(ScenarioState.Stale, Assert.Single((await store.LoadAsync("backlog", TestContext.Current.CancellationToken)).Scenarios).State);
    }

    /// <summary>A <c>scenario:</c> image names a scenarios file and a label unique in
    /// it; a file with no journey is no scenarios file to show.</summary>
    [Fact]
    public async Task A_scenario_image_naming_no_scenarios_file_or_label_says_which()
    {
        var repo = Repository();
        WritePage(repo, "orders/scenarios.md", Scenarios);

        var catalog = await Store(repo).LoadAsync("backlog", TestContext.Current.CancellationToken);

        Assert.Equal(DevbookScenarioShotKind.UnknownPage, catalog.Resolve("scenario:billing/scenarios.md#cart-filled", null)!.Kind);
        var unknown = catalog.Resolve("scenario:orders/scenarios.md#nope", null)!;
        Assert.Equal(DevbookScenarioShotKind.UnknownLabel, unknown.Kind);
        Assert.Equal(["cart-filled"], unknown.Alternatives);
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
        WritePage(repo, "orders/scenarios.md", Scenarios);
        WriteRun(repo, RunFolder, $$"""
            { "version": 3, "signature": "{{Signature()}}",
              "parts": [ { "title": "The cart is filled", "anchor": "the-cart-is-filled", "outcome": "failed", "failureShot": "{{file}}" } ],
              "shots": { "cart-filled": { "file": "{{file}}", "part": "the-cart-is-filled" } } }
            """);

        var catalog = await Store(repo).LoadAsync("backlog", TestContext.Current.CancellationToken);

        var scenario = Assert.Single(catalog.Scenarios);
        Assert.Null(scenario.ShotFile("cart-filled"));
        Assert.Null(scenario.FailureFile("the-cart-is-filled"));
        Assert.Equal(DevbookScenarioShotKind.NotCaptured, catalog.Resolve("scenario:orders/scenarios.md#cart-filled", null)!.Kind);
    }

    /// <summary>A run that names another journey is not this journey's.</summary>
    [Fact]
    public async Task A_run_of_another_journey_is_not_this_journey_s()
    {
        var repo = Repository();
        WritePage(repo, "orders/scenarios.md", Scenarios);
        WriteRun(repo, RunFolder, """
            { "version": 3, "journey": ".domain/billing/scenarios.md#place-an-order", "signature": "00000000", "parts": [], "shots": {} }
            """);

        var catalog = await Store(repo).LoadAsync("backlog", TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(catalog.Scenarios).Run);
    }

    private static string Signature() =>
        ScenarioSignature.Of(Assert.Single(ScenarioPageParser.ParseFile(Scenarios, "x/scenarios.md").Scenarios));

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

    private static void WriteRun(string repo, string runFolder, string json)
    {
        var folder = Path.Combine(repo, runFolder.Replace('/', Path.DirectorySeparatorChar));
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
