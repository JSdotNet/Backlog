using Backlog.Infrastructure.GitHub;
using Backlog.UI.Components.Diagrams;
using Backlog.SharedKernel.Devbook;
using Backlog.UI.Components.Devbook;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class DevbookFolderSourceTests
{
    [Fact]
    public void Reports_when_no_repository_is_configured()
    {
        using var workspace = TestWorkspace.Create();
        var source = new DevbookFolderSource(new GitHubSettingsStore(workspace.SettingsPath));

        var location = source.Resolve(".tech");

        Assert.False(location.Available);
        Assert.Equal(DevbookFolderSource.NoRepositoryScoped, location.Message);
    }

    [Fact]
    public void Resolves_enabled_knowledge_folder_from_primary_clone()
    {
        using var workspace = TestWorkspace.Create();
        Directory.CreateDirectory(Path.Combine(workspace.RepositoryPath, ".tech"));
        var store = workspace.CreateStore();

        var source = new DevbookFolderSource(store);
        var location = source.Resolve(".tech");

        Assert.True(location.Available);
        Assert.Equal(Path.Combine(workspace.RepositoryPath, ".tech"), location.FullPath);
        Assert.Equal("JSdotNet/Backlog", location.RepositoryFullName);
    }

    [Fact]
    public void Reports_disabled_knowledge_folder()
    {
        using var workspace = TestWorkspace.Create();
        var store = workspace.CreateStore();
        store.SetDevbookFolder("backlog", ".tech", enabled: false, path: null);

        var location = new DevbookFolderSource(store).Resolve(".tech");

        Assert.False(location.Available);
        Assert.Contains("turned off", location.Message);
    }
}

public sealed class TechnologyDevbookReaderTests
{
    [Fact]
    public void Parses_layers_nodes_relationships_metadata_and_diagrams()
    {
        using var workspace = TestWorkspace.Create();
        var techPath = Path.Combine(workspace.RepositoryPath, ".tech");
        Directory.CreateDirectory(Path.Combine(techPath, "_meta"));
        File.WriteAllText(Path.Combine(techPath, "technology-graph.md"), """
            # Technology graph
            ```meta
            status: draft
            ```

            Repository technology overview.

            ```mermaid
            flowchart TB
                dotnet --> blazor
            ```
            """);
        File.WriteAllText(Path.Combine(techPath, "shared.md"), """
            # Shared Technologies
            ```meta
            status: accepted
            kind: layer
            ```

            Shared platform choices.

            ## .NET
            ```meta
            status: accepted
            kind: runtime
            version: 10.0
            depends-on: [".tech/desktop.md#blazor"]
            related: [".arc42/04-solution-strategy.md"]
            alternatives: ["node-js"]
            ```

            Cross-platform runtime.
            """);
        File.WriteAllText(Path.Combine(techPath, "desktop.md"), """
            # Desktop Stack
            ```meta
            status: proposed
            kind: layer
            ```

            Desktop UI choices.

            ## Blazor
            ```meta
            status: accepted
            kind: ui-framework
            ```

            Component model.
            """);
        File.WriteAllText(Path.Combine(techPath, "_meta", "graph.json"), """
            { "stats": { "nodes": 2, "edges": 1, "nodesByStatus": { "accepted": 2 } } }
            """);

        var location = new DevbookFolderLocation(".tech", true, null, null, null, techPath);

        var view = TechnologyDevbookReader.Read(location);

        Assert.True(view.Available);
        Assert.Equal("Technology graph", view.Title);
        Assert.Equal(["shared.md", "desktop.md"], view.Layers.Select(layer => layer.FileName));
        Assert.Equal(2, view.Layers.Sum(layer => layer.Nodes.Count));
        Assert.Single(view.Relationships);
        Assert.Single(view.Diagrams);
        Assert.Equal("mermaid", view.Diagrams[0].Language);
        Assert.Equal(2, view.Stats.Nodes);

        var dotnet = Assert.Single(view.Layers[0].Nodes);
        Assert.Equal(".NET", dotnet.Label);
        Assert.Equal("runtime", dotnet.Kind);
        Assert.Equal("10.0", dotnet.Metadata.Version);
        Assert.Equal([".tech/desktop.md#blazor"], dotnet.DependsOn);
        Assert.Equal([".arc42/04-solution-strategy.md"], dotnet.Related);
        Assert.Equal(["node-js"], dotnet.Alternatives);
        Assert.Equal(2, view.Graph.Nodes.Count);
        Assert.Single(view.Graph.Edges);
        Assert.Contains(view.Graph.Nodes, node => node.Id == ".tech/shared.md#net" && node.Kind == "runtime" && node.Layer == "Shared Technologies");
        Assert.Contains(view.Graph.Edges, edge => edge.Source == ".tech/shared.md#net" && edge.Target == ".tech/desktop.md#blazor" && edge.Label == "depends on");
    }

