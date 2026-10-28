using System.Text.RegularExpressions;

namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// The Brain's own convention: which areas it has (local ADR 0026 §2). Each area
/// is one top folder of the Brain and one tab of the pane, and the areas come in
/// two groups.
///
/// <list type="number">
/// <item><b>Declared areas first</b>, in the order the <c>CONVENTIONS.md</c> at
/// the Brain's root writes them under a heading named <c>Areas</c>, at any level —
/// or the PARA set (<see cref="Para"/>) when it declares none or there is no file.
/// A declared area stays an area while its folder is empty or missing, so a
/// capture always has a target.</item>
/// <item><b>Discovered areas after them</b>: every other top folder that is one
/// plain segment (<see cref="IsAreaFolder"/>) and holds a <c>.md</c> note at any
/// depth outside its dot sub-folders, ordered by folder name, ordinal and
/// case-insensitive, named after the folder (<see cref="NameFor"/>), with no
/// <c>Holds</c>.</item>
/// </list>
///
/// <para>A folder the same file lists under a heading named <c>Ignore</c> is never
/// an area, even when <c>Areas</c> declares it; nor is a dot-folder. Markdown at
/// the Brain's root belongs to no area. Both sections take a table whose columns
/// include <c>Folder</c> — <c>Areas</c> also reads <c>Area</c> or <c>Name</c>,
/// <c>Holds</c> and <c>Order</c>, the shape the template <b>Create conventions</b>
/// writes (<c>BrainConventionTemplate</c>) — or a list:</para>
///
/// <code>
/// ## Areas
///
/// | Area     | Folder   |
/// |----------|----------|
/// | Inbox    | _inbox   |
/// | Projects | projects |
///
/// - Inbox — `_inbox`
/// - `projects`
/// - people
///
/// ## Ignore
///
/// - `_templates`
/// </code>
///
/// <para>A list item names its folder in backticks, or is the folder when it has
/// no backticks; the text before the backticks, without its dash, is the area's
/// name. An area with no name is named after its folder. The order is the order
/// written, unless a table's <c>Order</c> column numbers the rows. A folder that
/// is not one plain segment — a path, <c>..</c>, a dot-folder — is skipped, and
/// so is a folder listed twice, whatever its case.</para>
///
/// <para>Finding the discovered areas lists folders and file names and never
/// opens a file, so a OneDrive placeholder counts by its name and is not
/// downloaded. <see cref="Hidden(string?)"/> answers the folders <b>Show hidden</b>
/// brings back as tabs — the ignored ones and the dot-folders — which stay out of
/// every area, search and the database.</para>
///
/// <para>This is the one reader of the file in the app: the database builder, the
/// Brain folder source and <b>Create conventions</b> all ask it.
/// <c>tools/devbook/brain.mjs</c> reads the same file and the same folders the
/// same way for the Node writer, and <c>DevbookBuilderParityTests</c> holds the
/// two together on a Brain fixture.</para>
/// </summary>
public static partial class BrainConventions
{
    /// <summary>The file at the Brain's root that declares its areas.</summary>
    public const string FileName = "CONVENTIONS.md";

    /// <summary>The key the Brain resolves under through
    /// <see cref="IDevbookFolderSource"/>.</summary>
    public const string FolderKey = "brain";

    /// <summary>What the Brain's one folder, and every location it resolves to, is
    /// labelled — the word <c>BrainScope.Name</c> puts a scope into, spelt here
    /// because this project references nothing.</summary>
    public const string ScopeLabel = "Brain";

    /// <summary>The one folder setting the Brain has: keyed
    /// <see cref="FolderKey"/>, its own root, never overridden. The single
    /// definition the Brain's folder source and the assistant tools both name, so
    /// the Brain cannot be described two ways.</summary>
    public static DevbookFolderSetting Folder { get; } =
        new(FolderKey, ScopeLabel, string.Empty, SupportsPathOverride: false);

    /// <summary>The heading the areas are declared under.</summary>
    public const string AreasHeading = "Areas";

    /// <summary>The heading the folders that are never areas are listed under.</summary>
    public const string IgnoreHeading = "Ignore";

