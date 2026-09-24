namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// The one name a Devbook chapter has, whichever device is speaking.
/// <para>
/// A remark is addressed by repository alias and chapter path, and the path has
/// to mean the same chapter on every device the person owns or the remark they
/// left on the laptop is invisible on the tower. That is harder than it sounds,
/// because the areas spell their document paths differently and arc42 spells
/// its own differently again depending on where the folder is pointed: the
/// conventional <c>.arc42/adr/0001.md</c>, <c>docs/arch/adr/0001.md</c> for a
/// folder relocated inside the clone, and <c>arch/adr/0001.md</c> — the last
/// segment only — for one configured off the clone entirely. Three names for
/// one chapter, chosen by a machine-local setting.
/// </para>
/// <para>
/// So the canonical name is the area's <em>conventional</em> folder
/// (<c>.arc42</c>, <c>.domain</c>, …) followed by the chapter's path within
/// whatever folder the area is actually configured to. The conventional folder
/// rather than the configured one for the reason the defect gives: a configured
/// folder moves per machine, and a key that moves per machine is the bug. In the
/// root layout the two are the same string, so nothing changes for the
/// repositories that never moved a folder — which is the point. The devbook
/// layout (<c>.devbook/arc42</c>) is one more configured spelling and keys under
/// the same legacy root folder.
/// </para>
/// <para>
/// <c>instructions</c> is the exception, and by construction rather than by a
/// special case somebody has to remember: its root <em>is</em> the repository,
/// its <see cref="DevbookFolderSetting.DefaultRelativePath"/> is empty, and its
/// paths already carry their own real folder (<c>.github/…</c>,
/// <c>.agents/…</c>). An empty default contributes no prefix — one would match
/// every path there is — so an instructions path matches no area and comes back
/// unchanged, which is exactly its canonical form.
/// </para>
/// <para>
/// Everything here is pure and total: no disk, no settings lookup, no throw. A
/// caller with no folder configuration to offer still gets the conventional
/// reading, which is correct for every repository that has not moved a folder.
/// </para>
/// <para>
/// <see cref="Canonical"/> is idempotent, and that is a property of its shape
/// rather than an intention. A path that already begins with <em>any</em> area's
/// conventional folder is declared canonical and returned untouched, before a
/// configured folder is even looked at. Every answer this can give therefore
/// either begins with a conventional folder — in which case a second pass
/// returns on that first test — or begins with none and matched no configured
/// folder either, in which case a second pass takes the same two decisions again
/// and changes nothing. The idempotence matters because it is what lets the
/// stores canonicalize on every load and every write with nothing recording
/// whether they already have: there is no migration marker because there is
/// nothing to remember. An earlier draft matched configured folders first and was
/// <em>not</em> idempotent — a repository with <c>.arc42</c> at <c>docs/arch</c>
/// and <c>.tech</c> at <c>.arc42/adr</c> sent a chapter through arc42 on the
/// first pass and tech on the second — and since two of the call sites applied
/// it twice, that lost remarks rather than merely looking untidy.
/// </para>
/// <para>
/// The price of the conventional-first rule, stated plainly because it is a real
/// consequence rather than an edge nobody reaches: point area B at area A's
/// conventional folder — <c>.arc42</c> at <c>.domain</c> — and chapters under it
/// key as A's, not B's. That configuration is genuinely ambiguous. One folder is
/// being read by two areas and nothing in a path says which of them meant it, so
/// no rule gets it right; what a rule can do is be stable, and conventional-first
/// is. The alternative — letting the configured folder win — is the draft above,
/// which was unstable and destructive in exactly this shape.
/// </para>
/// </summary>
public static class DevbookChapterKey
{
    /// <summary>The menu presents <c>.agents</c> as <c>.agent</c> so the three
    /// instruction roots read alike, so a selection can name a folder the
    /// repository spells differently. One file, one key: the short spelling
    /// folds into the long one.</summary>
    private const string ShortAgentsFolder = ".agent/";

    private const string AgentsFolder = ".agents/";

