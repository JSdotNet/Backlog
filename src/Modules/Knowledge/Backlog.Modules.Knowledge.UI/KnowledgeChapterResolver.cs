using Backlog.Modules.Knowledge.Abstractions;

namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// Turns "this area, this folder, that selection" into the one chapter file an
/// editor may write — or into nothing at all.
/// <para>
/// Nothing at all is the answer worth designing for. A selection this cannot
/// place makes the editing surface render with <c>CanEdit="false"</c>, so the
/// reader is simply not offered a way in; the alternative is an Edit button that
/// throws on the first keystroke. Every step that can fail therefore fails to
/// null rather than to an exception, and the panels keep their existing habit of
/// showing what they can read.
/// </para>
/// <para>
/// The shape is <see cref="KnowledgeMarkdownStatusWriter"/>'s: a folder root, an
/// item path that may or may not carry its area prefix, and a containment check
/// before the path is believed. What differs is the leniency — that writer serves
/// stores whose document paths are always prefixed, while a selection here can
/// also come straight from the menu, which names a chapter relative to the area
/// folder. The panels' own <c>PathMatches</c> helpers already treat the two
/// spellings as one selection; refusing either here would make an area editable
/// from the document list and not from the menu beside it.
/// </para>
/// </summary>
public static class KnowledgeChapterResolver
{
    /// <summary>Resolves against a folder the knowledge-folder port has already
    /// located. An unavailable folder resolves to null rather than to a path that
    /// happens to combine: the message explaining why it is unavailable belongs
    /// to the panel, and an editing surface is not where it gets shown.</summary>
    public static KnowledgeChapterRef? TryResolve(string areaKey, KnowledgeFolderLocation? location, string? selection) =>
        location is { Available: true } ? TryResolve(areaKey, location.FullPath, selection) : null;

    /// <summary>Resolves against a root a store already holds — the technology
    /// view's location, the design model's folder, the arc42 folder. Same rules;
    /// the five areas differ only in where their root came from.</summary>
    public static KnowledgeChapterRef? TryResolve(string areaKey, string? rootPath, string? selection)
    {
        if (string.IsNullOrWhiteSpace(areaKey) || string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(selection))
        {
            return null;
        }

        var area = NormalizeAreaKey(areaKey);

        foreach (var candidate in Candidates(area, selection))
        {
            if (KnowledgeChapterPaths.ResolveWithin(rootPath, candidate) is not { } fullPath) continue;
            if (!File.Exists(fullPath)) continue;

            return new KnowledgeChapterRef(area, rootPath, candidate);
        }

        return null;
    }

    /// <summary>
    /// The relative paths worth trying, best first.
    /// <para>
    /// Order matters wherever a selection could be read two ways. One that
    /// carries the dotted area folder (<c>.arc42/08-x.md</c>) means the chapter
    /// under that folder, so the stripped form goes first. One that starts with
    /// the undotted name (<c>arc42/08-x.md</c>, which is what the panels'
    /// leading-dot trimming leaves behind) is ambiguous with a real subfolder of
    /// that name, so the literal reading goes first and the strip is the
    /// fallback. Existence decides between them, which is why it is the last
    /// check rather than the first.
    /// </para>
    /// </summary>
    private static IEnumerable<string> Candidates(string areaKey, string selection)
    {
        var normalized = KnowledgeChapterPaths.Normalize(selection);
        if (normalized.Length == 0) yield break;

        var folder = AreaFolderName(areaKey);
        if (folder is null)
        {
            // Instructions has no area folder: its root is the repository root
            // and its relative paths keep their leading dot (.github/, .claude/).
            yield return normalized;

            // The menu presents .agents as ".agent" so the three instruction
            // roots read alike, so a selection can name a folder the repository
            // spells differently. Same fallback the instructions panel already
            // makes when it reads the selected file.
            if (normalized.StartsWith(".agent/", StringComparison.OrdinalIgnoreCase))
            {
                yield return ".agents/" + normalized[".agent/".Length..];
            }

            yield break;
        }

        if (StartsWithSegment(normalized, "." + folder))
        {
            yield return normalized[(folder.Length + 2)..];
            yield return normalized;
        }
        else if (StartsWithSegment(normalized, folder))
        {
            yield return normalized;
            yield return normalized[(folder.Length + 1)..];
        }
        else
        {
            yield return normalized;
        }
    }

    private static bool StartsWithSegment(string path, string segment) =>
        path.Length > segment.Length
        && path[segment.Length] == '/'
        && path.StartsWith(segment, StringComparison.OrdinalIgnoreCase);

    /// <summary>The area's folder name without its leading dot, or null for an
    /// area whose root is the repository itself. Mirrors the mapping
    /// <see cref="KnowledgeFolderOpenService"/> uses in the other direction, from
    /// an area key to a configured folder key.</summary>
    private static string? AreaFolderName(string areaKey) => areaKey switch
    {
        "backlog" => "backlog",
        "domain" => "domain",
        "arc42" => "arc42",
        "tech" => "tech",
        "design" => "design",
        _ => null
    };

    /// <summary>Areas are named without the dot everywhere the menu and the area
    /// catalog speak, but a caller holding a configured folder key (<c>.arc42</c>)
    /// is naming the same area and should not have to translate first.</summary>
    private static string NormalizeAreaKey(string areaKey) => areaKey.Trim().TrimStart('.').ToLowerInvariant();
}

/// <summary>
/// The path rules the resolver and the writer share: how a selection is spelled,
/// and what "inside the root" means.
/// <para>
/// Shared rather than repeated because the two make the same decision at two
/// different moments — the resolver so a chapter can be offered at all, the
/// writer so a ref built by anybody still cannot reach outside its root — and a
/// containment check that drifted between them would be a containment check in
/// name only.
/// </para>
/// </summary>
internal static class KnowledgeChapterPaths
{
    /// <summary>One spelling for a selection: forward slashes, no anchor, no
    /// leading <c>./</c> or <c>/</c>. The anchor is dropped rather than honoured
    /// because a chapter is a file — a heading inside it is the same file, and the
    /// domain panel names sections as <c>path#anchor</c>.</summary>
    internal static string Normalize(string path)
    {
        var forward = path.Replace('\\', '/').Trim();

        var anchor = forward.IndexOf('#', StringComparison.Ordinal);
        if (anchor >= 0) forward = forward[..anchor].Trim();

        while (forward.StartsWith("./", StringComparison.Ordinal)) forward = forward[2..];

        return forward.TrimStart('/');
    }

    /// <summary>The full path of a relative path under a root, or null when it
    /// does not stay there. A rooted relative path is the case that makes this
    /// worth having: <c>Path.Combine</c> hands back the absolute path and
    /// discards the root entirely, so the check has to be on the result rather
    /// than on the input.</summary>
    internal static string? ResolveWithin(string rootPath, string relativePath)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
            var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

            return fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // An unusable path is the same answer as one outside the root: there
            // is no chapter here to edit.
            return null;
        }
    }
}
