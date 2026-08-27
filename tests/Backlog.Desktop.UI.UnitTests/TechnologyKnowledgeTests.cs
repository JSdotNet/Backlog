using Backlog.Infrastructure.GitHub;
using Backlog.UI.Components.Diagrams;
using Backlog.UI.Components.Knowledge;

namespace Backlog.Desktop.UI.UnitTests;

public sealed class KnowledgeFolderSourceTests
{
    [Fact]
    public void Reports_when_no_repository_is_configured()
    {
        using var workspace = TestWorkspace.Create();
        var source = new KnowledgeFolderSource(new GitHubSettingsStore(workspace.SettingsPath));

        var location = source.Resolve(".tech");

        Assert.False(location.Available);
        Assert.Contains("Configure a repository", location.Message);
    }

    [Fact]
    public void Resolves_enabled_knowledge_folder_from_primary_clone()
    {
        using var workspace = TestWorkspace.Create();
        Directory.CreateDirectory(Path.Combine(workspace.RepositoryPath, ".tech"));
        var store = workspace.CreateStore();

        var source = new KnowledgeFolderSource(store);
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
        store.SetKnowledgeFolder("backlog", ".tech", enabled: false, path: null);

        var location = new KnowledgeFolderSource(store).Resolve(".tech");

        Assert.False(location.Available);
        Assert.Contains("turned off", location.Message);
    }
}

public sealed class TechnologyKnowledgeReaderTests
{
    [Fact]
    public void Parses_layers_nodes_relationships_metadata_and_diagrams()
    {
        using var workspace = TestWorkspace.Create();
        var techPath = Path.Combine(workspace.RepositoryPath, ".tech");
        Directory.CreateDirectory(Path.Combine(techPath, "_meta"));
        WriteTechIndex(techPath, "shared.md", "desktop.md");
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

        var location = new KnowledgeFolderLocation(".tech", true, null, null, null, techPath);

        var view = TechnologyKnowledgeReader.Read(location);

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
    public async Task Updates_layer_and_node_status_metadata()
    {
        using var workspace = TestWorkspace.Create();
        var techPath = Path.Combine(workspace.RepositoryPath, ".tech");
        Directory.CreateDirectory(techPath);
        WriteTechIndex(techPath, "shared.md");
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
        var service = new TechnologyKnowledgeService(new KnowledgeFolderSource(workspace.CreateStore()));

        await service.UpdateStatusAsync("backlog", ".tech/shared.md", "adopted");
        await service.UpdateStatusAsync("backlog", ".tech/shared.md#net", "active");
        var view = await service.ReadAsync("backlog");

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
    /// The committed reading order for a <c>.tech</c> fixture. The layer sequence
    /// lives here now rather than in the root document's fence, so a fixture that
    /// cares about order writes the index the reader actually consults.
    /// </summary>
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

        var view = TechnologyKnowledgeReader.Read(new KnowledgeFolderLocation(".tech", true, null, null, null, techPath));

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

        // Reading order is the committed one — shared before desktop — not
        // alphabetical, which would put desktop first.
        Assert.Equal(0, dotnet.LayerIndex);
        Assert.Equal("shared.md", dotnet.LayerFileName);
        Assert.Equal(1, blazor.LayerIndex);
        Assert.Equal(0, blazor.OrdinalInLayer);
        Assert.Equal(1, winui.OrdinalInLayer);
    }

    /// <summary>Pinned as a theory against <c>KnowledgeStatus</c> so a change to
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

        var view = TechnologyKnowledgeReader.Read(new KnowledgeFolderLocation(".tech", true, null, null, null, techPath));

        var dotnet = Assert.Single(view.Graph.Nodes, node => node.Id == ".tech/shared.md#net");

        Assert.Equal(status, dotnet.Status);
        Assert.Equal(tone, dotnet.ToneSlug);
        Assert.Equal(KnowledgeStatus.Vocabulary(KnowledgeFolder.Tech).SlugFor(status), dotnet.ToneSlug);
    }