    [Fact]
    public void A_nodes_kind_is_read_from_type_first_and_from_the_legacy_kind_second()
    {
        // The corpus writes `type`, the field contract 16 names; `kind` is the old
        // `.tech` spelling the convention still parses. Reading only `kind` left
        // every current chapter a generic "technology".
        using var workspace = TestWorkspace.Create();
        var techPath = Path.Combine(workspace.RepositoryPath, ".tech");
        Directory.CreateDirectory(techPath);
        File.WriteAllText(Path.Combine(techPath, "technology-graph.md"), """
            # Technology graph
            ```meta
            status: adopted
            ```
            """);
        File.WriteAllText(Path.Combine(techPath, "shared.md"), """
            # Shared Technologies
            ```meta
            status: adopted
            ```

            ## Markdig
            ```meta
            status: adopted
            type: library
            ```

            Markdown parser.

            ## .NET
            ```meta
            status: adopted
            kind: runtime
            ```

            Cross-platform runtime.

            ## Both
            ```meta
            status: adopted
            type: tool
            kind: runtime
            ```

            Written mid-rename.

            ## Neither
            ```meta
            status: adopted
            ```

            Not classified.
            """);

        var view = TechnologyDevbookReader.Read(new DevbookFolderLocation(".tech", true, null, null, null, techPath));

        var kinds = view.Layers.Single().Nodes.ToDictionary(node => node.Label, node => node.Kind);
        Assert.Equal("library", kinds["Markdig"]);
        Assert.Equal("runtime", kinds[".NET"]);
        Assert.Equal("tool", kinds["Both"]);
        Assert.Equal("technology", kinds["Neither"]);
    }


    [Fact]
    public async Task Updates_layer_and_node_status_metadata()
    {
        using var workspace = TestWorkspace.Create();
        var techPath = Path.Combine(workspace.RepositoryPath, ".tech");
        Directory.CreateDirectory(techPath);
        File.WriteAllText(Path.Combine(techPath, "technology-graph.md"), """
            # Technology graph
            ```meta
            status: draft
            ```
            """);
        File.WriteAllText(Path.Combine(techPath, "shared.md"), """
            # Shared Technologies
            ```meta
            status: accepted
            kind: layer
            ```

            ## .NET
            ```meta
            status: accepted
            kind: runtime
            ```

            Cross-platform runtime.
            """);
        var service = new TechnologyDevbookService(new DevbookFolderSource(workspace.CreateStore()));

        await service.UpdateStatusAsync("backlog", ".tech/shared.md", "adopted", TestContext.Current.CancellationToken);
        await service.UpdateStatusAsync("backlog", ".tech/shared.md#net", "active", TestContext.Current.CancellationToken);
        var view = await service.ReadAsync("backlog", TestContext.Current.CancellationToken);

        var layer = Assert.Single(view.Layers);
        Assert.Equal("adopted", layer.Metadata.Status);
        Assert.Equal("active", Assert.Single(layer.Nodes).Status);
        var markdown = File.ReadAllText(Path.Combine(techPath, "shared.md"));
        Assert.Contains("status: adopted", markdown);
        Assert.Contains("status: active", markdown);
    }

    [Theory]
    [InlineData("mermaid")]
    [InlineData("mmd")]
    public void Diagram_view_identifies_renderable_mermaid_languages(string language)
    {
        Assert.True(DiagramView.IsDiagram(language));
        Assert.True(DiagramView.CanRender(language));
    }

    [Theory]
    [InlineData("plantuml")]
    [InlineData("dot")]
    public void Diagram_view_keeps_non_mermaid_diagrams_as_source_fallback(string language)
    {
        Assert.True(DiagramView.IsDiagram(language));
        Assert.False(DiagramView.CanRender(language));
    }

    /// <summary>
    /// Everything the atlas needs beyond what a lane view did: how much leans on a
    /// node, where it sits in the reading order, which tone its status wears, and
    /// which nodes are foundations.
    /// </summary>
    [Fact]
    public void The_graph_carries_degree_reading_order_tone_and_which_nodes_are_foundations()
    {
        using var workspace = TestWorkspace.Create();
        var techPath = WriteAtlasFolder(workspace.RepositoryPath);

        var view = TechnologyDevbookReader.Read(new DevbookFolderLocation(".tech", true, null, null, null, techPath));

        var dotnet = Assert.Single(view.Graph.Nodes, node => node.Id == ".tech/shared.md#net");
        var blazor = Assert.Single(view.Graph.Nodes, node => node.Id == ".tech/desktop.md#blazor");
        var winui = Assert.Single(view.Graph.Nodes, node => node.Id == ".tech/desktop.md#winui");

        // Two things depend on .NET and .NET depends on nothing, which is exactly
        // what makes it a foundation.
        Assert.Equal(2, dotnet.InDegree);
        Assert.Equal(0, dotnet.OutDegree);
        Assert.True(dotnet.IsFoundation);

        Assert.Equal(0, blazor.InDegree);
        Assert.Equal(1, blazor.OutDegree);
        Assert.False(blazor.IsFoundation);
        Assert.Equal(1, winui.OutDegree);

        // Reading order is the convention's — shared pinned first — not
        // alphabetical, which would put desktop first.
        Assert.Equal(0, dotnet.LayerIndex);
        Assert.Equal("shared.md", dotnet.LayerFileName);
        Assert.Equal(1, blazor.LayerIndex);
        Assert.Equal(0, blazor.OrdinalInLayer);
        Assert.Equal(1, winui.OrdinalInLayer);
    }

