using Backlog.Infrastructure.GitHub;
using Backlog.UI.Components.Diagrams;

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
        File.WriteAllText(Path.Combine(techPath, "technology-graph.md"), """
            # Technology graph
            ```meta
            status: draft
            order: ["shared.md", "desktop.md"]
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
        File.WriteAllText(Path.Combine(techPath, "technology-graph.md"), """
            # Technology graph
            ```meta
            status: draft
            order: ["shared.md"]
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

