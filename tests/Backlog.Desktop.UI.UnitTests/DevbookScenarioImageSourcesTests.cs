using Backlog.Infrastructure.Devbook.Scenarios;
using Backlog.Infrastructure.GitHub;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The scenario images in an entry's body — no knowledge document, the source the
/// shell hands the Tasks pane — read through <see cref="DevbookScenarioImageSources"/>.
/// </summary>
public sealed class DevbookScenarioImageSourcesTests : IDisposable
{
    /// <summary>A catalog is read off the renderer, so a suite running in parallel
    /// gets longer than the default second.</summary>
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly List<string> _roots = [];

    /// <summary>
    /// The same images in an entry's body resolve every way they do in a chapter: a
    /// capture from an earlier run says so, a stale journey says stale, an unknown
    /// label lists the scenarios file's labels, and an unknown file says no scenarios
    /// file has that name.
    /// </summary>
    [Fact]
    public async Task A_scenario_image_in_an_entry_resolves_every_way_it_does_in_a_chapter()
    {
        await using var harness = CreateHarness();
        WriteEntryScenarios(harness.Root);
        var store = harness.Context.Services.GetRequiredService<DevbookScenarioStore>();
        using var sources = new DevbookScenarioImageSources(store);
        var loaded = new TaskCompletionSource();
        sources.Changed += () => loaded.TrySetResult();

        var source = sources.For(harness.RepositoryAlias);
        Assert.NotNull(source);
        await loaded.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);
        Assert.Same(source, sources.For(harness.RepositoryAlias));

        const string body =
            "Where the cart stands.\n\n" +
            "![Cart](scenario:orders/scenarios.md#cart-filled)\n\n" +
            "![Edit](scenario:orders/scenarios.md#edited)\n\n" +
            "![Nope](scenario:orders/scenarios.md#nope)\n\n" +
            "![Gone](scenario:billing/scenarios.md#cart-filled)\n";
        var component = harness.Context.Render<CascadingValue<Backlog.UI.Components.Devbook.IDevbookImageSource>>(parameters => parameters
            .Add(cascade => cascade.Value, source)
            .Add(cascade => cascade.IsFixed, true)
            .AddChildContent<MarkdownView>(view => view.Add(v => v.Blocks, MarkdownPreview.Parse(body))));

        var shots = component.FindAll("[data-testid='devbook-scenario-shot']");
        Assert.Equal(["shown", "shown", "unknown-label", "unknown-page"], shots.Select(shot => shot.GetAttribute("data-kind")));