    /// <summary>Pinned as a theory against <c>DevbookStatus</c> so a change to
    /// one folder's tone mapping fails here rather than quietly repainting the
    /// atlas.</summary>
    [Theory]
    [InlineData("candidate", "ready")]
    [InlineData("trial", "draft")]
    [InlineData("adopted", "active")]
    [InlineData("hold", "blocked")]
    [InlineData("retired", "archived")]
    public void Every_rung_of_the_ladder_wears_the_tone_the_vocabulary_gives_it(string status, string tone)
    {
        using var workspace = TestWorkspace.Create();
        var techPath = WriteAtlasFolder(workspace.RepositoryPath, sharedStatus: status);

        var view = TechnologyDevbookReader.Read(new DevbookFolderLocation(".tech", true, null, null, null, techPath));

        var dotnet = Assert.Single(view.Graph.Nodes, node => node.Id == ".tech/shared.md#net");

        Assert.Equal(status, dotnet.Status);
        Assert.Equal(tone, dotnet.ToneSlug);
        Assert.Equal(DevbookStatus.Vocabulary(DevbookFolder.Tech).SlugFor(status), dotnet.ToneSlug);
    }

    /// <summary>A `depends-on` target outside `.tech` is a real chapter documented
    /// elsewhere. The derived index already knows its name, its folder and its
    /// status, so those are read rather than reverse-engineered from the slug.</summary>
    [Fact]
    public void A_reference_out_of_the_folder_is_named_from_the_index_rather_than_its_slug()
    {
        using var workspace = TestWorkspace.Create();
        var techPath = WriteAtlasFolder(workspace.RepositoryPath, dependsOnOutside: true);

        var view = TechnologyDevbookReader.Read(new DevbookFolderLocation(".tech", true, null, null, null, techPath));

        var outside = Assert.Single(view.Graph.Nodes, node => node.IsBoundary);

        Assert.Equal(".arc42/04-solution-strategy.md#technology-choices", outside.Id);
        Assert.Equal("Technology Choices", outside.Label);
        Assert.Equal("Architecture", outside.Layer);
        Assert.Equal("external", outside.Kind);
        Assert.Equal(-1, outside.LayerIndex);
        Assert.Equal(1, outside.InDegree);

        // Its status answers to .arc42's vocabulary, where `active` is Active — not
        // to this folder's, which does not define the word at all.
        Assert.Equal("active", outside.Status);
        Assert.Equal("active", outside.ToneSlug);

        // Not a foundation. A foundation is a technology this project chose to sit
        // on; this is a chapter somewhere else that happens to be referenced.
        Assert.False(outside.IsFoundation);
    }

    /// <summary>The index is generated, so a checkout that has not run the
    /// generator is a normal state. The graph still reads, with the boundary node
    /// named from its slug the way it always was.</summary>
    [Fact]
    public void A_reference_the_index_does_not_name_falls_back_to_its_slug()
    {
        using var workspace = TestWorkspace.Create();
        var techPath = WriteAtlasFolder(workspace.RepositoryPath, dependsOnOutside: true, indexNamesOutside: false);

        var view = TechnologyDevbookReader.Read(new DevbookFolderLocation(".tech", true, null, null, null, techPath));

        var outside = Assert.Single(view.Graph.Nodes, node => node.IsBoundary);

        Assert.Equal("technology choices", outside.Label);
        Assert.Equal("External reference", outside.Layer);
        Assert.Equal("unknown", outside.Status);
        Assert.Equal(string.Empty, outside.ToneSlug);
    }

    /// <summary>
    /// A malformed index reads the same as no index at all.
    ///
    /// <para>It did not. The graph read had no <c>try</c> around
    /// <c>JsonDocument.Parse</c>, so a half-written or hand-mangled
    /// <c>_meta/graph.json</c> threw out of <c>Read</c> and took the whole
    /// technology panel down — for a generated file whose complete absence the same
    /// method already treated as ordinary. Every other reader of a generated
    /// artifact in Devbook degrades instead; this one is now among them.</para>
    /// </summary>
    [Fact]
    public void A_malformed_index_does_not_stop_the_graph_being_read()
    {
        using var workspace = TestWorkspace.Create();
        var techPath = WriteAtlasFolder(workspace.RepositoryPath, dependsOnOutside: true);

        File.WriteAllText(Path.Combine(techPath, "_meta", "graph.json"), "{ \"stats\": { \"nodes\": ");

        var view = TechnologyDevbookReader.Read(new DevbookFolderLocation(".tech", true, null, null, null, techPath));

        Assert.True(view.Available);
        Assert.Equal(0, view.Stats.Nodes);
        Assert.Contains(view.Graph.Nodes, node => node.IsBoundary);
    }

    [Fact]
    public void A_missing_index_does_not_stop_the_graph_being_read()
    {
        using var workspace = TestWorkspace.Create();
        var techPath = WriteAtlasFolder(workspace.RepositoryPath, dependsOnOutside: true, writeGraphIndex: false);

        var view = TechnologyDevbookReader.Read(new DevbookFolderLocation(".tech", true, null, null, null, techPath));

        Assert.True(view.Available);
        Assert.Equal(0, view.Stats.Nodes);
        Assert.Contains(view.Graph.Nodes, node => node.IsBoundary);
    }

