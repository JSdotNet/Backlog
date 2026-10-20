namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// A bounded context listed by unit, as the New domain view reads it (local ADR
/// 0036, "The menu follows the reading order" and "A split file is a child of its
/// kind").
/// <para>
/// The kinds come first, in the devbook reading order, each one row: Domain,
/// Actors, Features, Skills, Requirements, Model, Flow. A split file
/// <c>&lt;kind&gt;.&lt;name&gt;.md</c> is a child of its kind's row, and each further
/// dot nests it one level deeper — <c>features.producten.budgetbeheer.md</c> under
/// <c>features.producten.md</c>. A kind, or a split, with no file of its own is a
/// row that only opens its children. The files that are a tab or a button on
/// another page draw no row: <c>domain*.invariants.md</c> is Always true and
/// <c>dependencies.md</c> is Neighbours (a <c>*.demo.html</c> never reaches the
/// menu, which lists Markdown only). Every other page, and every folder, follows
/// the kinds in the order the rail has always given them.
/// </para>
/// <para>
/// Derived from the file names alone, as the rest of the rail is. A split row is
/// labelled by its heading as the devbook database outline recorded it, and by
/// its last name segment, sentence-cased, where there is no database or the
/// chapter moved since it was built — the menu never opens a chapter to find one.
/// </para>
/// </summary>
public sealed partial class DevbookMenu
{
    /// <summary>The kinds a context is read by, in reading order, with the label
    /// each one's row carries. A context has Features or Skills, so the two sit
    /// side by side.</summary>
    private static readonly (string Prefix, string Label)[] DomainUnitKinds =
    [
        ("domain", "Domain"),
        ("actors", "Actors"),
        ("features", "Features"),
        ("skills", "Skills"),
        ("requirements", "Requirements"),
        ("model", "Model"),
        ("flow", "Flow")
    ];

    /// <summary>Whether <paramref name="directory"/> is a bounded context: a
    /// directory directly under the Domain folder.</summary>
    private static bool IsBoundedContextDirectory(string root, string directory) =>
        string.Equals(
            Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(directory)),
            Path.TrimEndingDirectorySeparator(root),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>A file the view shows on another file's page rather than as a
    /// row: an invariants subpage (Always true) or the context's dependencies
    /// (Neighbours).</summary>
    private static bool ShowsOnAnotherPage(string fileName) =>
        DomainDevbookStore.IsInvariantsSubpage(fileName)
        || string.Equals(fileName, "dependencies.md", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<DevbookMenuNode> ContextUnits(
        string root,
        string directory,
        string areaKey,
        DevbookMenuOutline outline,
        List<(string DiskPath, DevbookMenuNode Node)> directories,
        List<(string DiskPath, DevbookMenuNode Node)> files)
    {
        var relativeDirectory = RelativeDirectory(root, directory);
        var listed = files.Where(file => !ShowsOnAnotherPage(Path.GetFileName(file.DiskPath))).ToList();

        // One row per file stem: `features.producten` for features.producten.md.
        var byStem = new Dictionary<string, (string DiskPath, DevbookMenuNode Node)>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in listed) byStem.TryAdd(Path.GetFileNameWithoutExtension(file.DiskPath), file);

        var units = new List<DevbookMenuNode>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (prefix, label) in DomainUnitKinds)
        {
            var stems = byStem.Keys
                .Where(stem => string.Equals(stem, prefix, StringComparison.OrdinalIgnoreCase)
                    || stem.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (stems.Count == 0) continue;

            claimed.UnionWith(stems);
            units.Add(UnitRow(prefix, label, stems));
        }

        var others = directories
            .Concat(listed.Where(file => !claimed.Contains(Path.GetFileNameWithoutExtension(file.DiskPath))))
            .ToList();
        var names = others.Select(entry => Path.GetFileName(entry.DiskPath)).ToList();

        return
        [
            .. units,
            .. Label(
                Order(
                    others,
                    outline.Order(relativeDirectory, names),
                    outline.RootDocument(relativeDirectory, names),
                    areaKey,
                    root,
                    directory),
                outline,
                relativeDirectory)
        ];

        // The row for one stem, its children the stems one dot longer that start
        // with it — made up where a deeper split has no parent file of its own.
        DevbookMenuNode UnitRow(string stem, string? kindLabel, IReadOnlyList<string> descendants)
        {
            var children = descendants
                .Where(other => other.Length > stem.Length + 1
                    && other.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase))
                .Select(other => stem + "." + other[(stem.Length + 1)..].Split('.')[0])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(child => UnitRow(child, null, descendants))
                .ToList();

            if (byStem.TryGetValue(stem, out var file))
            {
                return file.Node with
                {
                    Label = kindLabel ?? Heading(Path.GetFileName(file.DiskPath), stem),
                    Children = children
                };
            }

            // No file behind the row: it carries a path no panel opens, so a click
            // only opens and closes its children.
            // Keyed apart from a real folder of the same name, which the pane
            // would otherwise open and close together with this row.
            var path = RelativePath(root, directory) + "/" + stem;
            return new DevbookMenuNode(
                path.ToLowerInvariant() + "#unit",
                kindLabel ?? SentenceCase(LastSegment(stem)),
                path,
                DevbookMenuNodeKind.Folder,
                areaKey,
                children,
                true);
        }

        string Heading(string fileName, string stem) =>
            outline.TryHeading(relativeDirectory, fileName, out var heading)
                ? heading
                : SentenceCase(LastSegment(stem));
    }

    /// <summary>
    /// The rows a Domain path is shown on when the menu lists by unit and the path
    /// has no row of its own, nearest first: an invariants subpage on its domain
    /// page (<c>tasks/domain.order.invariants.md</c> on <c>tasks/domain.order.md</c>),
    /// <c>dependencies.md</c> on its context's row. Empty for any other path. A
    /// reference into one of those files, or a selection made under the flat list,
    /// lands on the page it shows on rather than nowhere.
    /// </summary>
    public static IReadOnlyList<string> DomainUnitHosts(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        var path = relativePath.Replace('\\', '/');
        var slash = path.LastIndexOf('/');
        if (slash <= 0) return [];

        var context = path[..slash];
        var name = path[(slash + 1)..];

        if (DomainDevbookStore.IsInvariantsSubpage(name))
        {
            return [context + "/" + name[..^".invariants.md".Length] + ".md"];
        }

        return string.Equals(name, "dependencies.md", StringComparison.OrdinalIgnoreCase)
            ? [context + "/context.md", context]
            : [];
    }

    private static string LastSegment(string stem) => stem[(stem.LastIndexOf('.') + 1)..];

    /// <summary>A name segment as a heading is usually written:
    /// <c>afspraken-beheren</c> is "Afspraken beheren".</summary>
    private static string SentenceCase(string segment)
    {
        var words = segment.Replace('_', ' ').Replace('-', ' ').Trim();
        return words.Length == 0 ? segment : char.ToUpperInvariant(words[0]) + words[1..];
    }
}
