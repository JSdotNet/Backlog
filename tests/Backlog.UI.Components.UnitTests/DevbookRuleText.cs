using System.Text.RegularExpressions;

using Backlog.Tests;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The devbook contract as its rule text states it, read out of the vendored
/// copies under <c>tests/Fixtures/devbook-rules/</c>.
///
/// <para>The other half of <c>DevbookRuleTextContractTests</c>. The value sets the
/// product reads and writes against — <c>DevbookSchema</c> and the folder classes
/// that draw from it — are C#; the contract CI enforces is prose in another
/// repository. Pinning one against a copy of the other is the same move
/// <c>DevbookSchemaSource</c> makes against <c>devbook-schema.mjs</c>: the
/// fixture is the rule, so a contract bump is a fixture swap, and the failing
/// tests name each word that moved.</para>
///
/// <para>Deliberately narrow. Each accessor finds one table row or one bullet by
/// the words the rule opens it with and returns the backticked values in it — so
/// a rewording elsewhere in the file changes nothing, and a change to the list
/// itself fails loudly rather than parsing as an empty set.</para>
/// </summary>
internal static class DevbookRuleText
{
    private static readonly Regex Backticked = new("`([^`]+)`", RegexOptions.Compiled);

    public static string ChapterMetadata { get; } = Read("devbook-chapter-metadata.md");

    public static string Domain { get; } = Read("devbook-domain.md");

    public static string Annotations { get; } = Read("devbook-annotations.md");

    /// <summary>The <c>type</c> table in <c>devbook-chapter-metadata.md</c>: the
    /// chapter and file cells of one folder's row.</summary>
    public static (IReadOnlyList<string> Chapter, IReadOnlyList<string> File) TypeRow(string folder)
    {
        var cells = TableRow(ChapterMetadata, $"`{folder}/`", minimumCells: 3);
        return (Values(cells[1]), cells[2].Trim() == "none" ? [] : Values(cells[2]));
    }

    /// <summary><c>devbook-domain.md</c>'s own <c>type</c> table, one level.</summary>
    public static IReadOnlyList<string> DomainTypeRow(string level) =>
        Values(TableRow(Domain, level, minimumCells: 2)[1]);

    /// <summary>The folders a <c>status</c> table row names, by what the row says
    /// about the field — <c>optional</c> or <c>**required**</c>.</summary>
    public static IReadOnlyList<string> StatusFolders(string requirement)
    {
        foreach (var line in Lines(ChapterMetadata))
        {
            var cells = Cells(line);
            if (cells.Length >= 3 && cells[1].Trim() == requirement && cells[0].Contains("/`", StringComparison.Ordinal))
            {
                return [.. Values(cells[0]).Select(folder => folder.TrimEnd('/'))];
            }
        }

        throw Missing($"status row '{requirement}'");
    }

    /// <summary>The resting value named in the optional row's third cell.</summary>
    public static string RestingValue()
    {
        foreach (var line in Lines(ChapterMetadata))
        {
            var cells = Cells(line);
            if (cells.Length >= 3 && cells[1].Trim() == "optional") return Values(cells[2])[0];
        }

        throw Missing("resting value");
    }

    /// <summary>The two rungs, in the order the rule introduces them.</summary>
    public static IReadOnlyList<string> DecisionRungs() =>
    [
        Capture(ChapterMetadata, "The first is `([a-z-]+)`"),
        Capture(ChapterMetadata, "The second rung sits above it, `([a-z-]+)`")
    ];

    /// <summary>Every field bullet whose name starts with a prefix, with what the
    /// bullet's parenthesis says about it — <c>`domain/` only, optional</c>.</summary>
    public static IReadOnlyList<(string Field, string Scope)> FieldBullets(string prefix)
    {
        var pattern = new Regex($@"^- \*\*({Regex.Escape(prefix)}[a-z-]*)\*\* \(([^)]*)\)");
        return [.. Lines(ChapterMetadata)
            .Select(line => pattern.Match(line))
            .Where(match => match.Success)
            .Select(match => (match.Groups[1].Value, match.Groups[2].Value))];
    }

    /// <summary>The <c>review</c> states, as the bullet lists them.</summary>
    public static IReadOnlyList<string> ReviewStates()
    {
        var start = ChapterMetadata.IndexOf("- **review** (optional)", StringComparison.Ordinal);
        if (start < 0) throw Missing("review bullet");

        var end = ChapterMetadata.IndexOf("- **reviewer**", start, StringComparison.Ordinal);

        // Each state is written as `word` (waiting on …).
        return [.. Regex.Matches(ChapterMetadata[start..end], @"`([a-z-]+)`\s+\(waiting")
            .Select(match => match.Groups[1].Value)];
    }

    /// <summary>The <c>deployment</c> values, from the bullet that defines them in
    /// <c>devbook-domain.md</c>.</summary>
    public static IReadOnlyList<string> DeploymentValues() =>
    [
        Capture(Domain, "how the context ships\\. `([a-z]+)` is a deployable"),
        Capture(Domain, "`([a-z]+)` runs inside a modular monolith")
    ];

    /// <summary>The <c>index</c> values the rule defines.</summary>
    public static IReadOnlyList<string> IndexValues() =>
    [.. Regex.Matches(ChapterMetadata, "`index: ([a-z]+)`").Select(match => match.Groups[1].Value).Distinct()];

    /// <summary>The extension key shape, as the rule spells it.</summary>
    public static string ExtensionKeyShape() => Capture(ChapterMetadata, "their owner, `(ext\\.[^`]+)`");

    /// <summary>One row of the annotation field table: its default and its
    /// closed set.</summary>
    public static (string Default, IReadOnlyList<string> Values) AnnotationField(string field)
    {
        var cells = TableRow(Annotations, $"`{field}`", minimumCells: 3);
        // The cell names its set and then talks about a member of it again —
        // "`open` · `resolved`. Two states, and `resolved` is short-lived."
        var values = Values(cells[2]).Distinct(StringComparer.Ordinal).ToList();
        return (Values(cells[1]).Single(), values);
    }

    private static string[] TableRow(string text, string firstCell, int minimumCells)
    {
        foreach (var line in Lines(text))
        {
            var cells = Cells(line);
            if (cells.Length >= minimumCells && cells[0].Trim() == firstCell) return cells;
        }

        throw Missing($"table row starting '{firstCell}'");
    }

    /// <summary>The cells of a Markdown table row, leading and trailing pipes
    /// dropped. Rows in these rules sit inside list items, so the row is indented.</summary>
    private static string[] Cells(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith('|') || !trimmed.EndsWith('|')) return [];

        return trimmed[1..^1].Split('|');
    }

    private static IReadOnlyList<string> Values(string cell) =>
        [.. Backticked.Matches(cell).Select(match => match.Groups[1].Value)];

    private static string Capture(string text, string pattern)
    {
        var match = Regex.Match(text, pattern);
        return match.Success ? match.Groups[1].Value : throw Missing(pattern);
    }

    private static IEnumerable<string> Lines(string text) => text.Replace("\r\n", "\n").Split('\n');

    private static string Read(string name) =>
        File.ReadAllText(RepositoryRoot.File(["tests", "Fixtures", "devbook-rules", name]));

    private static InvalidOperationException Missing(string what) =>
        new($"The vendored devbook rule text no longer states {what}. See tests/Fixtures/devbook-rules/README.md.");
}
