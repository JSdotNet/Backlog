using System.Text.Json;

using Backlog.Desktop.UI.Devbook;
using Backlog.UI.Components.Diagrams;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Lobes read model turned into what the atlas draws
/// (<c>.devbook/domain/devbook/features.md#lobes-atlas</c>): the canvas model and
/// the index rows, which must name the same marks.
/// </summary>
public sealed class DevbookLobesAtlasTests
{
    private const string Brain = DevbookLobesGraph.BrainScopeKey;
    private const string Backlog = "repo:backlog";
    private const string Stack = "ghost:stack";

    [Fact]
    public void Every_region_maps_to_the_renderers_key()
    {
        Assert.Equal(
            ["frontal", "parietal", "temporal", "occipital", "cerebellum", "stem", "dust"],
            Enum.GetValues<DevbookLobe>().Select(DevbookLobesAtlas.LobeKey));
    }

    [Fact]
    public void Scopes_carry_their_kind_count_regions_and_satellite()
    {
        var model = DevbookLobesAtlas.Model(Graph(), LobesAtlasModel.AllLayout);

        var brain = model.Scopes.Single(scope => scope.Key == Brain);
        Assert.Equal(LobesAtlasScope.Brain, brain.Kind);
        Assert.Equal(3, brain.Count);
        Assert.Null(brain.SatelliteLabel);
        Assert.Contains(brain.Regions!, region => region is { Lobe: "frontal", Label: "Frontal", Count: 1 });
        Assert.Contains(brain.Regions!, region => region is { Lobe: "stem", Label: "Brain stem" });
        Assert.DoesNotContain(brain.Regions!, region => region.Lobe == "dust");

        var backlog = model.Scopes.Single(scope => scope.Key == Backlog);
        Assert.Equal(LobesAtlasScope.Devbook, backlog.Kind);
        Assert.Equal("Brain · 2 notes link here", backlog.SatelliteLabel);
        Assert.Contains(backlog.Regions!, region => region is { Lobe: "frontal", Label: "arc42", Count: 1 });

        var ghost = model.Scopes.Single(scope => scope.Key == Stack);
        Assert.Equal(LobesAtlasScope.Ghost, ghost.Kind);
        Assert.Null(ghost.Count);
        Assert.Equal("Brain · 1 note links here", ghost.SatelliteLabel);
    }

    [Fact]
    public void In_all_each_tag_carries_its_count_and_a_ghosts_says_it_is_not_registered()
    {
        var model = DevbookLobesAtlas.Model(Graph(), LobesAtlasModel.AllLayout);

        var brain = model.Scopes.Single(scope => scope.Key == Brain);
        Assert.Equal(3, brain.Count);
        Assert.Null(brain.Status);
        var backlog = model.Scopes.Single(scope => scope.Key == Backlog);
        Assert.Equal(2, backlog.Count);
        Assert.Null(backlog.Status);

        var ghost = model.Scopes.Single(scope => scope.Key == Stack);
        Assert.Null(ghost.Count);
        Assert.Equal("not registered", ghost.Status);
    }

    [Fact]
    public void Hubs_and_a_devbooks_roots_are_hubs_and_ghost_chapters_are_hollow()
    {
        var model = DevbookLobesAtlas.Model(Graph(), LobesAtlasModel.AllLayout);

        Assert.Equal(LobesAtlasMark.Hub, Mark(model, "brain|hub:repo:backlog").Kind);
        Assert.Equal(LobesAtlasMark.Hub, Mark(model, "brain|index:CONVENTIONS.md").Kind);
        Assert.Equal(LobesAtlasMark.Hub, Mark(model, "repo:backlog|README.md").Kind);
        Assert.Equal(LobesAtlasMark.Item, Mark(model, "repo:backlog|arc42/adr.md").Kind);
        Assert.Equal(LobesAtlasMark.GhostItem, Mark(model, "ghost:stack|ghost:arc42/x.md").Kind);
        Assert.False(Mark(model, "ghost:stack|ghost:arc42/x.md").Dashed);
        Assert.True(Mark(model, "brain|hub:ghost:stack").Dashed);
        Assert.Equal("dust", Mark(model, "brain|archive/old.md").Lobe);
    }