    /// <summary>
    /// A heading with punctuation in it anchors the way GitHub anchors it, because
    /// that is what every <c>depends-on</c> in the repository and every id in
    /// <c>_meta</c> was written against.
    ///
    /// <para>This is a regression test with a scar. The reader used to map every
    /// non-alphanumeric to a hyphen and collapse the runs, so
    /// <c>## ASP.NET Core Minimal APIs</c> became <c>asp-net-core-minimal-apis</c>
    /// while the repository said <c>aspnet-core-minimal-apis</c>. Nothing failed
    /// loudly: the two <c>depends-on</c> edges naming it simply missed, and the
    /// graph synthesised an external placeholder for a chapter sitting in the very
    /// same file. Punctuation is dropped; only whitespace becomes a hyphen.</para>
    /// </summary>
    [Fact]
    public void A_heading_with_punctuation_anchors_the_way_the_repository_writes_it()
    {
        using var workspace = TestWorkspace.Create();
        var techPath = Path.Combine(workspace.RepositoryPath, ".tech");
        Directory.CreateDirectory(techPath);

        File.WriteAllText(Path.Combine(techPath, "technology-graph.md"), "# Technology graph\n");
        File.WriteAllText(Path.Combine(techPath, "cloud.md"),
            "# Cloud Stack\n\n"
            + "## ASP.NET Core Minimal APIs\n```meta\nstatus: candidate\nkind: framework\n```\n\nThe HTTP surface.\n\n"
            + "## Azure Container Apps\n```meta\nstatus: candidate\nkind: hosting\ndepends-on: [\".tech/cloud.md#aspnet-core-minimal-apis\"]\n```\n\nWhere it runs.\n");

        var view = TechnologyDevbookReader.Read(new DevbookFolderLocation(".tech", true, null, null, null, techPath));

        Assert.Contains(view.Graph.Nodes, node => node.Id == ".tech/cloud.md#aspnet-core-minimal-apis");

        // The edge resolves inside the folder, so nothing is synthesised for it.
        Assert.DoesNotContain(view.Graph.Nodes, node => node.IsBoundary);
        Assert.Equal(2, view.Graph.Nodes.Count);

        var target = Assert.Single(view.Graph.Nodes, node => node.Label == "ASP.NET Core Minimal APIs");
        Assert.Equal(1, target.InDegree);
        Assert.True(target.IsFoundation);
    }

    /// <summary>A node documented in another folder is walked like any other but is
    /// no technology of this stack: it falls in no family and no count.</summary>
    [Fact]
    public void A_boundary_node_is_walked_but_not_counted_as_a_technology()
    {
        using var workspace = TestWorkspace.Create();
        var techPath = WriteAtlasFolder(workspace.RepositoryPath, dependsOnOutside: true);

        var graph = TechnologyDevbookReader.Read(new DevbookFolderLocation(".tech", true, null, null, null, techPath)).Graph;

        var outside = Assert.Single(graph.Nodes, node => node.IsBoundary);
        Assert.Equal(TechnologyTypeFamily.Other, outside.Family);
        Assert.Equal(1, outside.UsedBy);

        var blazor = Assert.Single(graph.Nodes, node => node.Id == ".tech/desktop.md#blazor");
        Assert.Equal(2, blazor.RestsOn);
        Assert.Contains(outside.Id, blazor.DependencyIds);

        Assert.Equal(3, graph.CountsByType.Values.Sum());
        Assert.Equal(3, graph.CountsByStatus.Values.Sum());
        Assert.Equal(3, graph.CountsByFamily.Values.Sum());
        Assert.False(graph.CountsByType.ContainsKey("external"));
    }

    /// <summary>A folder with two layers, one dependency inside it and optionally
    /// one out of it. Enough shape to answer every question above without each
    /// test writing its own Markdown.</summary>
    private static string WriteAtlasFolder(
        string repositoryPath,
        string sharedStatus = "adopted",
        bool dependsOnOutside = false,
        bool indexNamesOutside = true,
        bool writeGraphIndex = true)
    {
        var techPath = Path.Combine(repositoryPath, ".tech");
        Directory.CreateDirectory(techPath);

        File.WriteAllText(Path.Combine(techPath, "technology-graph.md"), "# Technology graph\n");

        var outside = dependsOnOutside ? ", \".arc42/04-solution-strategy.md#technology-choices\"" : string.Empty;

        File.WriteAllText(Path.Combine(techPath, "shared.md"),
            "# Shared Technologies\n\n## .NET\n```meta\nstatus: " + sharedStatus + "\nkind: runtime\n```\n\nCross-platform runtime.\n");

        File.WriteAllText(Path.Combine(techPath, "desktop.md"),
            "# Desktop Stack\n\n"
            + "## Blazor\n```meta\nstatus: candidate\nkind: ui-framework\ndepends-on: [\".tech/shared.md#net\"" + outside + "]\n```\n\nComponent model.\n\n"
            + "## WinUI\n```meta\nstatus: adopted\nkind: ui-framework\ndepends-on: [\".tech/shared.md#net\"]\n```\n\nThe window it all sits in.\n");

        if (writeGraphIndex)
        {
            // Nothing else of this fixture lives under `_meta`, so the folder the
            // graph rollup goes in is this fixture's to create.
            Directory.CreateDirectory(Path.Combine(techPath, "_meta"));

            var boundary = dependsOnOutside && indexNamesOutside
                ? ", { \"data\": { \"id\": \".arc42/04-solution-strategy.md#technology-choices\", \"label\": \"Technology Choices\","
                  + " \"type\": \"chapter\", \"folder\": \"arc42\", \"path\": \".arc42/04-solution-strategy.md\","
                  + " \"status\": \"active\", \"outOfScope\": true } }"
                : string.Empty;

            File.WriteAllText(Path.Combine(techPath, "_meta", "graph.json"),
                "{ \"stats\": { \"nodes\": 3, \"edges\": 3, \"nodesByStatus\": { \"adopted\": 2 } },"
                + " \"elements\": { \"nodes\": [ { \"data\": { \"id\": \".tech/shared.md#net\", \"label\": \".NET\", \"folder\": \"tech\" } }"
                + boundary
                + " ], \"edges\": [] } }");
        }

        return techPath;
    }