    /// <summary>
    /// The conventional folder of each area that has one, keyed the way the menu
    /// and the area catalog name it.
    /// <para>
    /// Read from the published settings rather than written out again — and so
    /// <c>instructions</c>, which has no folder of its own because its root is the
    /// repository itself, is absent by construction rather than by a
    /// <c>_ =&gt; null</c> arm somebody has to remember.
    /// </para>
    /// <para>
    /// The legacy root folder (<c>.arc42</c>) rather than the devbook default
    /// (<c>.devbook/arc42</c>): these prefixes are what stored and synced remarks
    /// are keyed by, and the default moving under <c>.devbook/</c> is exactly the
    /// kind of move a key must not follow. A <c>.devbook/arc42/…</c> path is a
    /// configured spelling like any other and canonicalizes to <c>.arc42/…</c>.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> DefaultFolderPaths =
        DevbookFolderSetting.Defaults()
            .Where(folder => !string.IsNullOrWhiteSpace(folder.LegacyRelativePath))
            .ToDictionary(folder => NormalizeAreaKey(folder.Key), folder => folder.LegacyRelativePath, StringComparer.OrdinalIgnoreCase);

    /// <summary>The same folders as values, for the "is this already canonical?"
    /// test that has to ask about every area at once rather than about one.</summary>
    private static readonly string[] ConventionalFolders = [.. DefaultFolderPaths.Values];

    /// <summary>One spelling for a path: forward slashes, no anchor, no leading
    /// <c>./</c> or <c>/</c>, no surrounding whitespace. The anchor is dropped
    /// rather than honoured because a chapter is a file — a heading inside it is
    /// the same file, and the domain panel names sections as
    /// <c>path#anchor</c>.</summary>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        var forward = path.Replace('\\', '/').Trim();

        var anchor = forward.IndexOf('#', StringComparison.Ordinal);
        if (anchor >= 0) forward = forward[..anchor].Trim();

        while (forward.StartsWith("./", StringComparison.Ordinal)) forward = forward[2..];

