using Backlog.UI.Components.Knowledge;

namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// Where a knowledge reference points, said in the terms the pane selects with.
/// <para>
/// A reference is authored as a repository path — <c>.domain/backlog/domain.md</c>
/// — and a selection is remembered as a section plus a path beneath that section's
/// folder. Those are the same fact spelled two ways, and the panel that renders the
/// reference is not the thing holding the selection, so the translation has to
/// travel between them. It is a record rather than a pair of strings because the
/// section is what makes the path mean anything: the same <c>domain.md</c> exists
/// under more than one folder, and a path without its section is a file name in
/// search of an owner.
/// </para>
/// <para>
/// Producing one is also the test of whether the reference is worth offering at
/// all. <see cref="From(string?)"/> hands back nothing for a path whose folder is
/// not a section this product reads, which is what lets a panel leave such a
/// reference as text instead of drawing a control that goes nowhere.
/// </para>
/// </summary>
/// <param name="AreaKey">The section holding the target, as the menu and the area
/// catalog name it — <c>domain</c>, <c>arc42</c>, <c>tech</c>, <c>design</c>,
/// <c>backlog</c>.</param>
/// <param name="Path">The reference's own path: repository-relative, with forward
/// slashes, and still carrying the area folder at the front.</param>
/// <param name="Anchor">The heading slug the reference named, or
/// <see langword="null"/> when it addresses the file as a whole. Carried because it
/// is part of what the author wrote; nothing scrolls to it yet.</param>
public sealed record KnowledgeChapterLink(string AreaKey, string Path, string? Anchor)
{
    /// <summary>The same file as the knowledge menu spells it: beneath the area's
    /// own folder and without the area prefix, which is what a menu node carries
    /// and what a selection is remembered by.</summary>
    public string RelativePath => Path[(Path.IndexOf('/') + 1)..];

    /// <summary>
    /// Whether this names a view of the C4 model beside the chapters rather than a
    /// chapter.
    /// <para>
    /// Worth telling apart at the point of use, because the two are followed
    /// differently: a chapter is selected through the knowledge menu, and a C4 view
    /// is not in the menu at all — it is one view of one workspace, selected inside
    /// the panel that draws the architecture chapters. A caller that cannot follow
    /// the second should render it as text rather than as a control that goes
    /// nowhere.
    /// </para>
    /// </summary>
    public bool IsC4View => Path.EndsWith(".dsl", StringComparison.OrdinalIgnoreCase);

    /// <summary>The path and anchor back together, the way they were authored. What
    /// a C4 view is looked up by: the workspace and the view key are both needed, and
    /// splitting them was only ever for the chapter case.</summary>
    public string Reference => Anchor is null ? Path : $"{Path}#{Anchor}";

    /// <summary>Reads a link out of a reference as it was authored — a metadata
    /// entry, or a code span in prose — and refuses everything that does not name
    /// a chapter in a section.</summary>
    public static KnowledgeChapterLink? From(string? link) =>
        KnowledgeReference.Parse(link) is { } reference ? From(reference) : null;

    /// <summary>
    /// The same reading of a reference something has already parsed.
    /// <para>
    /// Two things have to hold. The folder at the front must name a section, or
    /// there is nowhere to send the reader; and the target must be something a
    /// section can show — a markdown chapter, or a view of the C4 model kept beside
    /// the chapters in <c>_c4/</c>. Anything else resolves to nothing rather than to
    /// a guess.
    /// </para>
    /// <para>
    /// The C4 case is narrow on purpose: a <c>.dsl</c> anywhere else in a knowledge
    /// folder is not a view of anything this app draws, and a reference naming one
    /// should stay text.
    /// </para>
    /// </summary>
    public static KnowledgeChapterLink? From(KnowledgeReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var path = reference.Path.Trim().Replace('\\', '/');
        if (path.StartsWith("./", StringComparison.Ordinal)) path = path[2..];
        path = path.TrimStart('/');

        if (!IsChapterFile(path) && !IsC4Workspace(path)) return null;
        if (KnowledgeAreaCatalog.AreaKeyForPath(path) is not { } areaKey) return null;

        return new KnowledgeChapterLink(areaKey, path, reference.Slug);
    }

    private static bool IsChapterFile(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    /// <summary>A workspace of the C4 model: a <c>.dsl</c> directly inside a
    /// <c>_c4/</c> folder, addressed with the view key as its anchor. A workspace
    /// with no anchor names a file rather than a picture, and there is no whole-file
    /// view to show, so it does not resolve.</summary>
    private static bool IsC4Workspace(string path) =>
        path.EndsWith(".dsl", StringComparison.OrdinalIgnoreCase)
        && path.Contains($"/{C4KnowledgeStore.WorkspaceDirectory}/", StringComparison.OrdinalIgnoreCase);
}
