using Backlog.UI.Components.Diagrams;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Lobes atlas takeover (<c>.devbook/domain/devbook/features.md#lobes-atlas</c>)
/// over a graph handed in whole: its scope switch, the camera a switch resets, the
/// regions a scope hides, and what a selected node offers.
/// <para>
/// The camera is the renderer's, and it keeps one per layout: a new layout starts
/// from the default view. So "switching scope resets the camera" is proved here as
/// the layout the renderer is handed changing with the scope, which is the only
/// thing C# decides about it.
/// </para>
/// </summary>
public sealed class LobesAtlasTakeoverTests : IDisposable
{
    private const string RenderFunction = "backlogDiagrams.renderLobesAtlas";

    private readonly BunitContext _context = new();
    private readonly FixedLobes _lobes = new(SampleGraph());

    public LobesAtlasTakeoverTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddSingleton<IDevbookLobesSource>(_lobes);
    }

    [Fact]
    public void The_scope_switch_offers_all_the_brain_each_devbook_and_each_ghost_dashed()
    {
        var component = Render();

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-scope']")));

        var options = component.FindAll("[data-testid='lobes-atlas-scope']");
        Assert.Equal(["All", "Brain", "backlog", "devbook-stack"], options.Select(option => option.TextContent.Trim()));
        Assert.Equal(["all", "brain", "repo:backlog", "ghost:devbook-stack"], options.Select(option => option.GetAttribute("data-scope")));
        Assert.Contains("lobes-atlas__scope--ghost", options[3].ClassList);
        Assert.DoesNotContain("lobes-atlas__scope--ghost", options[2].ClassList);

        // Opened from the header with no scope, it opens on All.
        Assert.Equal("true", options[0].GetAttribute("aria-pressed"));
        Assert.Equal(LobesAtlasModel.AllLayout, LastModel().Layout);
    }

    [Fact]
    public void Switching_scope_hands_the_renderer_a_new_layout_which_resets_the_camera()
    {
        var component = Render();
        component.WaitForAssertion(() => Assert.Equal(LobesAtlasModel.AllLayout, LastModel().Layout));

        Scope(component, "repo:backlog").Click();

        component.WaitForAssertion(() =>
        {
            Assert.Equal("repo:backlog", LastModel().Layout);
            Assert.Equal("true", Scope(component, "repo:backlog").GetAttribute("aria-pressed"));
            Assert.Equal("false", Scope(component, LobesAtlasModel.AllLayout).GetAttribute("aria-pressed"));
            Assert.Equal("repo:backlog", component.Find("[data-testid='lobes-atlas']").GetAttribute("data-scope"));
            Assert.Contains("backlog devbook", component.Find("[data-testid='lobes-atlas-headline']").TextContent, StringComparison.Ordinal);
            Assert.Equal("Open devbook", component.Find("[data-testid='lobes-atlas-card-action']").TextContent.Trim());
        });

        // The index lists only that scope's marks.
        Assert.All(component.FindAll("[data-testid='graph-atlas-index-option']"),
            option => Assert.StartsWith("repo:backlog|", option.GetAttribute("data-node-id"), StringComparison.Ordinal));

        Scope(component, "ghost:devbook-stack").Click();
        component.WaitForAssertion(() =>
        {
            Assert.Equal("ghost:devbook-stack", LastModel().Layout);
            Assert.Equal("Register repository", component.Find("[data-testid='lobes-atlas-card-action']").TextContent.Trim());
            Assert.Equal("settings/repositories", component.Find("[data-testid='lobes-atlas-card-action']").GetAttribute("href"));
        });
    }

    [Fact]
    public void A_scope_handed_in_opens_on_that_scope_and_an_unknown_one_opens_all()
    {
        var component = Render("brain");
        component.WaitForAssertion(() =>
        {
            Assert.Equal("brain", LastModel().Layout);
            Assert.Equal("true", Scope(component, "brain").GetAttribute("aria-pressed"));
            Assert.Equal("Anatomical regions", component.Find(".lobes-atlas__panel-title").TextContent.Trim());
            Assert.Equal("Open in the pane", component.Find("[data-testid='lobes-atlas-card-action']").TextContent.Trim());
        });

        var unknown = Render("repo:nowhere");
        unknown.WaitForAssertion(() => Assert.Equal("true", Scope(unknown, LobesAtlasModel.AllLayout).GetAttribute("aria-pressed")));
    }

    [Fact]
    public void A_region_row_hides_that_region_of_the_scope_on_screen()
    {
        var component = Render("repo:backlog");
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-region']")));

        var arc42 = component.Find("[data-testid='lobes-atlas-region'][data-lobe='frontal']");
        Assert.Equal("true", arc42.GetAttribute("aria-pressed"));
        arc42.Click();

        component.WaitForAssertion(() =>
        {
            Assert.Contains(new LobesAtlasRegionKey("repo:backlog", LobesAtlasMark.Frontal), LastModel().HiddenLobes);
            Assert.Equal("false", component.Find("[data-testid='lobes-atlas-region'][data-lobe='frontal']").GetAttribute("aria-pressed"));
            Assert.DoesNotContain(component.FindAll("[data-testid='graph-atlas-index-option']"),
                option => option.GetAttribute("data-node-id") == "repo:backlog|.devbook/arc42/01-introduction.md");
        });

        component.Find("[data-testid='lobes-atlas-show-all']").Click();
        component.WaitForAssertion(() => Assert.Empty(LastModel().HiddenLobes));
    }

    [Fact]
    public void A_brain_note_offers_to_open_in_the_pane()
    {
        string? opened = null;
        var component = Render("brain", onOpenNote: path => opened = path);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='graph-atlas-index-option']")));

        component.Find("[data-testid='graph-atlas-index-option'][data-node-id='brain|projects/plan.md']").Click();
        component.WaitForAssertion(() => component.Find("[data-testid='lobes-atlas-open-note']").Click());

        Assert.Equal("projects/plan.md", opened);
    }

    [Fact]
    public void A_resolved_chapter_opens_its_devbook_and_a_dashed_one_says_why_it_cannot()
    {
        BrainRepoLink? opened = null;
        var component = Render(onOpenChapter: link => opened = link);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='graph-atlas-index-option']")));

        component.Find("[data-testid='graph-atlas-index-option'][data-node-id='ghost:devbook-stack|ghost:tech/radar.md']").Click();
        component.WaitForAssertion(() =>
            Assert.Contains(BrainRepoLinkResolver.NotOnThisMachine, component.Find("[data-testid='lobes-atlas-repo-link']").TextContent, StringComparison.Ordinal));
        Assert.Empty(component.FindAll("[data-testid='lobes-atlas-repo-link'] button"));

        component.Find("[data-testid='graph-atlas-index-option'][data-node-id='repo:backlog|.devbook/arc42/adr/0001-use-sqlite.md']").Click();
        component.WaitForAssertion(() => component.Find("[data-testid='lobes-atlas-repo-link'] button").Click());

        Assert.NotNull(opened);
        Assert.Equal("backlog", opened.Alias);
        Assert.Equal(".devbook/arc42/adr/0001-use-sqlite.md", opened.Path);
    }

    [Fact]
    public void A_devbook_chapter_no_note_links_to_still_opens_in_its_devbook()
    {
        BrainRepoLink? opened = null;
        var component = Render("repo:backlog", onOpenChapter: link => opened = link);
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='graph-atlas-index-option']")));

        component.Find("[data-testid='graph-atlas-index-option'][data-node-id='repo:backlog|.devbook/arc42/01-introduction.md']").Click();
        component.WaitForAssertion(() => component.Find("[data-testid='lobes-atlas-open-chapter']").Click());

        Assert.NotNull(opened);
        Assert.Equal("backlog", opened.Alias);
        Assert.Equal(".devbook/arc42/01-introduction.md", opened.Path);
    }

    [Fact]
    public void The_close_button_and_the_card_raise_the_hosts_callbacks()
    {
        var closed = 0;
        string? openedScope = null;
        var component = _context.Render<LobesAtlasTakeover>(parameters => parameters
            .Add(atlas => atlas.Scope, "repo:backlog")
            .Add(atlas => atlas.OnClose, () => closed++)
            .Add(atlas => atlas.OnOpenScope, (string alias) => openedScope = alias));
        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll("[data-testid='lobes-atlas-card-action']")));

        component.Find("[data-testid='lobes-atlas-card-action']").Click();
        component.WaitForAssertion(() => Assert.Equal("backlog", openedScope));

        var close = component.Find("[data-testid='lobes-atlas-close']");
        Assert.Equal("Close the atlas", close.GetAttribute("aria-label"));
        close.Click();
        Assert.Equal(1, closed);
    }

    [Fact]
    public void No_brain_index_is_said_in_words_with_a_way_back()
    {
        _lobes.Graph = DevbookLobesGraph.Unavailable(DevbookAtlasService.BrainIndexMissing);
        var closed = 0;
        var component = _context.Render<LobesAtlasTakeover>(parameters => parameters.Add(atlas => atlas.OnClose, () => closed++));

        component.WaitForAssertion(() =>
            Assert.Contains("has not been built yet", component.Find("[data-testid='lobes-atlas-unavailable']").TextContent, StringComparison.Ordinal));
        Assert.Empty(component.FindAll("[data-testid='lobes-atlas-graph']"));
        Assert.Empty(component.FindAll("[data-testid='lobes-atlas-scope']"));

        component.Find("[data-testid='lobes-atlas-back']").Click();
        Assert.Equal(1, closed);
    }

    [Fact]
    public void A_change_reads_the_atlas_again()
    {
        var component = Render();
        component.WaitForAssertion(() => Assert.Equal(4, component.FindAll("[data-testid='lobes-atlas-scope']").Count));

        var graph = SampleGraph();
        _lobes.Graph = graph with { Scopes = [.. graph.Scopes.Where(scope => scope.Kind != DevbookLobesScopeKind.Ghost)] };
        _lobes.RaiseChanged();

        component.WaitForAssertion(() => Assert.Equal(3, component.FindAll("[data-testid='lobes-atlas-scope']").Count));
    }

    // ----- Fixtures ------------------------------------------------------------------

    private IRenderedComponent<LobesAtlasTakeover> Render(
        string? scope = null,
        Action<string>? onOpenNote = null,
        Action<BrainRepoLink>? onOpenChapter = null) =>
        _context.Render<LobesAtlasTakeover>(parameters => parameters
            .Add(atlas => atlas.Scope, scope)
            .Add(atlas => atlas.OnOpenNote, (string path) => onOpenNote?.Invoke(path))
            .Add(atlas => atlas.OnOpenRepositoryChapter, (BrainRepoLink link) => onOpenChapter?.Invoke(link)));

    private static AngleSharp.Dom.IElement Scope(IRenderedComponent<LobesAtlasTakeover> component, string key) =>
        component.Find($"[data-testid='lobes-atlas-scope'][data-scope='{key}']");

    /// <summary>The model the renderer was last handed.</summary>
    private LobesAtlasModel LastModel() =>
        Assert.IsType<LobesAtlasModel>(_context.JSInterop.Invocations
            .Where(invocation => invocation.Identifier == RenderFunction)
            .Last()
            .Arguments[2]);

    private static DevbookLobesGraph SampleGraph()
    {
        const string brain = "brain";
        const string backlog = "repo:backlog";
        const string stack = "ghost:devbook-stack";

        DevbookLobesNode[] nodes =
        [
            new($"{brain}|projects/plan.md", "Plan", brain, DevbookLobe.Frontal, DevbookLobesNodeKind.Note, "projects/plan.md"),
            new($"{brain}|people/ann.md", "Ann", brain, DevbookLobe.Temporal, DevbookLobesNodeKind.Note, "people/ann.md"),
            new($"{brain}|index:CONVENTIONS.md", "CONVENTIONS.md", brain, DevbookLobe.Stem, DevbookLobesNodeKind.IndexHub, string.Empty),
            new($"{brain}|hub:{backlog}", "backlog", brain, DevbookLobe.Occipital, DevbookLobesNodeKind.RepositoryHub, string.Empty),
            new($"{brain}|hub:{stack}", "devbook-stack", brain, DevbookLobe.Occipital, DevbookLobesNodeKind.RepositoryHub, string.Empty, Dashed: true),
            new($"{backlog}|.devbook/arc42/01-introduction.md", "Introduction", backlog, DevbookLobe.Frontal, DevbookLobesNodeKind.Chapter, ".devbook/arc42/01-introduction.md"),
            new($"{backlog}|.devbook/arc42/adr/0001-use-sqlite.md", "Use SQLite", backlog, DevbookLobe.Frontal, DevbookLobesNodeKind.Chapter, ".devbook/arc42/adr/0001-use-sqlite.md",
                RepositoryLink: new DevbookAtlasRepositoryLink("backlog", "repo:backlog/.devbook/arc42/adr/0001-use-sqlite.md", true, null)),
            new($"{backlog}|.devbook/tech/docker.md", "Docker", backlog, DevbookLobe.Parietal, DevbookLobesNodeKind.Chapter, ".devbook/tech/docker.md"),
            new($"{stack}|ghost:tech/radar.md", "tech/radar.md", stack, DevbookLobe.Parietal, DevbookLobesNodeKind.GhostChapter, "tech/radar.md", true,
                new DevbookAtlasRepositoryLink("devbook-stack", "repo:devbook-stack/tech/radar.md", false, BrainRepoLinkResolver.NotOnThisMachine))
        ];

        DevbookLobesEdge[] edges =
        [
            new($"{brain}|projects/plan.md", $"{brain}|people/ann.md", "related"),
            new($"{brain}|hub:{backlog}", $"{backlog}|.devbook/arc42/adr/0001-use-sqlite.md", DevbookLobesReader.BundleEdgeKind),
            new($"{brain}|hub:{stack}", $"{stack}|ghost:tech/radar.md", DevbookLobesReader.BundleEdgeKind, Dashed: true)
        ];

        DevbookLobesScope Summary(string key, string label, DevbookLobesScopeKind kind, string? alias, int linking) =>
            new(key, label, kind, alias, kind != DevbookLobesScopeKind.Ghost, null,
                Enum.GetValues<DevbookLobe>().ToDictionary(lobe => lobe, lobe => nodes.Count(node => node.ScopeKey == key && node.Lobe == lobe)),
                nodes.Count(node => node.ScopeKey == key && node.Kind is DevbookLobesNodeKind.Note or DevbookLobesNodeKind.Chapter or DevbookLobesNodeKind.GhostChapter),
                linking);

        return new DevbookLobesGraph(
            true,
            null,
            [
                Summary(brain, "Brain", DevbookLobesScopeKind.Brain, null, 1),
                Summary(backlog, "backlog", DevbookLobesScopeKind.Devbook, "backlog", 1),
                Summary(stack, "devbook-stack", DevbookLobesScopeKind.Ghost, "devbook-stack", 1)
            ],
            nodes,
            edges);
    }

    public void Dispose() => _context.Dispose();

    /// <summary>A Lobes source over a graph the test sets.</summary>
    internal sealed class FixedLobes(DevbookLobesGraph graph) : IDevbookLobesSource
    {
        public DevbookLobesGraph Graph { get; set; } = graph;

        public event Action? Changed;

        public Task<DevbookLobesGraph> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Graph);

        public void RaiseChanged() => Changed?.Invoke();
    }

    /// <summary>The sample graph, for the shell and pane tests.</summary>
    internal static DevbookLobesGraph Sample() => SampleGraph();
}
