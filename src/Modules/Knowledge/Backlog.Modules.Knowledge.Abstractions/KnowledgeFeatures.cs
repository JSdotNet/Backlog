namespace Backlog.Modules.Knowledge.Abstractions;

/// <summary>
/// The feature keys Second Brain owns.
/// <para>
/// <see cref="KnowledgeSections"/> is read by this context's own scope service
/// and <see cref="RepositoryKnowledge"/> by the Shell that decides whether to
/// offer the pane at all. Both name something about knowledge rather than about
/// the screen asking, so both sit here and the Shell reads them across — the
/// direction that works, because nothing below the Shell may read the Shell.
/// </para>
/// </summary>
public static class KnowledgeFeatures
{
    /// <summary>Show the side pane for repository knowledge.</summary>
    public const string RepositoryKnowledge = "repository-knowledge";

    /// <summary>Show the design, architecture, domain, technology and
    /// instruction sections in the knowledge pane and header.</summary>
    public const string KnowledgeSections = "knowledge-sections";
}
