using System.Text.Json;

using Backlog.UI.Components.Knowledge;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The knowledge atlas reads the graphs the metadata generator writes rather than
/// the Markdown behind them, so what is worth pinning is the arranging: which
/// edges are drawn, which are only used to work out where a chapter belongs, and
/// how a folder shaped like <c>.domain</c> and one shaped like <c>.design</c> both
/// come out as a readable handful of clusters.
/// </summary>
public sealed class KnowledgeAtlasReaderTests
{
    private static readonly KnowledgeAtlasScope DomainScope = new("domain", "Domain", ".domain");

    /// <summary>Two contexts of two files, one flat file, and one link of each
    /// kind the generator writes.</summary>
    private static string Graph() => Json(new
    {
        elements = new
        {
            nodes = new object[]
            {
                Node(".domain/tasks/domain.md", "Tasks Domain", "domain", "file", "active", ".domain/tasks/domain.md"),
                Node(".domain/tasks/domain.md#entries", "Entries", "domain", "chapter", "draft", ".domain/tasks/domain.md"),
                Node(".domain/tasks/model.md", "Tasks Model", "domain", "file", "draft", ".domain/tasks/model.md"),
                Node(".domain/inbox/domain.md", "Inbox Domain", "domain", "file", "active", ".domain/inbox/domain.md"),
                Node(".domain/inbox/domain.md#capture", "Capture", "domain", "chapter", "proposed", ".domain/inbox/domain.md"),
                Node(".domain/context-map.md", "Context Map", "domain", "file", "active", ".domain/context-map.md"),
                Node(".arc42/05-building-block-view.md#backlog", "Backlog Building Block", "arc42", "chapter", "active", ".arc42/05-building-block-view.md", outOfScope: true)
            },
            edges = new object[]
            {
                Edge("contains", ".domain/tasks/domain.md", ".domain/tasks/domain.md#entries"),
                Edge("contains", ".domain/inbox/domain.md", ".domain/inbox/domain.md#capture"),
                Edge("related", ".domain/tasks/domain.md", ".domain/inbox/domain.md"),
                Edge("related", ".domain/inbox/domain.md", ".domain/tasks/domain.md"),
                Edge("depends-on", ".domain/tasks/model.md", ".domain/context-map.md"),
                Edge("implements", ".domain/tasks/domain.md", ".arc42/05-building-block-view.md#backlog"),
                Edge("related", ".domain/context-map.md", ".domain/nowhere.md")
            }
        }
    });

    private static object Node(string id, string label, string folder, string type, string status, string path, bool outOfScope = false) =>
        new { data = new { id, label, folder, type, status, path, outOfScope } };

    private static object Edge(string type, string source, string target) =>
        new { data = new { id = $"{type}:{source}->{target}", source, target, type } };

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static KnowledgeAtlasGraph Read(KnowledgeAtlasScope? scope = null) =>
        KnowledgeAtlasReader.Read(scope ?? DomainScope, Graph());

    /// <summary><c>contains</c> is the hierarchy, not a relationship. Drawing it
    /// would put an edge between every chapter and its own file — most of the
    /// graph, saying what the clustering already says.</summary>
    [Fact]
    public void The_hierarchy_is_used_for_grouping_and_never_drawn()
    {
        var graph = Read();

        Assert.DoesNotContain(graph.Edges, edge => edge.Kind == "contains");
        Assert.Contains(graph.Edges, edge => edge.Kind == "related");
        Assert.Contains(graph.Edges, edge => edge.Kind == "depends-on");
        Assert.Contains(graph.Edges, edge => edge.Kind == "implements");
    }

    /// <summary><c>related</c> is written on both chapters, so the same pair
    /// arrives twice and would be drawn at double weight.</summary>
    [Fact]
    public void A_link_written_from_both_ends_is_drawn_once()
    {
        var graph = Read();

        var between = graph.Edges.Count(edge =>
            (edge.Source == ".domain/tasks/domain.md" && edge.Target == ".domain/inbox/domain.md")
            || (edge.Source == ".domain/inbox/domain.md" && edge.Target == ".domain/tasks/domain.md"));

        Assert.Equal(1, between);
    }

    /// <summary>There is no second end to draw it to.</summary>
    [Fact]
    public void A_link_to_something_the_index_does_not_describe_is_dropped()
    {
        var graph = Read();

        Assert.DoesNotContain(graph.Edges, edge => edge.Target == ".domain/nowhere.md");
    }