    /// <summary>A `depends-on` target outside `.tech` is a real chapter documented
    /// elsewhere. The derived index already knows its name, its folder and its
    /// status, so those are read rather than reverse-engineered from the slug.</summary>
    [Fact]
    public void A_reference_out_of_the_folder_is_named_from_the_index_rather_than_its_slug()
    {
        using var workspace = TestWorkspace.Create();
        var techPath = WriteAtlasFolder(workspace.RepositoryPath, dependsOnOutside: true);

        var view = TechnologyKnowledgeReader.Read(new KnowledgeFolderLocation(".tech", true, null, null, null, techPath));

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

        var view = TechnologyKnowledgeReader.Read(new KnowledgeFolderLocation(".tech", true, null, null, null, techPath));

        var outside = Assert.Single(view.Graph.Nodes, node => node.IsBoundary);

        Assert.Equal("technology choices", outside.Label);
        Assert.Equal("External reference", outside.Layer);
        Assert.Equal("unknown", outside.Status);
        Assert.Equal(string.Empty, outside.ToneSlug);
    }

    [Fact]
    public void A_missing_index_does_not_stop_the_graph_being_read()
    {
        using var workspace = TestWorkspace.Create();
        var techPath = WriteAtlasFolder(workspace.RepositoryPath, dependsOnOutside: true, writeGraphIndex: false);

        var view = TechnologyKnowledgeReader.Read(new KnowledgeFolderLocation(".tech", true, null, null, null, techPath));

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
        WriteTechIndex(techPath, "cloud.md");

        File.WriteAllText(Path.Combine(techPath, "technology-graph.md"), "# Technology graph\n");
        File.WriteAllText(Path.Combine(techPath, "cloud.md"),
            "# Cloud Stack\n\n"
            + "## ASP.NET Core Minimal APIs\n```meta\nstatus: candidate\nkind: framework\n```\n\nThe HTTP surface.\n\n"
            + "## Azure Container Apps\n```meta\nstatus: candidate\nkind: hosting\ndepends-on: [\".tech/cloud.md#aspnet-core-minimal-apis\"]\n```\n\nWhere it runs.\n");

        var view = TechnologyKnowledgeReader.Read(new KnowledgeFolderLocation(".tech", true, null, null, null, techPath));

        Assert.Contains(view.Graph.Nodes, node => node.Id == ".tech/cloud.md#aspnet-core-minimal-apis");

        // The edge resolves inside the folder, so nothing is synthesised for it.
        Assert.DoesNotContain(view.Graph.Nodes, node => node.IsBoundary);
        Assert.Equal(2, view.Graph.Nodes.Count);

        var target = Assert.Single(view.Graph.Nodes, node => node.Label == "ASP.NET Core Minimal APIs");
        Assert.Equal(1, target.InDegree);
        Assert.True(target.IsFoundation);
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
        WriteTechIndex(techPath, "shared.md", "desktop.md");

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

    private static void WriteTechIndex(string techPath, params string[] layers)
    {
        Directory.CreateDirectory(Path.Combine(techPath, "_meta"));

        var entries = new List<string>
        {
            "{ \"type\": \"file\", \"name\": \"technology-graph.md\", \"path\": \".tech/technology-graph.md\", \"title\": \"Technology graph\", \"status\": \"draft\", \"root\": true }"
        };
        entries.AddRange(layers.Select(layer =>
            $"{{ \"type\": \"file\", \"name\": \"{layer}\", \"path\": \".tech/{layer}\", \"title\": \"{layer}\", \"status\": null }}"));

        File.WriteAllText(
            Path.Combine(techPath, "_meta", "index.json"),
            "{ \"schemaVersion\": 1, \"scope\": \".tech\", \"problems\": [], \"entries\": [" + string.Join(", ", entries) + "] }");
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

    public static TestWorkspace Create() => new(Path.Combine(Path.GetTempPath(), "backlog-knowledge-tests", Guid.NewGuid().ToString("n")));

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