    /// <summary>
    /// The layers read in the convention's order from their file names —
    /// <c>shared.md</c> first, <c>tooling.md</c> last, the rest by name — and
    /// none is dropped.
    ///
    /// <para>The regression this holds: the retired <c>_reading-order.json</c> was
    /// consulted as the whole list, so a layer it did not declare silently fell
    /// out of the graph. The file is written here, declaring one layer, to show it
    /// is now ignored outright (local ADR 0016).</para>
    /// </summary>
    [Fact]
    public void Every_layer_reads_in_the_convention_s_order_whatever_a_stray_reading_order_file_says()
    {
        using var workspace = TestWorkspace.Create();
        var techPath = Path.Combine(workspace.RepositoryPath, ".tech");
        Directory.CreateDirectory(techPath);
        File.WriteAllText(
            Path.Combine(techPath, "_reading-order.json"),
            "{ \"version\": 1, \"scope\": \".tech\", \"directories\": { \".tech\": "
            + "{ \"root\": \"technology-graph.md\", \"order\": [\"desktop.md\"] } } }");

        File.WriteAllText(Path.Combine(techPath, "technology-graph.md"), "# Technology graph\n");
        foreach (var (file, title) in new[] { ("tooling.md", "Tooling"), ("desktop.md", "Desktop"), ("shared.md", "Shared"), ("cloud.md", "Cloud") })
        {
            File.WriteAllText(Path.Combine(techPath, file),
                $"# {title}\n\n## {title} thing\n```meta\nstatus: adopted\nkind: tool\n```\n\nWhat it is.\n");
        }

        var view = TechnologyDevbookReader.Read(new DevbookFolderLocation(".tech", true, null, null, null, techPath));

        Assert.Equal(["shared.md", "cloud.md", "desktop.md", "tooling.md"], view.Layers.Select(layer => layer.FileName));
    }
}

/// <summary>
/// The measures the Technology views compute from <c>depends-on</c>, as
/// <c>.devbook/domain/devbook/features.md#technology-view</c> defines them.
///
/// <para>One fixture answers every question: a five-deep stack whose longest chain
/// is known (Blazor → Razor Components → ASP.NET Core → C# Language → .NET
/// Runtime), shared dependencies (C# Language and .NET Runtime each sit under
/// several), and a two-node cycle in the tooling layer that a third technology
/// leans on.</para>
/// </summary>
public sealed class TechnologyGraphMeasuresTests : IDisposable
{
    private const string Runtime = ".tech/shared.md#net-runtime";
    private const string CSharp = ".tech/shared.md#c-language";
    private const string AspNet = ".tech/desktop.md#aspnet-core";
    private const string Razor = ".tech/desktop.md#razor-components";
    private const string Blazor = ".tech/desktop.md#blazor-server";
    private const string Lint = ".tech/tooling.md#lint";
    private const string Format = ".tech/tooling.md#format";
    private const string Probe = ".tech/tooling.md#probe";

    [Fact]
    public void Used_by_counts_each_direct_user_once()
    {
        var graph = ReadFixture();

        Assert.Equal(3, Node(graph, Runtime).UsedBy);
        Assert.Equal(2, Node(graph, CSharp).UsedBy);
        Assert.Equal(2, Node(graph, AspNet).UsedBy);
        Assert.Equal(1, Node(graph, Razor).UsedBy);
        Assert.Equal(0, Node(graph, Blazor).UsedBy);
        Assert.True(Node(graph, Blazor).IsLeaf);
        Assert.False(Node(graph, Razor).IsLeaf);
    }

    [Fact]
    public void Breaks_if_it_changes_counts_the_users_transitively_each_once()
    {
        var graph = ReadFixture();

        // Blazor Server reaches ASP.NET Core two ways — directly and through Razor
        // Components — and is counted once (requirements.md, "A user reached two ways").
        Assert.Equal(2, Node(graph, AspNet).BreaksIfItChanges);
        Assert.Equal(3, Node(graph, CSharp).BreaksIfItChanges);
        Assert.Equal(5, Node(graph, Runtime).BreaksIfItChanges);
        Assert.Equal(0, Node(graph, Blazor).BreaksIfItChanges);
    }