    [Fact]
    public void Repo_links_are_left_to_the_bundles()
    {
        var model = DevbookLobesAtlas.Model(Graph(), LobesAtlasModel.AllLayout);

        Assert.DoesNotContain(model.Links, link => link.Source == "brain|projects/plan.md" && link.Target == "repo:backlog|arc42/adr.md");
        Assert.Contains(model.Links, link => link is { Source: "brain|hub:repo:backlog", Kind: LobesAtlasLink.Bundle, Dashed: false });
        Assert.Contains(model.Links, link => link is { Source: "brain|hub:ghost:stack", Kind: LobesAtlasLink.Bundle, Dashed: true });
        Assert.Contains(model.Links, link => link is { Source: "brain|projects/plan.md", Target: "brain|people/ann.md", Kind: LobesAtlasLink.Own });
    }

    [Fact]
    public void The_layout_and_what_is_hidden_pass_through()
    {
        var model = DevbookLobesAtlas.Model(
            Graph(),
            Backlog,
            [Stack],
            [new LobesAtlasRegionKey(Backlog, "frontal")],
            showLabels: false);

        Assert.Equal(Backlog, model.Layout);
        Assert.Equal([Stack], model.HiddenScopes);
        Assert.Equal([new LobesAtlasRegionKey(Backlog, "frontal")], model.HiddenLobes);
        Assert.False(model.ShowLabels);
    }

    [Fact]
    public void A_blank_layout_is_all()
    {
        Assert.Equal(LobesAtlasModel.AllLayout, DevbookLobesAtlas.Model(Graph(), "  ").Layout);
    }

    [Fact]
    public void The_index_lists_every_scope_not_hidden_in_all()
    {
        var nodes = DevbookLobesAtlas.IndexNodes(Graph(), LobesAtlasModel.AllLayout, [Stack]);

        Assert.Contains(nodes, node => node.Id == "brain|projects/plan.md");
        Assert.Contains(nodes, node => node.Id == "repo:backlog|arc42/adr.md");
        Assert.DoesNotContain(nodes, node => node.Id.StartsWith("ghost:stack|", StringComparison.Ordinal));
    }

    [Fact]
    public void The_index_of_one_scope_lists_its_shown_regions_only()
    {
        var nodes = DevbookLobesAtlas.IndexNodes(Graph(), Backlog, hiddenLobes: [new LobesAtlasRegionKey(Backlog, "frontal")]);

        Assert.All(nodes, node => Assert.StartsWith("repo:backlog|", node.Id, StringComparison.Ordinal));
        Assert.DoesNotContain(nodes, node => node.Id == "repo:backlog|arc42/adr.md");
        Assert.Contains(nodes, node => node.Id == "repo:backlog|README.md");
    }

    [Fact]
    public void An_index_row_says_where_a_mark_is_and_why_it_is_hollow()
    {
        var nodes = DevbookLobesAtlas.IndexNodes(Graph(), LobesAtlasModel.AllLayout);

        var chapter = nodes.Single(node => node.Id == "repo:backlog|arc42/adr.md");
        Assert.Equal("Chapter", chapter.Kind);
        Assert.Equal("backlog · arc42", chapter.Group);
        Assert.Equal("active", chapter.ToneSlug);
        Assert.Equal(1, chapter.InDegree);

        var ghost = nodes.Single(node => node.Id == "ghost:stack|ghost:arc42/x.md");
        Assert.Equal("Ghost chapter", ghost.Kind);
        Assert.Equal("archived", ghost.ToneSlug);
        Assert.Equal("not on this machine", ghost.Status);
        Assert.Equal("stack · arc42", ghost.Group);

        Assert.Equal("Brain · Archive", nodes.Single(node => node.Id == "brain|archive/old.md").Group);
    }

    [Fact]
    public void A_brain_mark_wears_the_brains_tone_in_the_index_and_a_devbook_mark_stays_gold()
    {
        // Found in the live QA pass: every list mark read gold, the Brain's notes
        // included, where the map draws them in color-band-2.
        var nodes = DevbookLobesAtlas.IndexNodes(Graph(), LobesAtlasModel.AllLayout);

        Assert.Equal("brain", nodes.Single(node => node.Id == "brain|projects/plan.md").ToneSlug);
        Assert.Equal("active", nodes.Single(node => node.Id == "repo:backlog|arc42/adr.md").ToneSlug);
        Assert.Equal("archived", nodes.Single(node => node.Id == "ghost:stack|ghost:arc42/x.md").ToneSlug);
    }

