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
    public static KnowledgeChapterRef? TryResolve(string areaKey, KnowledgeFolderLocation? location, string? selection) =>
        location is { Available: true } ? TryResolve(areaKey, location.FullPath, selection, location.Folder?.EffectivePath) : null;

    /// <summary>Resolves against a root a store already holds — the technology
    /// view's location, the design model's folder, the arc42 folder. Same rules;
    /// the five areas differ only in where their root came from.
    /// <para>
    /// <paramref name="folderPath"/> is the configured path of that root, for a
    /// caller that holds it. Omitting it reads the area's conventional folder,
    /// which is what the stores that stamp a dotted prefix onto their document
    /// paths are naming whatever the folder was pointed at.
    /// </para></summary>
    public static KnowledgeChapterRef? TryResolve(string areaKey, string? rootPath, string? selection, string? folderPath = null)
    {
        if (string.IsNullOrWhiteSpace(areaKey) || string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(selection))
        {
            return null;
        }

        var area = NormalizeAreaKey(areaKey);

        foreach (var candidate in Candidates(area, folderPath, selection))
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
        var normalized = KnowledgeChapterPaths.Normalize(selection);
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

    /// <summary>
    /// The folder names a selection may carry, most specific first, or nothing at
    /// all for an area whose root is the repository itself.
    /// <para>
    /// Three spellings of one folder reach here, which is why this is a list
    /// rather than a name. The document list of an area pointed at
    /// <c>docs/arch</c> names the whole configured path; <c>Arc42KnowledgeReader</c>
    /// falls back to spelling its documents relative to the folder's <em>parent</em>
    /// when it is reading a folder configured off the clone, and so emits only the
    /// last segment; and a store that stamps a literal <c>.domain/</c> onto every
    /// path keeps naming the conventional folder wherever the folder actually
    /// sits. The conventional folder is therefore always offered alongside the
    /// configured one rather than instead of it.
    /// </para>
    /// <para>
    /// A rooted override contributes nothing but its last segment, which is the
    /// only part of it a selection can be spelled with.
    /// </para>
    /// </summary>
    private static List<string> FolderPrefixes(string areaKey, string? folderPath)
    {
        var prefixes = new List<string>();
        Add(folderPath);
        Add(DefaultFolderPaths.GetValueOrDefault(areaKey));

        return prefixes;

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            var normalized = KnowledgeChapterPaths.Normalize(path).TrimEnd('/');

            // A rooted override names somewhere off the clone entirely, so the
            // whole of it is not a prefix any selection carries; its last segment
            // still is, because that is the folder the reader walked.
            if (!Path.IsPathRooted(normalized)) AddPrefix(normalized);

            var lastSeparator = normalized.LastIndexOf('/');
            if (lastSeparator >= 0) AddPrefix(normalized[(lastSeparator + 1)..]);
        }

        void AddPrefix(string prefix)
        {
            AddOne(prefix);

            // The panels trim the leading dot when they present an area, so a
            // selection can name the same folder undotted. Kept after the dotted
            // spelling because Candidates reads the two in opposite orders.
            if (prefix.StartsWith('.')) AddOne(prefix[1..]);
        }

        void AddOne(string prefix)
        {
            if (prefix.Length > 0 && !prefixes.Contains(prefix, StringComparer.OrdinalIgnoreCase)) prefixes.Add(prefix);
        }
    }

    private static bool StartsWithSegment(string path, string segment) =>
        path.Length > segment.Length
        && path[segment.Length] == '/'
        && path.StartsWith(segment, StringComparison.OrdinalIgnoreCase);

    /// <summary>The conventional folder of each area that has one, keyed the way
    /// the menu and the area catalog name it. Read from the published settings
    /// rather than written out again, so an area whose default moves does not
    /// leave a second copy of it here — and so Instructions, whose default path is
    /// empty because its root is the repository itself, is absent by construction
    /// rather than by a <c>_ =&gt; null</c> arm somebody has to remember.</summary>
    private static readonly Dictionary<string, string> DefaultFolderPaths =
        KnowledgeFolderSetting.Defaults()
            .Where(folder => !string.IsNullOrWhiteSpace(folder.DefaultRelativePath))
            .ToDictionary(folder => NormalizeAreaKey(folder.Key), folder => folder.DefaultRelativePath, StringComparer.OrdinalIgnoreCase);

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