    [Fact]
    public void Rests_on_counts_the_depends_on_transitively_each_once()
    {
        var graph = ReadFixture();

        Assert.Equal(0, Node(graph, Runtime).RestsOn);
        Assert.True(Node(graph, Runtime).IsFoundation);
        Assert.Equal(1, Node(graph, CSharp).RestsOn);
        Assert.Equal(2, Node(graph, AspNet).RestsOn);
        Assert.Equal(3, Node(graph, Razor).RestsOn);
        Assert.Equal(4, Node(graph, Blazor).RestsOn);
    }

    [Fact]
    public void Depth_is_the_longest_chain_to_a_foundation()
    {
        var graph = ReadFixture();

        Assert.Equal(0, Node(graph, Runtime).Depth);
        Assert.Equal(1, Node(graph, CSharp).Depth);
        // Two paths down — directly, and through C# Language — and the longer counts.
        Assert.Equal(2, Node(graph, AspNet).Depth);
        Assert.Equal(3, Node(graph, Razor).Depth);
        Assert.Equal(4, Node(graph, Blazor).Depth);
    }

    [Fact]
    public void Complexity_is_rests_on_plus_depth_and_wears_its_band()
    {
        var graph = ReadFixture();

        Assert.Equal(4, Node(graph, AspNet).Complexity);
        Assert.Equal(TechnologyComplexityBand.Low, Node(graph, AspNet).ComplexityBand);
        Assert.Equal(6, Node(graph, Razor).Complexity);
        Assert.Equal(TechnologyComplexityBand.Medium, Node(graph, Razor).ComplexityBand);
        Assert.Equal(8, Node(graph, Blazor).Complexity);
        Assert.Equal(TechnologyComplexityBand.Medium, Node(graph, Blazor).ComplexityBand);
    }

    [Theory]
    [InlineData(0, TechnologyComplexityBand.Low)]
    [InlineData(5, TechnologyComplexityBand.Low)]
    [InlineData(6, TechnologyComplexityBand.Medium)]
    [InlineData(9, TechnologyComplexityBand.Medium)]
    [InlineData(10, TechnologyComplexityBand.High)]
    [InlineData(31, TechnologyComplexityBand.High)]
    public void The_complexity_bands_break_at_six_and_ten(int score, TechnologyComplexityBand band) =>
        Assert.Equal(band, TechnologyComplexity.BandFor(score));

    [Theory]
    [InlineData("framework", TechnologyTypeFamily.Framework)]
    [InlineData("platform", TechnologyTypeFamily.Platform)]
    [InlineData("runtime", TechnologyTypeFamily.Runtime)]
    [InlineData("language", TechnologyTypeFamily.LanguageAndPackage)]
    [InlineData("package", TechnologyTypeFamily.LanguageAndPackage)]
    [InlineData("library", TechnologyTypeFamily.LibraryToolAndPlugin)]
    [InlineData("tool", TechnologyTypeFamily.LibraryToolAndPlugin)]
    [InlineData("plugin", TechnologyTypeFamily.LibraryToolAndPlugin)]
    [InlineData("skill", TechnologyTypeFamily.Skill)]
    [InlineData("agent", TechnologyTypeFamily.AgentServiceAndFormat)]
    [InlineData("service", TechnologyTypeFamily.AgentServiceAndFormat)]
    [InlineData("format", TechnologyTypeFamily.AgentServiceAndFormat)]
    [InlineData("protocol", TechnologyTypeFamily.Protocol)]
    [InlineData("Plugin", TechnologyTypeFamily.LibraryToolAndPlugin)]
    [InlineData("technology", TechnologyTypeFamily.Other)]
    [InlineData("external", TechnologyTypeFamily.Other)]
    public void Each_of_the_thirteen_types_falls_into_its_family(string type, TechnologyTypeFamily family) =>
        Assert.Equal(family, TechnologyTypeFamilies.Of(type));

    [Fact]
    public void Each_node_carries_its_family_and_whether_it_is_adopted()
    {
        var graph = ReadFixture();

        Assert.Equal(TechnologyTypeFamily.LibraryToolAndPlugin, Node(graph, Lint).Family);
        Assert.Equal(TechnologyTypeFamily.LibraryToolAndPlugin, Node(graph, Format).Family);
        Assert.Equal(TechnologyTypeFamily.Skill, Node(graph, Probe).Family);
        Assert.Equal(TechnologyTypeFamily.LanguageAndPackage, Node(graph, CSharp).Family);

        Assert.True(Node(graph, Runtime).IsAdopted);
        Assert.False(Node(graph, Razor).IsAdopted);
        Assert.False(Node(graph, Lint).IsAdopted);
        Assert.False(Node(graph, Probe).IsAdopted);
    }

    [Fact]
    public void Each_node_carries_the_ids_of_its_direct_dependencies_and_users()
    {
        var graph = ReadFixture();

        // Declaration order, and a target named twice is named once.
        Assert.Equal([Runtime, CSharp], Node(graph, AspNet).DependencyIds);
        // Graph order: the reading order of the layers, then document order.
        Assert.Equal([CSharp, AspNet, Probe], Node(graph, Runtime).UserIds);
        Assert.Empty(Node(graph, Runtime).DependencyIds);
        Assert.Empty(Node(graph, Blazor).UserIds);
    }