    [Fact]
    public void The_model_serializes_in_the_shape_the_renderer_reads()
    {
        var json = JsonSerializer.Serialize(DevbookLobesAtlas.Model(Graph(), LobesAtlasModel.AllLayout), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("all", root.GetProperty("layout").GetString());
        Assert.True(root.GetProperty("showLabels").GetBoolean());
        var mark = root.GetProperty("marks")[0];
        foreach (var name in new[] { "id", "label", "scope", "lobe", "kind", "dashed" }) Assert.True(mark.TryGetProperty(name, out _), name);
        var link = root.GetProperty("links")[0];
        foreach (var name in new[] { "source", "target", "kind", "dashed" }) Assert.True(link.TryGetProperty(name, out _), name);
        var scope = root.GetProperty("scopes")[0];
        foreach (var name in new[] { "key", "label", "kind", "count", "regions", "satelliteLabel", "status" }) Assert.True(scope.TryGetProperty(name, out _), name);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("hiddenScopes").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("hiddenLobes").ValueKind);
    }

    private static LobesAtlasMark Mark(LobesAtlasModel model, string id) => model.Marks.Single(mark => mark.Id == id);

    private static IReadOnlyDictionary<DevbookLobe, int> Counts(params (DevbookLobe Lobe, int Count)[] counts) =>
        Enum.GetValues<DevbookLobe>().ToDictionary(lobe => lobe, lobe => counts.Where(pair => pair.Lobe == lobe).Sum(pair => pair.Count));

    private static DevbookLobesGraph Graph()
    {
        DevbookLobesNode[] nodes =
        [
            new("brain|projects/plan.md", "Plan", Brain, DevbookLobe.Frontal, DevbookLobesNodeKind.Note, "projects/plan.md"),
            new("brain|people/ann.md", "Ann", Brain, DevbookLobe.Temporal, DevbookLobesNodeKind.Note, "people/ann.md"),
            new("brain|archive/old.md", "Old", Brain, DevbookLobe.Dust, DevbookLobesNodeKind.Note, "archive/old.md"),
            new("brain|index:CONVENTIONS.md", "CONVENTIONS.md", Brain, DevbookLobe.Stem, DevbookLobesNodeKind.IndexHub, ""),
            new("brain|hub:repo:backlog", "backlog", Brain, DevbookLobe.Occipital, DevbookLobesNodeKind.RepositoryHub, ""),
            new("brain|hub:ghost:stack", "stack", Brain, DevbookLobe.Occipital, DevbookLobesNodeKind.RepositoryHub, "", Dashed: true),
            new("repo:backlog|README.md", "README", Backlog, DevbookLobe.Stem, DevbookLobesNodeKind.Chapter, "README.md"),
            new("repo:backlog|arc42/adr.md", "Decisions", Backlog, DevbookLobe.Frontal, DevbookLobesNodeKind.Chapter, "arc42/adr.md"),
            new("ghost:stack|ghost:arc42/x.md", "x", Stack, DevbookLobe.Frontal, DevbookLobesNodeKind.GhostChapter, "arc42/x.md", Dashed: true)
        ];

        DevbookLobesEdge[] edges =
        [
            new("brain|projects/plan.md", "brain|people/ann.md", "related"),
            new("brain|projects/plan.md", "repo:backlog|arc42/adr.md", DevbookLobesReader.RepoLinkEdgeKind, Label: "backlog"),
            new("brain|hub:repo:backlog", "repo:backlog|arc42/adr.md", DevbookLobesReader.BundleEdgeKind),
            new("brain|hub:ghost:stack", "ghost:stack|ghost:arc42/x.md", DevbookLobesReader.BundleEdgeKind, Dashed: true)
        ];

        DevbookLobesScope[] scopes =
        [
            new(Brain, "Brain", DevbookLobesScopeKind.Brain, null, true, null,
                Counts((DevbookLobe.Frontal, 1), (DevbookLobe.Temporal, 1), (DevbookLobe.Dust, 1), (DevbookLobe.Stem, 1), (DevbookLobe.Occipital, 2)), 3, 2),
            new(Backlog, "backlog", DevbookLobesScopeKind.Devbook, "backlog", true, null,
                Counts((DevbookLobe.Stem, 1), (DevbookLobe.Frontal, 1)), 2, 2),
            new(Stack, "stack", DevbookLobesScopeKind.Ghost, "stack", false, "not on this machine",
                Counts((DevbookLobe.Frontal, 1)), 1, 1)
        ];

        return new DevbookLobesGraph(true, null, scopes, nodes, edges);
    }
}