        string Source(int index) => component.FindAll("[data-testid='devbook-scenario-shot-source']")[index].TextContent;
        Assert.Contains("Place an order › The cart is filled · passed", Source(0), StringComparison.Ordinal);
        Assert.Contains("from an earlier run", Source(0), StringComparison.Ordinal);
        Assert.Contains("Edit an order", Source(1), StringComparison.Ordinal);
        Assert.Contains("stale", Source(1), StringComparison.Ordinal);
        Assert.Contains("orders/scenarios.md has no screenshot nope", Source(2), StringComparison.Ordinal);
        Assert.Contains("labels: cart-filled, edited", Source(2), StringComparison.Ordinal);
        Assert.Contains("No scenarios file with a journey is named billing/scenarios.md", Source(3), StringComparison.Ordinal);
    }

    /// <summary>
    /// A catalog that lands after the entry was drawn — a page written since, the
    /// folder change announced — raises <c>Changed</c>, and the redraw the shell
    /// does on it turns "no scenario page" into the page's screenshot. The same
    /// source instance throughout, as the fixed cascade needs.
    /// </summary>
    [Fact]
    public async Task A_catalog_that_lands_late_redraws_an_entry_s_image()
    {
        await using var harness = CreateHarness();
        WriteEntryScenarios(harness.Root);
        var store = harness.Context.Services.GetRequiredService<DevbookScenarioStore>();
        using var sources = new DevbookScenarioImageSources(store);
        var loaded = new TaskCompletionSource();
        sources.Changed += () => loaded.TrySetResult();
        var source = sources.For(harness.RepositoryAlias);
        await loaded.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);

        var blocks = MarkdownPreview.Parse("![Late](scenario:orders/scenarios.cancel.md#cancelled)\n");
        void Entry(ComponentParameterCollectionBuilder<CascadingValue<Backlog.UI.Components.Devbook.IDevbookImageSource>> parameters) => parameters
            .Add(cascade => cascade.Value, source)
            .Add(cascade => cascade.IsFixed, true)
            .AddChildContent<MarkdownView>(view => view.Add(v => v.Blocks, blocks));
        var component = harness.Context.Render<CascadingValue<Backlog.UI.Components.Devbook.IDevbookImageSource>>(Entry);
        Assert.Equal("unknown-page", component.Find("[data-testid='devbook-scenario-shot']").GetAttribute("data-kind"));

        // A split scenarios file, scenarios.<name>.md, is one as much as scenarios.md is.
        File.WriteAllText(Path.Combine(harness.Root, ".domain", "orders", "scenarios.cancel.md"),
            "# Cancelling\n\n## Cancel an order\n\n```meta\ntype: scenario\n```\n\n### The order is cancelled\n- **When** I cancel it\n\n![Cancelled](shot:cancelled)\n");
        var relanded = new TaskCompletionSource();
        sources.Changed += () => relanded.TrySetResult();
        store.Invalidate();
        await relanded.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);

        // What the shell does on Changed: draws again, with the same source.
        component.Render(Entry);

        Assert.Same(source, sources.For(harness.RepositoryAlias));
        component.WaitForAssertion(() => Assert.Equal("not-captured", component.Find("[data-testid='devbook-scenario-shot']").GetAttribute("data-kind")), Wait);
        Assert.Contains("Cancel an order", component.Find("[data-testid='devbook-scenario-shot-source']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>The journeys behind an entry's images, in one scenarios file: one whose
    /// capture is older than its last run, and one edited since its run.</summary>
    private static void WriteEntryScenarios(string root)
    {
        var orders = Path.Combine(root, ".domain", "orders");
        Directory.CreateDirectory(orders);
        File.WriteAllText(Path.Combine(orders, "context.md"), "# Orders\n\n```meta\nstatus: draft\nindex: root\ntype: context\n```\n\nOrders prose.\n");

        const string place = "## Place an order\n\n```meta\ntype: scenario\n```\n\n### The cart is filled\n- **Given** an empty cart\n\n![The filled cart](shot:cart-filled)\n";
        const string edit = "## Edit an order\n\n```meta\ntype: scenario\n```\n\n### The order is edited\n- **When** I change the lamp\n\n![The edit](shot:edited)\n";
        const string file = "# Scenarios\n\n" + place + "\n" + edit;
        var journeys = ScenarioPageParser.ParseFile(file, ".domain/orders/scenarios.md").Scenarios;

        WriteRun(root, journeys[0], "the-cart-is-filled", "cart-filled", shotRanAt: "2026-10-01T08:30:00Z");
        WriteRun(root, journeys[1], "the-order-is-edited", "edited", shotRanAt: "2026-10-07T08:30:00Z");
        File.WriteAllText(Path.Combine(orders, "scenarios.md"), file.Replace("change the lamp", "change two lamps", StringComparison.Ordinal));

        static void WriteRun(string root, ScenarioJourney journey, string part, string label, string shotRanAt)
        {
            var folder = Path.Combine(root, journey.RunFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, $"{label}.png"), [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            var signature = ScenarioSignature.Of(journey);
            File.WriteAllText(Path.Combine(folder, "run.json"), $$"""
                {
                  "version": 3,
                  "journey": "{{journey.Address}}",
                  "signature": "{{signature}}",
                  "ranAt": "2026-10-07T08:30:00Z",
                  "parts": [ { "title": "Part", "anchor": "{{part}}", "outcome": "passed" } ],
                  "shots": { "{{label}}": { "file": "{{label}}.png", "part": "{{part}}", "ranAt": "{{shotRanAt}}" } }
                }
                """);
        }
    }

    private Harness CreateHarness()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-scenario-image-sources-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".domain"));
        _roots.Add(root);
        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), "# Context Map\n\n```meta\nstatus: draft\n```\n\nThe contexts.\n");

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = root,
            DevbookFolders = DevbookFolderSetting.Defaults()
        };
        Assert.Null(gitHub.SetRepositories([repository]));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(gitHub, settings));
        context.Services.AddSingleton(sp => new DevbookScenarioStore(sp.GetRequiredService<IDevbookFolderSource>()));

        return new Harness(root, context, repository.Alias);
    }

    public void Dispose()
    {
        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed record Harness(string Root, BunitContext Context, string RepositoryAlias) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