    [Fact]
    public void The_longest_chain_runs_from_the_deepest_technology_to_a_foundation()
    {
        var graph = ReadFixture();

        Assert.Equal([Blazor, Razor, AspNet, CSharp, Runtime], graph.LongestChain);
    }

    [Fact]
    public void A_cycle_is_reported_and_its_walks_end()
    {
        var graph = ReadFixture();

        var cycle = Assert.Single(graph.Cycles);
        Assert.Equal([Lint, Format], cycle);
        Assert.True(Node(graph, Lint).InCycle);
        Assert.True(Node(graph, Format).InCycle);
        Assert.False(Node(graph, Probe).InCycle);

        // Each counted once and never itself, however many times the cycle goes round.
        Assert.Equal(1, Node(graph, Lint).RestsOn);
        Assert.Equal(2, Node(graph, Lint).UsedBy);
        Assert.Equal(2, Node(graph, Lint).BreaksIfItChanges);
        Assert.Equal(3, Node(graph, Probe).RestsOn);

        // A hop inside the cycle is not a hop down the stack.
        Assert.Equal(0, Node(graph, Lint).Depth);
        Assert.Equal(1, Node(graph, Probe).Depth);
    }

    [Fact]
    public void A_technology_that_names_itself_is_a_cycle_of_one()
    {
        using var workspace = TestWorkspace.Create();
        var techPath = Path.Combine(workspace.RepositoryPath, ".tech");
        Directory.CreateDirectory(techPath);
        File.WriteAllText(Path.Combine(techPath, "technology-graph.md"), "# Technology graph\n");
        File.WriteAllText(Path.Combine(techPath, "shared.md"),
            "# Shared\n\n## Loop\n```meta\nstatus: adopted\ntype: tool\ndepends-on: [\".tech/shared.md#loop\"]\n```\n\nNames itself.\n");

        var graph = TechnologyDevbookReader.Read(new DevbookFolderLocation(".tech", true, null, null, null, techPath)).Graph;

        Assert.Equal([".tech/shared.md#loop"], Assert.Single(graph.Cycles));
        var loop = Assert.Single(graph.Nodes);
        Assert.Equal(0, loop.RestsOn);
        Assert.Equal(0, loop.UsedBy);
        Assert.Equal(0, loop.Depth);
        Assert.Equal([".tech/shared.md#loop"], graph.LongestChain);
    }

    /// <summary>A cycle is one step down the stack: its members share the depth of
    /// the longest chain that leaves it, through whichever member it leaves by.</summary>
    [Fact]
    public void A_cycle_s_members_share_the_depth_of_the_chain_that_leaves_it()
    {
        using var workspace = TestWorkspace.Create();
        var techPath = Path.Combine(workspace.RepositoryPath, ".tech");
        Directory.CreateDirectory(techPath);
        File.WriteAllText(Path.Combine(techPath, "technology-graph.md"), "# Technology graph\n");
        File.WriteAllText(Path.Combine(techPath, "shared.md"),
            "# Shared\n\n"
            + "## Base\n```meta\nstatus: adopted\ntype: runtime\n```\n\nFoundation.\n\n"
            + "## Ping\n```meta\nstatus: adopted\ntype: tool\ndepends-on: [\".tech/shared.md#pong\"]\n```\n\nOne half.\n\n"
            + "## Pong\n```meta\nstatus: adopted\ntype: tool\ndepends-on: [\".tech/shared.md#ping\", \".tech/shared.md#base\"]\n```\n\nOther half.\n");

        var graph = TechnologyDevbookReader.Read(new DevbookFolderLocation(".tech", true, null, null, null, techPath)).Graph;

        Assert.Equal(1, Node(graph, ".tech/shared.md#ping").Depth);
        Assert.Equal(1, Node(graph, ".tech/shared.md#pong").Depth);
        Assert.Equal(2, Node(graph, ".tech/shared.md#ping").RestsOn);
        // Ping is first in graph order and leaves the cycle through Pong, which is listed.
        Assert.Equal([".tech/shared.md#ping", ".tech/shared.md#pong", ".tech/shared.md#base"], graph.LongestChain);
    }

    [Fact]
    public void An_acyclic_graph_reports_no_cycle()
    {
        using var workspace = TestWorkspace.Create();
        var techPath = Path.Combine(workspace.RepositoryPath, ".tech");
        Directory.CreateDirectory(techPath);
        File.WriteAllText(Path.Combine(techPath, "technology-graph.md"), "# Technology graph\n");
        WriteStack(techPath, withCycle: false);

        var graph = TechnologyDevbookReader.Read(new DevbookFolderLocation(".tech", true, null, null, null, techPath)).Graph;

        Assert.Empty(graph.Cycles);
        Assert.DoesNotContain(graph.Nodes, node => node.InCycle);
    }

