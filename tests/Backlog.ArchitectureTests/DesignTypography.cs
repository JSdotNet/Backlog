using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The type scale as <c>.design/typography-and-layout.md</c> states it, read back
/// so tests can hold the stylesheets to it — the counterpart to
/// <see cref="DesignPalette"/> for the values that are not colours.
/// </summary>
internal static class DesignTypography
{
    /// <summary>Every weight the "Weights, Line Heights, Letter Spacing" table
    /// declares, as the bare numbers a stylesheet would write.
    ///
    /// <para>That table is the whole scale: its first cell is a
    /// <c>font-weight-*</c> token and its second is the value, and the file names
    /// no weight anywhere else. Read out rather than listed here so a weight added
    /// to the document is permitted the moment the document permits it — a test
    /// carrying its own copy of the scale is a second source for the one thing
    /// this file is supposed to be the only source of.</para></summary>
    public static HashSet<string> SpecifiedWeights()
    {
        var markdown = System.IO.File.ReadAllText(
            RepositoryRoot.File(".design", "typography-and-layout.md"));

        return
        [
            .. Regex.Matches(markdown, @"^\|\s*`font-weight-[a-z]+`\s*\|\s*(\d+)\s*\|", RegexOptions.Multiline)
                .Select(row => row.Groups[1].Value)
        ];
    }
}
