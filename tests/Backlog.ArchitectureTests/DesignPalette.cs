using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The palette as <c>.design/color-scheme.md</c> states it — the single source
/// of token <em>values</em> that file claims to be, read back so tests can hold
/// things to it.
///
/// <para>This started inside <see cref="DesignTokenTests"/>, which checks the
/// component library's stylesheet against the document. <see
/// cref="BrandAssetTests"/> needs the same answer for the app icon, and two
/// copies of a markdown-table parser would be exactly the duplication those
/// tests exist to prevent: the copies drift, one of them stops seeing a table
/// the document has since reshaped, and the rule it enforces quietly weakens
/// while staying green.</para>
/// </summary>
internal static class DesignPalette
{
    /// <summary>The chapter that documents what the product deliberately is
    /// <em>not</em>: its table puts the org guide's superseded value beside the
    /// product's, so its second cell is a colour this file is declaring it does
    /// not use. Read as a declaration it would look like the file contradicting
    /// itself.</summary>
    private const string SupersededValuesChapter = "Surface and Border Deviation";

    /// <summary>The same colours as the tables in <c>color-scheme.md</c> give them.
    /// Every declaring table there puts the token in the first cell and its value
    /// in the second, so one pattern reads all of them — and a row whose second
    /// cell is not a literal (a token reference, or the per-stack mapping's CSS
    /// declaration) is not a value this can check.</summary>
    public static Dictionary<string, string> SpecifiedColors()
    {
        var markdown = File.ReadAllText(
            RepositoryRoot.File(".design", "color-scheme.md"));

        var declaring = string.Concat(
            Regex.Split(markdown, @"^(?=## )", RegexOptions.Multiline)
                .Where(chapter => !chapter.StartsWith($"## {SupersededValuesChapter}", StringComparison.Ordinal)));

        var rows = Regex.Matches(declaring, @"^\|\s*`((?:color|code)-[a-z0-9-]+)`\s*\|\s*`([^`]+)`\s*\|",
            RegexOptions.Multiline);

        var specified = new Dictionary<string, string>();

        foreach (var row in rows.Where(row => IsColorLiteral(row.Groups[2].Value)))
        {
            var token = row.Groups[1].Value;
            var value = Normalized(row.Groups[2].Value);

            // The file states most values twice, per-group and in the full
            // reference. Those two copies have to agree as well.
            Assert.True(
                !specified.TryGetValue(token, out var earlier) || earlier == value,
                $"color-scheme.md lists {token} as both {earlier} and {value}.");

            specified[token] = value;
        }

        return specified;
    }

    /// <summary>The value <c>color-scheme.md</c> gives one token, as an assertion:
    /// a caller naming a token the document does not declare has a stale name,
    /// and should hear that rather than silently compare against nothing.</summary>
    public static string Value(string token)
    {
        var specified = SpecifiedColors();

        Assert.True(
            specified.TryGetValue(token, out var value),
            $"color-scheme.md does not declare {token}. Either the token was renamed or the "
            + "table it lived in was reshaped; in both cases the rule reading it needs updating.");

        return value!;
    }

    public static bool IsColorLiteral(string value) =>
        value.TrimStart().StartsWith('#') || value.TrimStart().StartsWith("rgb", StringComparison.OrdinalIgnoreCase);

    /// <summary>Case and the spaces inside <c>rgba(...)</c> are formatting, not
    /// value: the markdown writes the scrim tight and the CSS writes it spaced.</summary>
    public static string Normalized(string value) =>
        Regex.Replace(value, @"\s+", string.Empty).ToLowerInvariant();
}
