using System.Text.RegularExpressions;
using Backlog.UI.Components.Diagrams;
using Bunit;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A click demo in its frame, as devbook's <c>devbook.demo.address@1</c> says a host
/// frames one: sandboxed with scripts and without same-origin, the document handed
/// over as it is, and a <c>demo:goto</c> sent only once the demo said
/// <c>demo:ready</c>.
/// </summary>
public sealed class DemoViewTests
{
    private const string Html = "<!doctype html><html><head><title>Ordering</title></head><body><main data-demo-app></main></body></html>";

    [Fact]
    public void The_frame_is_sandboxed_with_scripts_and_nothing_else()
    {
        using var context = Context();

        var demo = Render(context);

        // allow-same-origin would hand the demo this app's storage and cookies;
        // the contract says scripts are the only token a demo needs.
        Assert.Equal("allow-scripts", demo.Find("[data-testid='demo-view-frame']").GetAttribute("sandbox"));
    }

    /// <summary>The Archify frame pins its document to the dark theme and lays a
    /// stylesheet over it. A demo shows the product as it looks, so it is handed
    /// over byte for byte and through its own loader, never the artifact's.</summary>
    [Fact]
    public void The_demo_is_handed_to_its_frame_unchanged_and_not_through_the_artifact_loader()
    {
        using var context = Context();

        Render(context);

        var load = Assert.Single(context.JSInterop.Invocations["backlogDemos.load"]);
        Assert.Equal(Html, load.Arguments[2]);
        Assert.Empty(context.JSInterop.Invocations["backlogDiagrams.renderArtifact"]);
    }

    [Fact]
    public void No_address_is_sent_before_the_demo_is_ready()
    {
        using var context = Context();

        Render(context, ["walkthrough/checkout"]);

        Assert.Empty(context.JSInterop.Invocations["backlogDemos.goto"]);
    }

    /// <summary>A message names no file and starts at <c>#</c>: the frame already
    /// names the file, and a demo never knows its own path.</summary>
    [Fact]
    public async Task The_first_address_is_opened_once_the_demo_reports_ready()
    {
        using var context = Context();
        var demo = Render(context, ["walkthrough/checkout", "cart"]);

        await demo.InvokeAsync(() => demo.Instance.OnDemoReady());

        var go = Assert.Single(context.JSInterop.Invocations["backlogDemos.goto"]);
        Assert.Equal("#walkthrough/checkout", go.Arguments[1]);
    }

    [Fact]
    public async Task A_demo_with_no_address_is_left_where_it_opens()
    {
        using var context = Context();
        var demo = Render(context);

        await demo.InvokeAsync(() => demo.Instance.OnDemoReady());

        Assert.Empty(context.JSInterop.Invocations["backlogDemos.goto"]);
        Assert.Empty(demo.FindAll("[data-testid='demo-view-place']"));
    }

    [Fact]
    public async Task A_place_the_reader_picks_is_opened_in_the_demo()
    {
        using var context = Context();
        var demo = Render(context, ["walkthrough/checkout", "cart/total?role=clerk"]);
        await demo.InvokeAsync(() => demo.Instance.OnDemoReady());

        var places = demo.FindAll("[data-testid='demo-view-place']");
        Assert.Equal(["walkthrough/checkout", "cart/total?role=clerk"], places.Select(place => place.TextContent.Trim()));

        await demo.InvokeAsync(() => demo.FindAll("[data-testid='demo-view-place']")[1].Click());

        Assert.Equal(
            ["#walkthrough/checkout", "#cart/total?role=clerk"],
            context.JSInterop.Invocations["backlogDemos.goto"].Select(go => go.Arguments[1]));
        Assert.Equal("true", demo.FindAll("[data-testid='demo-view-place']")[1].GetAttribute("aria-pressed"));
    }

    [Fact]
    public async Task A_place_picked_before_the_demo_is_ready_is_opened_when_it_is()
    {
        using var context = Context();
        var demo = Render(context, ["walkthrough/checkout", "cart"]);

        await demo.InvokeAsync(() => demo.FindAll("[data-testid='demo-view-place']")[1].Click());
        Assert.Empty(context.JSInterop.Invocations["backlogDemos.goto"]);

        await demo.InvokeAsync(() => demo.Instance.OnDemoReady());

        var go = Assert.Single(context.JSInterop.Invocations["backlogDemos.goto"]);
        Assert.Equal("#cart", go.Arguments[1]);
    }