    /// <summary>The PARA set, in its reading order: the areas a Brain has when it
    /// declares none.</summary>
    public static IReadOnlyList<BrainArea> Para { get; } =
    [
        new("Inbox", "_inbox", 1, "Notes not yet filed; where a capture lands."),
        new("Projects", "projects", 2, "Active work with a goal and a deadline."),
        new("Areas", "areas", 3, "Ongoing responsibilities without a deadline."),
        new("Resources", "resources", 4, "Reference material and collected knowledge."),
        new("People", "people", 5, "Notes about the people the work involves."),
        new("Journal", "journal", 6, "Dated entries."),
        new("Archive", "archive", 7, "Inactive notes kept for future search.")
    ];

    /// <summary>
    /// The areas of the Brain at <paramref name="brainRoot"/>, in tab order: the
    /// ones its <c>CONVENTIONS.md</c> declares — the PARA set when the file is
    /// missing, unreadable or declares none — then every other top folder holding
    /// a note, minus the ignored folders (<see cref="Resolve"/>). One listing of
    /// the Brain's root and, per candidate folder, a walk of names that stops at
    /// the first note. Never throws.
    /// </summary>
    public static IReadOnlyList<BrainArea> Read(string? brainRoot)
    {
        if (string.IsNullOrWhiteSpace(brainRoot)) return Para;

        var markdown = ReadConventions(brainRoot);
        return Resolve(Declared(markdown), Ignored(markdown), TopFolders(brainRoot), folder => HoldsNote(System.IO.Path.Combine(brainRoot, folder)));
    }

    /// <summary>
    /// The areas, from what <see cref="Read"/> finds on disk: <paramref name="declared"/>
    /// (or the PARA set when it is empty) in their order, then every folder of
    /// <paramref name="topFolders"/> that can be an area, is neither declared nor
    /// ignored, and <paramref name="holdsNote"/> — by folder name, ordinal and
    /// case-insensitive, named after the folder. A folder in
    /// <paramref name="ignored"/> is never an area, declared or not, so ignoring
    /// every declared area leaves none rather than bringing the PARA set back.
    /// Folders compare case-insensitively throughout, and only a discovered
    /// candidate is ever probed. Orders run from 1. Nothing declared, ignored or
    /// discovered answers <see cref="Para"/> itself.
    /// </summary>
    public static IReadOnlyList<BrainArea> Resolve(
        IReadOnlyList<BrainArea> declared,
        IReadOnlyCollection<string> ignored,
        IEnumerable<string> topFolders,
        Func<string, bool> holdsNote)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(ignored);
        ArgumentNullException.ThrowIfNull(topFolders);
        ArgumentNullException.ThrowIfNull(holdsNote);

        var leading = declared.Count > 0 ? declared : Para;
        var skipped = new HashSet<string>(ignored, StringComparer.OrdinalIgnoreCase);
        var named = new HashSet<string>(leading.Select(area => area.Folder), StringComparer.OrdinalIgnoreCase);

        var kept = leading.Where(area => !skipped.Contains(area.Folder)).ToList();
        var discovered = topFolders
            .Where(folder => IsAreaFolder(folder) && !skipped.Contains(folder) && named.Add(folder))
            .Order(FolderOrder)
            .Where(holdsNote)
            .ToList();

        if (declared.Count == 0 && kept.Count == Para.Count && discovered.Count == 0) return Para;

