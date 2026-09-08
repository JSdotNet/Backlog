namespace Backlog.Infrastructure.Knowledge.UnitTests;

/// <summary>
/// The scope projection, which is the second cross-language rule this change
/// carries: the database stores the graph once and unprojected, so the rule that
/// used to produce the six scoped <c>graph.json</c> files now runs on the reading
/// side, and the two statements of it have to agree.
///
/// <para>They were checked against each other on a real corpus while this was
/// written — the C# projection reproduced the committed artifacts exactly on all
/// six scopes, node count, edge count and boundary count alike. That check cannot
/// be a test here, because slice 5 deletes the artifacts it compares against, so
/// what stays is this: the three rules of <c>projectScope</c> stated as cases, each
/// one arranged so that getting it wrong changes the answer.</para>
/// </summary>
public class KnowledgeScopeProjectionTests
{
    [Fact]
    public void The_repository_scope_keeps_everything()
    {
        var projection = KnowledgeScopeProjection.Project(
            KnowledgeDatabaseSchema.RepositoryScope,
            [Chapter("a", ".domain/inbox/domain.md"), Chapter("b", ".tech/shared.md")],
            [Edge("related", "a", "b")]);

        Assert.Equal(2, projection.Nodes.Count);
        Assert.Single(projection.Edges);
        Assert.DoesNotContain(projection.Nodes, node => node.OutOfScope);
    }

    /// <summary>
    /// Membership is decided by path, not by the folder column. They agree for
    /// every chapter in a knowledge folder and disagree for a node outside them,
    /// which carries no folder at all — so a folder filter would silently drop the
    /// boundary nodes the map is supposed to show as stubs.
    /// </summary>
    [Fact]
    public void A_folder_scope_keeps_the_folder_and_the_nodes_beneath_it()
    {
        var projection = KnowledgeScopeProjection.Project(
            ".domain",
            [
                Chapter("root", ".domain"),
                Chapter("inbox", ".domain/inbox/domain.md"),
                Chapter("tech", ".tech/shared.md"),
                External("external")
            ],
            []);

        Assert.Equal(["root", "inbox"], projection.Nodes.Select(node => node.Id));
    }

    /// <summary>
    /// Only an outbound reference pulls a boundary node in. An unrelated document
    /// merely pointing at this scope must not drag its whole neighbourhood into the
    /// scoped graph — which is the difference between a map of one folder and a map
    /// of everything that ever mentioned it.
    /// </summary>
    [Fact]
    public void An_outbound_reference_pulls_its_target_in_as_a_boundary_node()
    {
        var projection = KnowledgeScopeProjection.Project(
            ".domain",
            [Chapter("inbox", ".domain/inbox/domain.md"), Chapter("tech", ".tech/shared.md")],
            [Edge("depends-on", "inbox", "tech")]);

        var boundary = Assert.Single(projection.Nodes, node => node.OutOfScope);

        Assert.Equal("tech", boundary.Id);
        Assert.Single(projection.Edges);
    }

    [Fact]
    public void An_inbound_reference_pulls_nothing_in()
    {
        var projection = KnowledgeScopeProjection.Project(
            ".domain",
            [Chapter("inbox", ".domain/inbox/domain.md"), Chapter("tech", ".tech/shared.md")],
            [Edge("depends-on", "tech", "inbox")]);

        Assert.Single(projection.Nodes);
        Assert.Empty(projection.Edges);
    }

    /// <summary>
    /// An edge survives only when both ends are present and its source is in scope.
    /// Without the second half, a boundary node's own references would draw edges
    /// between two things neither of which this scope describes.
    /// </summary>
    [Fact]
    public void An_edge_out_of_a_boundary_node_is_not_drawn()
    {
        var projection = KnowledgeScopeProjection.Project(
            ".domain",
            [
                Chapter("inbox", ".domain/inbox/domain.md"),
                Chapter("tech", ".tech/shared.md"),
                Chapter("design", ".design/color-scheme.md")
            ],
            [Edge("depends-on", "inbox", "tech"), Edge("related", "tech", "design")]);

        // `design` is reachable only through `tech`, which is itself only a stub
        // here, so neither it nor the edge to it belongs to this scope.
        Assert.Equal(["inbox", "tech"], projection.Nodes.Select(node => node.Id));
        Assert.Single(projection.Edges);
    }

    /// <summary>The flag on the row is about the corpus — a node with no folder —
    /// and the flag on a projection is about one scope. A node in scope keeps its
    /// own answer; a node pulled in as a stub is marked whatever it said
    /// before.</summary>
    [Fact]
    public void The_boundary_flag_is_a_fact_about_the_projection_not_about_the_row()
    {
        var projection = KnowledgeScopeProjection.Project(
            ".domain",
            [Chapter("inbox", ".domain/inbox/domain.md"), External("external")],
            [Edge("related", "inbox", "external")]);

        Assert.False(Assert.Single(projection.Nodes, node => node.Id == "inbox").OutOfScope);
        Assert.True(Assert.Single(projection.Nodes, node => node.Id == "external").OutOfScope);
    }

    private static KnowledgeNodeRow Chapter(string id, string path) =>
        new(id, "chapter", id, path.Split('/')[0].TrimStart('.'), path, null, null, null, "active", false, null, null, null, null);

    /// <summary>A reference target outside the knowledge folders: no folder, no
    /// path, and already flagged by the writer.</summary>
    private static KnowledgeNodeRow External(string id) =>
        new(id, "external", id, null, null, null, null, null, null, true, null, null, null, null);

    private static KnowledgeEdgeRow Edge(string type, string source, string target) =>
        new($"{source}->{target}:{type}", type, source, target);
}