    [Fact]
    public void The_graph_counts_depends_on_across_each_pair_of_layers()
    {
        var graph = ReadFixture();

        // Edges inside a layer are not cross-layer; ASP.NET Core → .NET Runtime,
        // ASP.NET Core → C# Language and Razor Components → C# Language are.
        Assert.Equal(
            [new TechnologyLayerLink("Desktop", "Shared", 3), new TechnologyLayerLink("Tooling", "Shared", 1)],
            graph.LayerLinks);
    }

    [Fact]
    public void The_graph_counts_its_technologies_by_type_family_and_status()
    {
        var graph = ReadFixture();

        Assert.Equal(1, graph.CountsByType["runtime"]);
        Assert.Equal(1, graph.CountsByType["language"]);
        Assert.Equal(3, graph.CountsByType["framework"]);
        Assert.Equal(1, graph.CountsByType["tool"]);
        Assert.Equal(1, graph.CountsByType["plugin"]);
        Assert.Equal(1, graph.CountsByType["skill"]);
        Assert.Equal(8, graph.CountsByType.Values.Sum());

        Assert.Equal(2, graph.CountsByFamily[TechnologyTypeFamily.LibraryToolAndPlugin]);
        Assert.Equal(3, graph.CountsByFamily[TechnologyTypeFamily.Framework]);

        Assert.Equal(4, graph.CountsByStatus["adopted"]);
        Assert.Equal(1, graph.CountsByStatus["trial"]);
        Assert.Equal(1, graph.CountsByStatus["candidate"]);
        Assert.Equal(1, graph.CountsByStatus["hold"]);
        Assert.Equal(1, graph.CountsByStatus["retired"]);
    }

    [Fact]
    public void An_empty_folder_has_no_chain_no_cycle_and_no_counts()
    {
        Assert.Empty(TechnologyGraphData.Empty.LongestChain);
        Assert.Empty(TechnologyGraphData.Empty.Cycles);
        Assert.Empty(TechnologyGraphData.Empty.LayerLinks);
        Assert.Empty(TechnologyGraphData.Empty.CountsByType);
    }

    private static TechnologyGraphNode Node(TechnologyGraphData graph, string id) =>
        Assert.Single(graph.Nodes, node => node.Id == id);

    private TechnologyGraphData ReadFixture()
    {
        var techPath = Path.Combine(_repositoryPath, ".tech");
        Directory.CreateDirectory(techPath);
        File.WriteAllText(Path.Combine(techPath, "technology-graph.md"), "# Technology graph\n");
        WriteStack(techPath, withCycle: true);

        return TechnologyDevbookReader.Read(new DevbookFolderLocation(".tech", true, null, null, null, techPath)).Graph;
    }

    // Typed as the interface and the path: the workspace's own type is file-local.
    private readonly IDisposable _workspace;
    private readonly string _repositoryPath;

    public TechnologyGraphMeasuresTests()
    {
        var workspace = TestWorkspace.Create();
        _workspace = workspace;
        _repositoryPath = workspace.RepositoryPath;
    }

    public void Dispose() => _workspace.Dispose();

    private static void WriteStack(string techPath, bool withCycle)
    {
        static string Chapter(string title, string type, string status, params string[] dependsOn) =>
            $"## {title}\n```meta\nstatus: {status}\ntype: {type}\n"
            + (dependsOn.Length == 0 ? string.Empty : "depends-on: [" + string.Join(", ", dependsOn.Select(id => $"\"{id}\"")) + "]\n")
            + "```\n\nWhat it is.\n\n";

        File.WriteAllText(Path.Combine(techPath, "shared.md"),
            "# Shared\n\n"
            + Chapter(".NET Runtime", "runtime", "adopted")
            + Chapter("C# Language", "language", "adopted", Runtime));

        File.WriteAllText(Path.Combine(techPath, "desktop.md"),
            "# Desktop\n\n"
            // C# Language is named twice, and counted once.
            + Chapter("ASP.NET Core", "framework", "adopted", Runtime, CSharp, CSharp)
            + Chapter("Razor Components", "framework", "trial", AspNet, CSharp)
            + Chapter("Blazor Server", "framework", "candidate", Razor, AspNet));

        File.WriteAllText(Path.Combine(techPath, "tooling.md"),
            "# Tooling\n\n"
            + Chapter("Lint", "tool", "hold", withCycle ? new[] { Format } : [])
            + Chapter("Format", "plugin", "adopted", Lint)
            + Chapter("Probe", "skill", "retired", Lint, Runtime));
    }
}

file sealed class TestWorkspace : IDisposable
{
    private TestWorkspace(string root)
    {
        Root = root;
        RepositoryPath = Path.Combine(root, "repo");
        SettingsPath = Path.Combine(root, "github.json");
        Directory.CreateDirectory(RepositoryPath);
    }

    public string Root { get; }

    public string RepositoryPath { get; }

    public string SettingsPath { get; }

    public static TestWorkspace Create() => new(Path.Combine(Path.GetTempPath(), "backlog-devbook-tests", Guid.NewGuid().ToString("n")));

    public GitHubSettingsStore CreateStore()
    {
        var store = new GitHubSettingsStore(SettingsPath);
        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);
        store.SetRepositories(repositories);
        store.SetCloneDirectory("backlog", RepositoryPath);
        return store;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
    }

}

