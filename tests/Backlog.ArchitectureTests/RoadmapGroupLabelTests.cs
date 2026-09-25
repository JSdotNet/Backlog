using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The timeline's sidebar and its track are two lists of the same height in the
/// same order: every track row is placed at its index times the row height, and
/// every sidebar label has to land opposite it.
/// <para>
/// Found in the storybook: a group name written down the side of its rows had its
/// length as a floor on the group's height, so a one-row group with a longer name
/// grew past its one track row and every label after it sat lower than the row it
/// names. The name is now written across the group's first row instead, inside that
/// row's own label, so the group holds nothing but rows. What keeps the two columns
/// level is then that a row label never grows: one line, clipped, and nothing in
/// the group written sideways. bUnit has no layout to measure, so the rule is
/// asserted in the stylesheet.
/// </para>
/// </summary>
public class RoadmapGroupLabelTests
{
    private static readonly string Stylesheet = File.ReadAllText(Path.Combine(
        Repository.Root.FullName, "src", "Core", "Backlog.UI.Components", "wwwroot", "components.css"));

    [Fact]
    public void A_row_label_is_one_clipped_line_so_its_text_cannot_make_the_row_taller()
    {
        var row = Rule(".roadmap-timeline__row-name");

        Assert.True(Declares(row, "white-space", "nowrap"),
            ".roadmap-timeline__row-name must not wrap: a second line makes the sidebar row taller than its "
            + "track row, and every label after it drifts below the row it names.");
        Assert.True(Declares(row, "overflow", "hidden"),
            ".roadmap-timeline__row-name must clip, so a long group or lane name is cut short inside its row "
            + "rather than pushing the row's height or spilling over the next one.");
    }

    [Fact]
    public void A_group_name_is_written_across_its_row_not_down_the_group()
    {
        var label = Rule(".roadmap-timeline__group-name");

        Assert.False(Regex.IsMatch(label, @"(^|[;\s])writing-mode\s*:"),
            ".roadmap-timeline__group-name is written vertically again. Sideways, a name's length is a floor on "
            + "its group's height; write it across the group's first row.");
        Assert.False(Declares(label, "position", "absolute"),
            ".roadmap-timeline__group-name is positioned out of its row. It belongs inside the first row's label, "
            + "where the row's own height holds it.");
    }

    private static string Rule(string selector)
    {
        var rule = Regex.Match(
            Stylesheet,
            $@"^{Regex.Escape(selector)}\s*\{{(?<body>[^}}]*)\}}",
            RegexOptions.Multiline);
        Assert.True(rule.Success, $"components.css has no top-level {selector} rule.");

        // Comments first, so a sentence about a property is not read as declaring it.
        return Regex.Replace(rule.Groups["body"].Value, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
    }

    private static bool Declares(string body, string property, string value) =>
        Regex.IsMatch(body, $@"(^|[;\s]){Regex.Escape(property)}\s*:\s*{Regex.Escape(value)}\s*;");
}