    /// <summary>A different document is a new demo: it has to say it is ready
    /// again before anything is sent to it.</summary>
    [Fact]
    public async Task A_new_document_is_loaded_again_and_waits_for_its_own_ready()
    {
        using var context = Context();
        var demo = Render(context, ["cart"]);
        await demo.InvokeAsync(() => demo.Instance.OnDemoReady());

        demo.Render(parameters => parameters.Add(view => view.Html, Html.Replace("Ordering", "Ordering v2", StringComparison.Ordinal)));

        Assert.Equal(2, context.JSInterop.Invocations["backlogDemos.load"].Count);
        Assert.Single(context.JSInterop.Invocations["backlogDemos.goto"]);

        await demo.InvokeAsync(() => demo.Instance.OnDemoReady());
        Assert.Equal(2, context.JSInterop.Invocations["backlogDemos.goto"].Count);
    }

    /// <summary>A re-read of the same file is a new string with the same text; the
    /// demo keeps its place rather than reloading.</summary>
    [Fact]
    public async Task The_same_document_read_again_does_not_reload_the_frame()
    {
        using var context = Context();
        var demo = Render(context, ["cart"]);
        await demo.InvokeAsync(() => demo.Instance.OnDemoReady());

        demo.Render(parameters => parameters.Add(view => view.Html, new string(Html.AsSpan())));

        Assert.Single(context.JSInterop.Invocations["backlogDemos.load"]);
        Assert.Single(context.JSInterop.Invocations["backlogDemos.goto"]);
    }

    [Fact]
    public void Full_screen_takes_the_stage_and_the_way_out_is_inside_it()
    {
        using var context = Context();
        var demo = Render(context);

        demo.Find("[data-testid='demo-view-fullscreen']").Click();

        var stage = demo.Find("[data-testid='demo-view-stage']");
        Assert.NotNull(stage.QuerySelector("[data-testid='demo-view-frame']"));
        Assert.NotNull(stage.QuerySelector("[data-testid='demo-view-exit-fullscreen']"));
        Assert.Single(context.JSInterop.Invocations["backlogDiagrams.toggleArtifactFullscreen"]);
    }

    /// <summary>bUnit has no frame, so the half that lives in the browser is held
    /// to the contract on its text: a message counts only from the frame this host
    /// created, a newer version is ignored, the demo is posted to at <c>*</c>
    /// because an opaque origin cannot be named, and nothing theme-related is
    /// written into the document.</summary>
    [Fact]
    public void The_browser_half_follows_the_address_contract()
    {
        var script = File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.js"));
        var start = script.IndexOf("window.backlogDemos", StringComparison.Ordinal);
        Assert.True(start >= 0, "window.backlogDemos is no longer in components.js.");
        var end = script.IndexOf("window.backlogRoadmapTimeline", start, StringComparison.Ordinal);
        var demos = script[start..end];

        Assert.Matches(new Regex(@"event\.source\s*!==\s*\w+\.contentWindow"), demos);
        Assert.Matches(new Regex(@"\.v\s*>\s*1"), demos);
        Assert.Contains("'demo:ready'", demos, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"postMessage\(\{\s*type:\s*'demo:goto',\s*v:\s*1,\s*address\s*\},\s*'\*'\)"), demos);
        Assert.Matches(new Regex(@"\.srcdoc\s*=\s*html;"), demos);
        Assert.DoesNotContain("data-theme", demos, StringComparison.Ordinal);
        Assert.DoesNotContain("matchMedia", demos, StringComparison.Ordinal);
    }

    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    private static IRenderedComponent<DemoView> Render(BunitContext context, IReadOnlyList<string>? addresses = null) =>
        context.Render<DemoView>(parameters => parameters
            .Add(view => view.Html, Html)
            .Add(view => view.Path, ".devbook/domain/ordering/features.demo.html")
            .Add(view => view.Addresses, addresses ?? []));
}