        return forward.TrimStart('/');
    }

    /// <summary>Areas are named without the dot everywhere the menu and the area
    /// catalog speak, but a caller holding a configured folder key (<c>.arc42</c>)
    /// is naming the same area and should not have to translate first.</summary>
    public static string NormalizeAreaKey(string? areaKey) =>
        string.IsNullOrWhiteSpace(areaKey) ? string.Empty : areaKey.Trim().TrimStart('.').ToLowerInvariant();

    /// <summary>
    /// The one name this chapter has on every device.
    /// <para>
    /// Three steps, and the order of the first two is the whole of the
    /// idempotence argument the class comment makes. The path is spelled one way
    /// (separators, anchor, leading noise, and the menu's short <c>.agent</c>
    /// folding into the real <c>.agents</c> — that last one up here rather than at
    /// the end, so a fold can never uncover a configured folder a later pass would
    /// then match). Then, if it already begins with any area's conventional
    /// folder, it is already the name every device knows and comes straight back.
    /// Only then is it matched against the configured folders, most specific
    /// first, and the area that claims it has its conventional folder put on in
    /// place of whatever matched.
    /// </para>
    /// <para>
    /// A path no area claims — an instructions chapter, or one from a repository
    /// configured in some way this does not recognise — comes back in its spelled
    /// form, which for instructions is already canonical and for anything else is
    /// at least stable.
    /// </para>
    /// </summary>
    /// <param name="chapterPath">The path as whoever is holding it spells it: a
    /// document path from an area store, a selection from the menu, or a key read
    /// back out of a stored remark.</param>
    /// <param name="folders">The folders configured for the repository the
    /// chapter belongs to. Null or empty degrades to the conventional folders,
    /// which is the right answer for every repository that has not moved one.</param>
    public static string Canonical(string? chapterPath, IReadOnlyList<DevbookFolderSetting>? folders = null)
    {
        var normalized = FoldShortAgentsFolder(Normalize(chapterPath));
        if (normalized.Length == 0) return string.Empty;

        if (IsUnderAConventionalFolder(normalized)) return normalized;

        foreach (var (prefix, areaKey) in MatchOrder(folders))
        {
            if (!StartsWithSegment(normalized, prefix)) continue;
            if (!DefaultFolderPaths.TryGetValue(areaKey, out var conventional)) continue;

            var within = normalized[(prefix.Length + 1)..];

            // A prefix that claimed the whole path names the folder itself rather
            // than a chapter in it. Nothing to key, so nothing to rewrite.
            return within.Length == 0 ? normalized : conventional + "/" + within;
        }

        return normalized;
    }

    /// <summary>
    /// Whether this path is already spelled against an area's conventional
    /// folder, and so is already the name every device knows the chapter by.
    /// <para>
    /// Tested against every area's conventional folder rather than the one whose
    /// configured folder would claim the path, which is the point: it is what
    /// makes the answer independent of this machine's settings, and therefore
    /// what makes a second pass a no-op whatever those settings are.
    /// </para>
    /// </summary>
    private static bool IsUnderAConventionalFolder(string path) =>
        ConventionalFolders.Any(folder => StartsWithSegment(path, folder));

    /// <summary>The menu presents <c>.agents</c> as <c>.agent</c> so the three
    /// instruction roots read alike, so a selection can name a folder the
    /// repository spells differently. One file, one key.</summary>
    private static string FoldShortAgentsFolder(string path) =>
        path.StartsWith(ShortAgentsFolder, StringComparison.OrdinalIgnoreCase)
            ? AgentsFolder + path[ShortAgentsFolder.Length..]
            : path;

    /// <summary>
    /// The folder names a selection may carry for one area, most specific first,
    /// or nothing at all for an area whose root is the repository itself.
    /// <para>
    /// Three spellings of one folder reach here, which is why this is a list
    /// rather than a name. The document list of an area pointed at
    /// <c>docs/arch</c> names the whole configured path; <c>Arc42DevbookReader</c>
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
    public static IReadOnlyList<string> FolderPrefixes(string? areaKey, string? folderPath)
    {
        var area = NormalizeAreaKey(areaKey);
        var prefixes = new List<string>();

        Add(folderPath);
        Add(DefaultFolderPaths.GetValueOrDefault(area));
        if (DefaultFolderPaths.ContainsKey(area)) Add($"{DevbookFolderSetting.DevbookRoot}/{area}");

        return prefixes;

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            var normalized = Normalize(path).TrimEnd('/');

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
            // spelling because the resolver's Candidates reads the two in
            // opposite orders.
            if (prefix.StartsWith('.')) AddOne(prefix[1..]);
        }

        void AddOne(string prefix)
        {
            if (prefix.Length > 0 && !prefixes.Contains(prefix, StringComparer.OrdinalIgnoreCase)) prefixes.Add(prefix);
        }
    }

    /// <summary>
    /// Every area's folder spellings in one list, longest first, so that a path
    /// is claimed by the most specific folder that can claim it.
    /// <para>
    /// Length rather than the per-area order alone, because the areas compete
    /// here in a way they never do inside one area's own list: a repository that
    /// points <c>.arc42</c> at <c>.domain/arch</c> has a path that both
    /// <c>.domain/arch</c> and <c>.domain</c> could claim, and only the longer one
    /// is right. Within a length the per-area order still decides, which is what
    /// keeps the dotted spelling ahead of the undotted one.
    /// </para>
    /// <para>
    /// Two areas pointed at the <em>same</em> folder offer the same prefix at the
    /// same rank, and the winner is then settled by area key, alphabetically. It
    /// is an arbitrary rule for an arbitrary configuration — one folder read by
    /// two areas says nothing about which of them a path meant — but it is
    /// written down here rather than left to fall out of
    /// <see cref="DevbookFolderSetting.Defaults"/>' declaration order through the
    /// stability of a sort. A reader should be able to predict the key without
    /// knowing whether LINQ's ordering happens to be stable.
    /// </para>
    /// <para>
    /// <see cref="DevbookFolderSetting.Normalize"/> fills the gaps, so a caller
    /// that hands over nothing, a partial list, or a list still carrying the
    /// conventional path written out as an override all get the same answer.
    /// A folder that is switched off still contributes its prefixes: a remark
    /// already filed against it is exactly the thing that still needs a name.
    /// </para>
    /// </summary>
    private static IEnumerable<(string Prefix, string AreaKey)> MatchOrder(IReadOnlyList<DevbookFolderSetting>? folders) =>
        DevbookFolderSetting.Normalize(folders)
            .SelectMany(folder => FolderPrefixes(folder.Key, folder.EffectivePath)
                .Select((prefix, rank) => (Prefix: prefix, AreaKey: NormalizeAreaKey(folder.Key), Rank: rank)))
            .OrderByDescending(candidate => candidate.Prefix.Length)
            .ThenBy(candidate => candidate.Rank)
            .ThenBy(candidate => candidate.AreaKey, StringComparer.Ordinal)
            .Select(candidate => (candidate.Prefix, candidate.AreaKey));

    /// <summary>Whether a path lies under a folder, rather than merely starting
    /// with the same letters: <c>.arc42-old/x.md</c> is not in <c>.arc42</c>.
    /// Public because <c>DevbookChapterResolver</c> asks the same question of the
    /// same prefix list, and two copies of this test drifting apart is how a
    /// remark and the chapter it is about would come to disagree again.</summary>
    public static bool StartsWithSegment(string path, string segment) =>
        path.Length > segment.Length
        && path[segment.Length] == '/'
        && path.StartsWith(segment, StringComparison.OrdinalIgnoreCase);
}
