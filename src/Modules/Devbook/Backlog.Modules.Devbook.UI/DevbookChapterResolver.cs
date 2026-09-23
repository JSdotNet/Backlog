using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.Devbook;

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
/// The shape is <see cref="DevbookMarkdownStatusWriter"/>'s: a folder root, an
/// item path that may or may not carry its area prefix, and a containment check
/// before the path is believed. What differs is the leniency — that writer serves
/// stores whose document paths are always prefixed, while a selection here can
/// also come straight from the menu, which names a chapter relative to the area
/// folder. The panels' own <c>PathMatches</c> helpers already treat the two
/// spellings as one selection; refusing either here would make an area editable
/// from the document list and not from the menu beside it.
/// </para>
/// </summary>
public static class DevbookChapterResolver
{
    /// <summary>Resolves against a folder the devbook-folder port has already
    /// located. An unavailable folder resolves to null rather than to a path that
    /// happens to combine: the message explaining why it is unavailable belongs
    /// to the panel, and an editing surface is not where it gets shown.
    /// <para>
    /// The location carries the setting the folder was resolved from, so the
    /// configured path travels with the root rather than being guessed back from
    /// the area. A repository that points <c>.arc42</c> at <c>docs/arch</c> spells
    /// its selections that way, and forwarding only the root left that spelling
    /// matching nothing — the chapter resolved to null and the pane offered no way
    /// in without saying why. A location with no setting behind it falls back to
    /// the area's conventional folder.
    /// </para></summary>
    public static DevbookChapterRef? TryResolve(string areaKey, DevbookFolderLocation? location, string? selection) =>
        location is { Available: true }
            ? TryResolve(areaKey, location.FullPath, selection, location.Folder?.EffectivePath, location.CanEdit)
            : null;

    /// <summary>Resolves against a root a store already holds — the technology
    /// view's location, the design model's folder, the arc42 folder. Same rules;
    /// the five areas differ only in where their root came from.
    /// <para>
    /// <paramref name="folderPath"/> is the configured path of that root, for a
    /// caller that holds it. Omitting it reads the area's conventional folder,
    /// which is what the stores that stamp a dotted prefix onto their document
    /// paths are naming whatever the folder was pointed at.
    /// </para></summary>
    /// <param name="canEdit">Whether the resolved chapter may be written to. A
    /// chapter is still resolved when it may not be — the reader wants to read
    /// it — and the surface it reaches renders read-only instead of refusing to
    /// open. Resolving to null for an unwritable chapter would hide the content
    /// as well as the edit.</param>
    public static DevbookChapterRef? TryResolve(
        string areaKey,
        string? rootPath,
        string? selection,
        string? folderPath = null,
        bool canEdit = true)
    {
        if (string.IsNullOrWhiteSpace(areaKey) || string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(selection))
        {
            return null;
        }

        var area = NormalizeAreaKey(areaKey);

        foreach (var candidate in Candidates(area, folderPath, selection))
        {
            if (DevbookChapterPaths.ResolveWithin(rootPath, candidate) is not { } fullPath) continue;
            if (!File.Exists(fullPath)) continue;

            return new DevbookChapterRef(area, rootPath, candidate, canEdit);
        }

        return null;
    }

    /// <summary>
    /// The relative paths worth trying, best first.
    /// <para>
    /// Order matters wherever a selection could be read two ways. One that
    /// carries the dotted area folder (<c>.arc42/08-x.md</c>) means the chapter
    /// under that folder, so the stripped form goes first. One that starts with
    /// an undotted name (<c>arc42/08-x.md</c>, which is what the panels'
    /// leading-dot trimming leaves behind, or <c>docs/arch/08-x.md</c> under a
    /// folder somebody moved) is ambiguous with a real subfolder of that name, so
    /// the literal reading goes first and the strip is the fallback. Existence
    /// decides between them, which is why it is the last check rather than the
    /// first — and it is what keeps a configured folder from resolving against
    /// its own root twice.
    /// </para>
    /// <para>
    /// Only the first prefix a selection carries is read, because
    /// <see cref="FolderPrefixes"/> offers them most specific first: a selection
    /// under <c>docs/arch</c> is that folder's, not a chapter of some
    /// <c>arch</c> beneath it.
    /// </para>
    /// </summary>
    private static IEnumerable<string> Candidates(string areaKey, string? folderPath, string selection)
    {
        var normalized = DevbookChapterPaths.Normalize(selection);
        if (normalized.Length == 0) yield break;

        var prefixes = FolderPrefixes(areaKey, folderPath);
        if (prefixes.Count == 0)
        {
            // No folder to strip means Instructions: its root is the repository
            // root and its relative paths keep their leading dot (.github/,
            // .claude/). Its configured path is empty and cannot be overridden,
            // so it reaches here whatever the settings say.
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

        foreach (var prefix in prefixes)
        {
            if (!StartsWithSegment(normalized, prefix)) continue;

            var stripped = normalized[(prefix.Length + 1)..];
            if (prefix[0] == '.')
            {
                yield return stripped;
                yield return normalized;
            }
            else
            {
                yield return normalized;
                yield return stripped;
            }

            yield break;
        }

        yield return normalized;
    }

    /// <summary>The folder names a selection may carry for this area, most
    /// specific first, or nothing at all for an area whose root is the repository
    /// itself. Shared with the chapter key rather than repeated here: the two ask
    /// the same question — which folder is this path spelled against? — and a
    /// second copy of the answer is how a remark and the chapter it is about came
    /// to disagree in the first place.</summary>
    private static IReadOnlyList<string> FolderPrefixes(string areaKey, string? folderPath) =>
        DevbookChapterKey.FolderPrefixes(areaKey, folderPath);

    /// <summary>Whether a selection lies under a folder rather than merely
    /// starting with the same letters. Shared with the chapter key for the reason
    /// the prefix list is: the two are reading one prefix list and must read it
    /// the same way.</summary>
    private static bool StartsWithSegment(string path, string segment) =>
        DevbookChapterKey.StartsWithSegment(path, segment);

    /// <summary>Areas are named without the dot everywhere the menu and the area
    /// catalog speak, but a caller holding a configured folder key (<c>.arc42</c>)
    /// is naming the same area and should not have to translate first.</summary>
    private static string NormalizeAreaKey(string areaKey) => DevbookChapterKey.NormalizeAreaKey(areaKey);
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
internal static class DevbookChapterPaths
{
    /// <summary>One spelling for a selection: forward slashes, no anchor, no
    /// leading <c>./</c> or <c>/</c>. Shared with <see cref="DevbookChapterKey"/>,
    /// which needs the same spelling before it can name an area — a selection and
    /// the remark left on it have to normalize alike, or they are two different
    /// chapters.</summary>
    internal static string Normalize(string path) => DevbookChapterKey.Normalize(path);

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