        var areas = new List<BrainArea>(kept.Count + discovered.Count);
        foreach (var area in kept) areas.Add(area with { Order = areas.Count + 1 });
        foreach (var folder in discovered) areas.Add(new BrainArea(NameFor(folder), folder, areas.Count + 1));
        return areas;
    }

    /// <summary>The folders the <c>CONVENTIONS.md</c> of the Brain at
    /// <paramref name="brainRoot"/> lists under <c>Ignore</c> (<see cref="Ignored"/>):
    /// one read of that file, no listing. Empty while there is none. Never throws.</summary>
    public static IReadOnlyList<string> ReadIgnored(string? brainRoot) =>
        string.IsNullOrWhiteSpace(brainRoot) ? [] : Ignored(ReadConventions(brainRoot));

    /// <summary>
    /// The folders of the Brain at <paramref name="brainRoot"/> that <b>Show
    /// hidden</b> brings back as tabs (<see cref="Hidden(IReadOnlyCollection{string}, IEnumerable{string})"/>):
    /// the ignored ones and the dot-folders there now. Empty while no folder is
    /// given or it is missing. Never throws.
    /// </summary>
    public static IReadOnlyList<BrainArea> Hidden(string? brainRoot)
    {
        if (string.IsNullOrWhiteSpace(brainRoot)) return [];

        return Hidden(Ignored(ReadConventions(brainRoot)), TopFolders(brainRoot));
    }

    /// <summary>
    /// The hidden folders among <paramref name="topFolders"/>: each one
    /// <paramref name="ignored"/> names, case-insensitively, and each dot-folder,
    /// by folder name as the discovered areas are ordered, named after the folder
    /// without its dot — <c>.obsidian</c> reads Obsidian — with orders from 1.
    /// Never an area: search and the database leave them out.
    /// </summary>
    public static IReadOnlyList<BrainArea> Hidden(IReadOnlyCollection<string> ignored, IEnumerable<string> topFolders)
    {
        ArgumentNullException.ThrowIfNull(ignored);
        ArgumentNullException.ThrowIfNull(topFolders);

        var skipped = new HashSet<string>(ignored, StringComparer.OrdinalIgnoreCase);
        return
        [
            .. topFolders
                .Where(folder => !string.IsNullOrWhiteSpace(folder) && (folder.StartsWith('.') || skipped.Contains(folder)))
                .Distinct(StringComparer.Ordinal)
                .Order(FolderOrder)
                .Select((folder, index) => new BrainArea(NameFor(folder.TrimStart('.') is { Length: > 0 } bare ? bare : folder), folder, index + 1))
        ];
    }

    /// <summary>
    /// Whether <paramref name="area"/> holds dated entries, which read newest
    /// first: the PARA <c>journal</c> folder under any declaration, or an area
    /// whose <c>Holds</c> line in <c>CONVENTIONS.md</c> says <i>dated</i>.
    /// </summary>
    public static bool IsDated(BrainArea area)
    {
        ArgumentNullException.ThrowIfNull(area);
        return string.Equals(area.Folder, "journal", StringComparison.OrdinalIgnoreCase)
               || DatedWord().IsMatch(area.Holds);
    }

    /// <summary>The areas <paramref name="markdown"/> declares, or the PARA set
    /// when it declares none.</summary>
    public static IReadOnlyList<BrainArea> Parse(string? markdown)
    {
        var declared = Declared(markdown);
        return declared.Count > 0 ? declared : Para;
    }

    /// <summary>The areas <paramref name="markdown"/> declares, in order, and
    /// nothing else: empty when it declares none. No folder on disk is asked —
    /// what <b>Create conventions</b> scaffolds, and the base <see cref="Resolve"/>
    /// adds the discovered areas to.</summary>
    public static IReadOnlyList<BrainArea> Declared(string? markdown)
    {
        var areas = new List<BrainArea>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var position = 0;
        var ordered = Section(markdown, AreasHeading)
            .Select((candidate, index) => (candidate.Name, candidate.Folder, candidate.Order, candidate.Holds, Index: index))
            .OrderBy(candidate => candidate.Order ?? long.MaxValue)
            .ThenBy(candidate => candidate.Index);

        foreach (var (name, folder, _, holds, _) in ordered)
        {
            if (!IsAreaFolder(folder) || !seen.Add(folder)) continue;
            areas.Add(new BrainArea(string.IsNullOrWhiteSpace(name) ? NameFor(folder) : name.Trim(), folder, ++position, holds));
        }

        return areas;
    }

    /// <summary>
    /// The folders <paramref name="markdown"/> lists under its <c>Ignore</c>
    /// heading, in the order written: list items that name the folder in
    /// backticks or bare, as <c>Areas</c> takes them, or a table's <c>Folder</c>
    /// column. A folder that is not one plain segment is skipped, and one listed
    /// twice, whatever its case, counts once. Empty when there is no such section.
    /// </summary>
    public static IReadOnlyList<string> Ignored(string? markdown)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return [.. Section(markdown, IgnoreHeading).Select(candidate => candidate.Folder).Where(folder => IsAreaFolder(folder) && seen.Add(folder))];
    }

    /// <summary>Every folder a section under <paramref name="headingName"/>
    /// names, as written: table rows with a <c>Folder</c> column and list items,
    /// outside code fences.</summary>
    private static List<(string? Name, string Folder, long? Order, string Holds)> Section(string? markdown, string headingName)
    {
        var candidates = new List<(string? Name, string Folder, long? Order, string Holds)>();
        if (string.IsNullOrEmpty(markdown)) return candidates;

        // A byte order mark is not text: File.ReadAllText drops it, Node keeps it.
        if (markdown[0] == '\uFEFF') markdown = markdown[1..];

        var lines = markdown.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var fence = (string?)null;
        int? sectionLevel = null;
        string[]? header = null;

        foreach (var raw in lines)
        {
            var fenceMatch = FenceLine().Match(raw);
            if (fence is not null)
            {
                if (fenceMatch.Success && fenceMatch.Groups[1].Value[0] == fence[0] && fenceMatch.Groups[1].Length >= fence.Length) fence = null;
                continue;
            }

            if (fenceMatch.Success)
            {
                fence = fenceMatch.Groups[1].Value;
                continue;
            }

            var heading = Heading().Match(raw);
            if (heading.Success)
            {
                var level = heading.Groups[1].Length;
                if (sectionLevel is { } open && level <= open) sectionLevel = null;
                if (sectionLevel is null && string.Equals(heading.Groups[2].Value.Trim().TrimEnd('#').Trim(), headingName, StringComparison.OrdinalIgnoreCase))
                {
                    sectionLevel = level;
                    header = null;
                }

                continue;
            }

            if (sectionLevel is null) continue;

            var line = raw.Trim();
            if (line.StartsWith('|'))
            {
                var cells = Cells(line);
                if (header is null)
                {
                    header = [.. cells.Select(cell => cell.ToLowerInvariant())];
                    continue;
                }

                if (cells.All(cell => cell.Length > 0 && cell.All(c => c is '-' or ':' or ' '))) continue;

                var folderColumn = Array.IndexOf(header, "folder");
                if (folderColumn < 0 || folderColumn >= cells.Length) continue;

                var nameColumn = Array.FindIndex(header, cell => cell is "area" or "name");
                var orderColumn = Array.IndexOf(header, "order");
                var holdsColumn = Array.IndexOf(header, "holds");
                long? order = orderColumn >= 0 && orderColumn < cells.Length && OrderNumber().IsMatch(cells[orderColumn])
                    ? long.Parse(cells[orderColumn], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture)
                    : null;

                candidates.Add((
                    nameColumn >= 0 && nameColumn < cells.Length ? cells[nameColumn] : null,
                    Unquote(cells[folderColumn]),
                    order,
                    holdsColumn >= 0 && holdsColumn < cells.Length ? cells[holdsColumn] : string.Empty));
                continue;
            }

            header = null;

            var item = ListItem().Match(raw);
            if (!item.Success) continue;

            var text = item.Groups[1].Value.Trim();
            var code = Code().Match(text);
            if (code.Success)
            {
                var name = Separators().Replace(text[..code.Index], string.Empty).Trim();
                candidates.Add((name, code.Groups[1].Value.Trim(), null, string.Empty));
            }
            else
            {
                candidates.Add((null, text, null, string.Empty));
            }
        }

        return candidates;
    }

    /// <summary>Whether <paramref name="folder"/> can be an area: one plain path
    /// segment, not a dot-folder, with no character a folder name cannot hold.</summary>
    public static bool IsAreaFolder(string? folder) =>
        !string.IsNullOrWhiteSpace(folder)
        && folder == folder.Trim()
        && !folder.StartsWith('.')
        && !folder.EndsWith('.')
        && folder.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) < 0
        && !folder.Any(char.IsControl);

    /// <summary>An area's name from its folder: <c>_inbox</c> reads Inbox,
    /// <c>side-projects</c> reads Side projects.</summary>
    public static string NameFor(string folder)
    {
        var words = folder.Trim('_', '-', ' ').Replace('-', ' ').Replace('_', ' ');
        return words.Length == 0 ? folder : char.ToUpperInvariant(words[0]) + words[1..];
    }

    /// <summary>Folder names in tab order: ordinal and case-insensitive, then
    /// ordinal, so two names that differ only in case still sort the same way
    /// every time. <c>brain.mjs</c> compares by upper case to match.</summary>
    private static readonly IComparer<string> FolderOrder = Comparer<string>.Create((left, right) =>
    {
        var order = StringComparer.OrdinalIgnoreCase.Compare(left, right);
        return order != 0 ? order : StringComparer.Ordinal.Compare(left, right);
    });

    /// <summary>Every entry of a listing: hidden and system ones and reparse
    /// points too — OneDrive's placeholders are reparse points — and none it may
    /// not read.</summary>
    private static readonly EnumerationOptions Listing = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = 0
    };

    /// <summary>A note by its name alone: <c>.md</c>, matched as case-sensitively
    /// as the builder's walk matches it.</summary>
    private static readonly EnumerationOptions NoteListing = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
        MatchType = MatchType.Simple,
        MatchCasing = MatchCasing.CaseSensitive
    };

    /// <summary>The text of the Brain's <c>CONVENTIONS.md</c>, or null when it is
    /// missing or cannot be read.</summary>
    private static string? ReadConventions(string brainRoot)
    {
        try
        {
            var path = System.IO.Path.Combine(brainRoot, FileName);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The names of the folders at the Brain's root, links and
    /// placeholders included; none when it cannot be listed.</summary>
    private static List<string> TopFolders(string brainRoot)
    {
        try
        {
            return [.. new DirectoryInfo(brainRoot).EnumerateDirectories("*", Listing).Select(directory => directory.Name)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return [];
        }
    }

    /// <summary>
    /// Whether a <c>.md</c> file sits anywhere under <paramref name="directory"/>,
    /// outside its dot sub-folders: names only, stopping at the first, so no file
    /// is opened and a cloud placeholder is never downloaded. A sub-folder that is
    /// a link is not followed, as the builder's walk does not follow one; a
    /// placeholder folder is. A folder that holds no note is listed to the
    /// bottom, on every <see cref="Read"/> and so every staleness check: a large
    /// attachments tree costs its whole listing until it is ignored.
    /// </summary>
    private static bool HoldsNote(string directory)
    {
        try
        {
            if (Directory.EnumerateFiles(directory, "*.md", NoteListing).Any()) return true;

            foreach (var child in new DirectoryInfo(directory).EnumerateDirectories("*", Listing))
            {
                if (child.Name.StartsWith('.')) continue;
                if (child.Attributes.HasFlag(FileAttributes.ReparsePoint) && IsLink(child)) continue;
                if (HoldsNote(child.FullName)) return true;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            // A folder that cannot be listed shows no note.
        }

        return false;
    }

    /// <summary>Whether a reparse-point folder is a link — a symbolic link or a
    /// junction, which could loop — rather than a placeholder. One whose target
    /// cannot be read is taken for a link, as <c>DevbookBuildFiles</c> takes it.</summary>
    private static bool IsLink(DirectoryInfo directory)
    {
        try
        {
            return directory.LinkTarget is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static string[] Cells(string line)
    {
        var inner = line.Trim();
        if (inner.StartsWith('|')) inner = inner[1..];
        if (inner.EndsWith('|')) inner = inner[..^1];
        return [.. inner.Split('|').Select(cell => cell.Trim())];
    }

    private static string Unquote(string cell)
    {
        var match = Code().Match(cell);
        return (match.Success ? match.Groups[1].Value : cell).Trim();
    }

    // ASCII classes, spelt out, so the C# and JavaScript readers agree on every
    // character: .NET's \s and \d are not JavaScript's.
    [GeneratedRegex(@"^ {0,3}(`{3,}|~{3,})")]
    private static partial Regex FenceLine();

    [GeneratedRegex(@"^ {0,3}(#{1,6})[ \t]+(.*)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^[ \t]*(?:[-*+]|[0-9]+[.)])[ \t]+(.*)$")]
    private static partial Regex ListItem();

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex Code();

    [GeneratedRegex(@"[ \t]*[—–:-]+[ \t]*$")]
    private static partial Regex Separators();

    /// <summary>An <c>Order</c> cell: digits, few enough for both readers to hold
    /// exactly.</summary>
    [GeneratedRegex("^[0-9]{1,15}$")]
    private static partial Regex OrderNumber();

    /// <summary>The word <i>dated</i> in an area's <c>Holds</c> line.</summary>
    [GeneratedRegex(@"(?<![A-Za-z])dated(?![A-Za-z])", RegexOptions.IgnoreCase)]
    private static partial Regex DatedWord();
}

/// <summary>One area of the Brain: its name as the tab shows it, its top
/// folder, its place in the tab order, from 1, and a line on what goes there
/// (empty where the declaration gives none).</summary>
public sealed record BrainArea(string Name, string Folder, int Order, string Holds = "");
