using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel.Devbook;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Where a knowledge reference points, said in the terms the pane selects with.
/// <para>
/// A reference is authored as a repository path — <c>.devbook/domain/tasks/domain.md</c>
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
/// is part of what the author wrote. On a scenario page it names the part the pane
/// marks and scrolls to; on any other chapter nothing scrolls to it yet.</param>
public sealed record DevbookChapterLink(string AreaKey, string Path, string? Anchor)
{
    /// <summary>The same file as the Devbook menu spells it: beneath the area's
    /// own folder and without the area prefix, which is what a menu node carries
    /// and what a selection is remembered by. Both layouts' folder prefixes come
    /// off — <c>.domain/</c> and <c>.devbook/domain/</c>.</summary>
    public string RelativePath => DevbookMenu.IsBrainAreaKey(AreaKey) ? Path : DevbookLayout.WithinFolder(Path);

    /// <summary>
    /// Whether this names a view of the C4 model beside the chapters rather than a
    /// chapter.
    /// <para>
    /// Worth telling apart at the point of use, because the two are followed
    /// differently: a chapter is selected through the Devbook menu, and a C4 view
    /// is not in the menu at all — it is one view of one workspace, selected inside
    /// the panel that draws the architecture chapters. A caller that cannot follow
    /// the second should render it as text rather than as a control that goes
    /// nowhere.
    /// </para>
    /// </summary>
    public bool IsC4View => Path.EndsWith(".dsl", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this names a section's folder as a whole rather than a
    /// file in it — what <see cref="ForFolder"/> builds, its path ending in a
    /// separator. Following it opens the
    /// section's tab with whatever chapter that section has open.</summary>
    public bool IsFolder => Anchor is null && Path.EndsWith('/') && !DevbookMenu.IsBrainAreaKey(AreaKey);

    /// <summary>
    /// A section's folder, for a caller that names a folder rather than a chapter
    /// in it — the Devbook tab's adopted folders. Null when the name is not a
    /// section this product reads, so the caller draws it as text.
    /// </summary>
    public static DevbookChapterLink? ForFolder(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return null;

        var name = folderName.Trim().TrimStart('.').TrimEnd('/');
        if (name.Length == 0 || name.Contains('/', StringComparison.Ordinal)) return null;

        var path = $"{DevbookFolderSetting.DevbookRoot}/{name}/";
        return DevbookAreaCatalog.AreaKeyForPath(path) is { } areaKey ? new DevbookChapterLink(areaKey, path, null) : null;
    }

    /// <summary>The path and anchor back together, the way they were authored. What
    /// a C4 view is looked up by: the workspace and the view key are both needed, and
    /// splitting them was only ever for the chapter case.</summary>
    public string Reference => Anchor is null ? Path : $"{Path}#{Anchor}";

    /// <summary>Reads a link out of a reference as it was authored — a metadata
    /// entry, or a code span in prose — and refuses everything that does not name
    /// a chapter in a section.</summary>
    public static DevbookChapterLink? From(string? link) =>
        DevbookReference.Parse(link) is { } reference ? From(reference) : null;

    /// <summary>
    /// The same reading of a reference something has already parsed.
    /// <para>
    /// Two things have to hold. The folder at the front must name a section, or
    /// there is nowhere to send the reader; and the target must be something a
    /// section can show — a markdown chapter, or a view of the C4 model kept beside
    /// the chapters in <c>workspace.dsl</c>. Anything else resolves to nothing rather
    /// than to a guess.
    /// </para>
    /// <para>
    /// The C4 case is narrow on purpose: a <c>.dsl</c> anywhere else in a knowledge
    /// folder is not a view of anything this app draws, and a reference naming one
    /// should stay text.
    /// </para>
    /// </summary>
    public static DevbookChapterLink? From(DevbookReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var path = reference.Path.Trim().Replace('\\', '/');
        if (path.StartsWith("./", StringComparison.Ordinal)) path = path[2..];
        path = path.TrimStart('/');

        if (!IsChapterFile(path) && !IsC4Workspace(path)) return null;
        if (DevbookAreaCatalog.AreaKeyForPath(path) is not { } areaKey) return null;

        return new DevbookChapterLink(areaKey, path, reference.Slug);
    }

    /// <summary>
    /// A note of the Brain, addressed the way the Brain's database names it:
    /// relative to the Brain's root, its first segment the area folder
    /// (<c>projects/plan.md#next</c>). Its area key is the Brain's
    /// (<see cref="DevbookMenu.BrainAreaKey"/>), and its path is already the one
    /// the Brain's menu selects by, so <see cref="RelativePath"/> leaves it whole.
    /// A note at the Brain's root sits in no area and resolves to nothing.
    /// </summary>
    public static DevbookChapterLink? ForBrainNote(string? link)
    {
        if (DevbookReference.Parse(link) is not { } reference) return null;

        var path = reference.Path.Trim().Replace('\\', '/');
        if (path.StartsWith("./", StringComparison.Ordinal)) path = path[2..];
        path = path.TrimStart('/');

        var separator = path.IndexOf('/');
        if (separator <= 0 || !IsChapterFile(path)) return null;

        return new DevbookChapterLink(DevbookMenu.BrainAreaKey(path[..separator]), path, reference.Slug);
    }

    private static bool IsChapterFile(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    /// <summary>A file of the C4 model, addressed with the view key as its anchor: the
    /// architecture folder's <c>workspace.dsl</c>, a <c>.dsl</c> it includes from
    /// <c>c4/</c>, or a workspace in the <c>_c4/</c> folder that held them before
    /// devbook's contract 33.</summary>
    private static bool IsC4Workspace(string path)
    {
        if (!path.EndsWith(".dsl", StringComparison.OrdinalIgnoreCase)) return false;

        return path.EndsWith($"arc42/{C4DevbookStore.WorkspaceFile}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"arc42/{C4DevbookStore.IncludeDirectory}/", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"/{C4DevbookStore.ReferenceDirectory}/", StringComparison.OrdinalIgnoreCase);
    }
}
