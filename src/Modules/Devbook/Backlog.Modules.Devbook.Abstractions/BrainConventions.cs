using System.Text.RegularExpressions;

namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// The Brain's own convention: which areas it has, read from the
/// <c>CONVENTIONS.md</c> at the root of the Brain folder (local ADR 0026 §2).
///
/// <para>Each area is one top folder of the Brain and one tab of the pane. The
/// file declares them under a heading named <c>Areas</c>, at any level, in one of
/// two shapes — a table whose columns include <c>Folder</c> (and optionally
/// <c>Area</c> or <c>Name</c>, <c>Holds</c> and <c>Order</c>), which is the shape
/// the template <b>Create conventions</b> writes (<c>BrainConventionTemplate</c>),
/// or a list:</para>
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
/// </code>
///
/// <para>A list item names its folder in backticks, or is the folder when it has
/// no backticks; the text before the backticks, without its dash, is the area's
/// name. An area with no name is named after its folder. The order is the order
/// written, unless a table's <c>Order</c> column numbers the rows. A folder that
/// is not one plain segment — a path, <c>..</c>, a dot-folder — is skipped, and
/// so is a folder declared twice. When nothing usable is declared, or there is no
/// <c>CONVENTIONS.md</c> at all, the PARA set applies (<see cref="Para"/>).</para>
///
/// <para>This is the one reader of the file in the app: the database builder, the
/// Brain folder source and <b>Create conventions</b> all ask it.
/// <c>tools/devbook/brain.mjs</c> parses the same file the same way for the Node
/// writer, and <c>DevbookBuilderParityTests</c> holds the two together on a Brain
/// fixture.</para>
/// </summary>
public static partial class BrainConventions
{
    /// <summary>The file at the Brain's root that declares its areas.</summary>
    public const string FileName = "CONVENTIONS.md";

    /// <summary>The key the Brain resolves under through
    /// <see cref="IDevbookFolderSource"/>.</summary>
    public const string FolderKey = "brain";

    /// <summary>The heading the areas are declared under.</summary>
    public const string AreasHeading = "Areas";

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
    /// The areas the Brain at <paramref name="brainRoot"/> declares, or the PARA
    /// set when its <c>CONVENTIONS.md</c> is missing, unreadable or declares none.
    /// Never throws.
    /// </summary>
    public static IReadOnlyList<BrainArea> Read(string? brainRoot)
    {
        if (string.IsNullOrWhiteSpace(brainRoot)) return Para;

        try
        {
            var path = System.IO.Path.Combine(brainRoot, FileName);
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : Para;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Para;
        }
    }

    /// <summary>The areas <paramref name="markdown"/> declares, or the PARA set
    /// when it declares none.</summary>
    public static IReadOnlyList<BrainArea> Parse(string? markdown)
    {
        var declared = Declared(markdown);
        return declared.Count > 0 ? declared : Para;
    }

    /// <summary>The areas <paramref name="markdown"/> declares, in order, and
    /// nothing else: empty when it declares none.</summary>
    public static IReadOnlyList<BrainArea> Declared(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return [];

        // A byte order mark is not text: File.ReadAllText drops it, Node keeps it.
        if (markdown[0] == '\uFEFF') markdown = markdown[1..];

        var candidates = new List<(string? Name, string Folder, long? Order, string Holds)>();
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
                if (sectionLevel is null && string.Equals(heading.Groups[2].Value.Trim().TrimEnd('#').Trim(), AreasHeading, StringComparison.OrdinalIgnoreCase))
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

        var areas = new List<BrainArea>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var position = 0;
        var ordered = candidates
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
}

/// <summary>One area of the Brain: its name as the tab shows it, its top
/// folder, its place in the tab order, from 1, and a line on what goes there
/// (empty where the declaration gives none).</summary>
public sealed record BrainArea(string Name, string Folder, int Order, string Holds = "");
