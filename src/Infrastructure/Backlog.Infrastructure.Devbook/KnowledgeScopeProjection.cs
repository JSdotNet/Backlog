namespace Backlog.Infrastructure.Knowledge;

/// <summary>
/// One scope's reading of the graph: the nodes inside a knowledge folder, the
/// boundary nodes its outbound references reach, and the edges between them.
///
/// <para>The database stores the graph once, unprojected, because "a scope" is a
/// filter and that is the point of ADR 0004 — twelve committed JSON artifacts
/// existed because the previous arrangement answered "which scope?" with a file.
/// The projection therefore moved to the reading side, and this is it.</para>
///
/// <para>It is a faithful restatement of <c>projectScope</c> in
/// <c>.github/tools/knowledge-meta/graph.mjs</c>, which is what produced the
/// scoped <c>graph.json</c> files the atlas and the technology panel read today.
/// Three of its rules are load-bearing and none is obvious:</para>
///
/// <list type="bullet">
/// <item>Membership is decided by <b>path</b>, not by
/// <see cref="KnowledgeNodeRow.Folder"/>. They agree for every chapter in a
/// knowledge folder and disagree for a node outside them, which has no folder at
/// all and would silently vanish from a folder-kind filter it was supposed to
/// appear in as a boundary.</item>
/// <item>Only an <b>outbound</b> reference pulls a boundary node in. An unrelated
/// document merely pointing at this scope must not drag its whole neighbourhood
/// into the scoped graph.</item>
/// <item>An edge survives only when both ends are present <b>and its source is
/// in scope</b>. Without the second half, a boundary node's own references would
/// draw edges between two things neither of which this scope describes.</item>
/// </list>
///
/// <para>Two implementations of one rule is the drift hazard ADR 0004 names, so
/// this one is pinned by tests against the generator's behaviour rather than
/// trusted to stay in step on its own.</para>
/// </summary>
public sealed record KnowledgeScopeProjection(
    string Scope,
    IReadOnlyList<KnowledgeNodeRow> Nodes,
    IReadOnlyList<KnowledgeEdgeRow> Edges)
{
    /// <summary>
    /// Projects the whole graph onto one scope. The repository scope
    /// (<see cref="KnowledgeDatabaseSchema.RepositoryScope"/>) keeps everything,
    /// which is what makes the whole-repository atlas a case of this rather than an
    /// exception to it.
    /// </summary>
    public static KnowledgeScopeProjection Project(
        string scope,
        IReadOnlyList<KnowledgeNodeRow> nodes,
        IReadOnlyList<KnowledgeEdgeRow> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        if (string.IsNullOrWhiteSpace(scope) || scope == KnowledgeDatabaseSchema.RepositoryScope)
        {
            return new KnowledgeScopeProjection(KnowledgeDatabaseSchema.RepositoryScope, nodes, edges);
        }

        var prefix = scope + "/";
        var byId = new Dictionary<string, KnowledgeNodeRow>(StringComparer.Ordinal);
        var kept = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            byId[node.Id] = node;

            if (node.Path is { } path
                && (string.Equals(path, scope, StringComparison.Ordinal) || path.StartsWith(prefix, StringComparison.Ordinal)))
            {
                kept.Add(node.Id);
            }
        }

        var boundary = new Dictionary<string, KnowledgeNodeRow>(StringComparer.Ordinal);

        foreach (var edge in edges)
        {
            if (!kept.Contains(edge.Source) || kept.Contains(edge.Target)) continue;
            if (!byId.TryGetValue(edge.Target, out var target)) continue;

            boundary[target.Id] = target with { OutOfScope = true };
        }

        var projected = new List<KnowledgeNodeRow>(kept.Count + boundary.Count);
        foreach (var node in nodes)
        {
            if (kept.Contains(node.Id)) projected.Add(node);
        }

        projected.AddRange(boundary.Values);

        var available = projected.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var projectedEdges = edges
            .Where(edge => kept.Contains(edge.Source) && available.Contains(edge.Target))
            .ToList();

        return new KnowledgeScopeProjection(scope, projected, projectedEdges);
    }
}