    /// <summary>A file inside a directory groups by the directory — a bounded
    /// context — and a file sitting directly in the folder is its own group. One
    /// rule, and it gives a comparable handful of clusters for folders shaped very
    /// differently.</summary>
    [Fact]
    public void A_folder_scope_groups_by_context_and_falls_back_to_the_document()
    {
        var graph = Read();

        Assert.Equal("Tasks", NodeFor(graph, ".domain/tasks/domain.md").Group);

        // A chapter takes its file's group, not one of its own.
        Assert.Equal("Tasks", NodeFor(graph, ".domain/tasks/domain.md#entries").Group);
        Assert.Equal("Inbox", NodeFor(graph, ".domain/inbox/domain.md#capture").Group);

        // Flat in the folder, so the document names the group.
        Assert.Equal("Context Map", NodeFor(graph, ".domain/context-map.md").Group);
    }

    /// <summary>Reading everything at once, the five knowledge areas are the
    /// clusters — grouping by document there would be hundreds of them.</summary>
    [Fact]
    public void The_whole_repository_groups_by_folder()
    {
        var graph = KnowledgeAtlasReader.Read(KnowledgeAtlasScope.All, Graph());

        Assert.Equal("Domain", NodeFor(graph, ".domain/tasks/domain.md").Group);
        Assert.Equal("Architecture", NodeFor(graph, ".arc42/05-building-block-view.md#backlog").Group);
        Assert.Equal(2, graph.GroupCount);
    }

    /// <summary>Every group gets an index, and every node an ordinal within it, so
    /// the layout is the same twice for the same graph.</summary>
    [Fact]
    public void Groups_and_ordinals_are_assigned_so_the_picture_is_repeatable()
    {
        var first = Read();
        var second = Read();

        Assert.Equal(
            first.Nodes.Select(node => (node.Id, node.Group, node.GroupIndex, node.Ordinal)),
            second.Nodes.Select(node => (node.Id, node.Group, node.GroupIndex, node.Ordinal)));

        foreach (var group in first.Nodes.GroupBy(node => node.Group, StringComparer.Ordinal))
        {
            Assert.Equal([.. Enumerable.Range(0, group.Count())], [.. group.Select(node => node.Ordinal).Order()]);
        }
    }

    [Fact]
    public void Degrees_are_counted_from_the_drawn_edges_only()
    {
        var graph = Read();

        // One `related` (deduplicated) and one `implements` out; nothing in. The
        // `contains` edge to its own chapter is not counted.
        var backlog = NodeFor(graph, ".domain/tasks/domain.md");

        Assert.Equal(2, backlog.OutDegree);
        Assert.Equal(0, backlog.InDegree);

        Assert.Equal(1, NodeFor(graph, ".domain/context-map.md").InDegree);
    }

    /// <summary>In the whole-repository reading the folders' vocabularies sit side
    /// by side, and one shared list would have to be wrong about one of them:
    /// <c>proposed</c> is a real status in <c>.domain</c> and no status at all in
    /// <c>.tech</c>.</summary>
    [Fact]
    public void Each_node_wears_the_tone_its_own_folders_vocabulary_gives_it()
    {
        var graph = KnowledgeAtlasReader.Read(KnowledgeAtlasScope.All, Graph());

        Assert.Equal(
            KnowledgeStatus.Vocabulary(KnowledgeFolder.Domain).SlugFor("proposed"),
            NodeFor(graph, ".domain/inbox/domain.md#capture").ToneSlug);

        Assert.Equal(
            KnowledgeStatus.Vocabulary(KnowledgeFolder.Arc42).SlugFor("active"),
            NodeFor(graph, ".arc42/05-building-block-view.md#backlog").ToneSlug);
    }

    /// <summary>A node the index flagged is carried through, because the sheet
    /// says so rather than leaving the reader at a dead end.</summary>
    [Fact]
    public void A_node_from_outside_the_scope_is_kept_and_flagged()
    {
        var graph = Read();

        var outside = Assert.Single(graph.Nodes, node => node.OutOfScope);

        Assert.Equal(".arc42/05-building-block-view.md#backlog", outside.Id);
        Assert.Equal("Architecture", outside.Folder);
    }

    [Fact]
    public void An_index_with_no_graph_in_it_says_so_rather_than_drawing_nothing()
    {
        var graph = KnowledgeAtlasReader.Read(DomainScope, Json(new { stats = new { nodes = 0 } }));

        Assert.False(graph.Available);
        Assert.NotNull(graph.Message);
        Assert.Empty(graph.Nodes);
    }

    [Fact]
    public void An_index_that_names_no_documents_says_so()
    {
        var graph = KnowledgeAtlasReader.Read(DomainScope, Json(new
        {
            elements = new { nodes = Array.Empty<object>(), edges = Array.Empty<object>() }
        }));

        Assert.False(graph.Available);
        Assert.NotNull(graph.Message);
    }

    private static KnowledgeAtlasNode NodeFor(KnowledgeAtlasGraph graph, string id) =>
        Assert.Single(graph.Nodes, node => node.Id == id);
}
