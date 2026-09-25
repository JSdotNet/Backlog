using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The timeline's sidebar and its track are two lists of the same height in the
/// same order: every track row is placed at its index times the row height, and
/// every sidebar label has to land opposite it.
/// <para>
/// Found in the storybook: a group with one row and a name longer than that row is
/// tall. The name is written down the side, so its length was a floor on the
/// group's height, the sidebar group grew past its one track row, and every label
/// after it sat a little lower than the row it names — 4px, then 28px. The desktop
/// view had hidden this with an override of its own; every other host of the
/// library drew it. bUnit has no layout to measure, so the rule is asserted where
/// the defect was: the label must be out of flow, in a group that clips it.
/// </para>
/// </summary>
public class RoadmapGroupLabelTests
{
    private static readonly string Stylesheet = File.ReadAllText(Path.Combine(
        Repository.Root.FullName, "src", "Core", "Backlog.UI.Components", "wwwroot", "components.css"));

    [Fact]
    public void A_group_label_is_taken_out_of_flow_so_its_length_cannot_set_the_groups_height()
    {
        var label = Rule(".roadmap-timeline__group-name");

        Assert.True(
            Declares(label, "position", "absolute"),
            ".roadmap-timeline__group-name is in the group's flow. It is written vertically, so a name longer "
            + "than its rows makes the sidebar group taller than the track group, and every later label drifts "
            + "below its row. Position it absolutely inside the group.");
        // Physical, not inset-block: a logical inset is read in the element's own
        // writing mode, and the label is vertical-rl, so inset-block is left and
        // right. That shipped once and left every label at its text's height.
        Assert.True(
            Declares(label, "top", "0") && Declares(label, "bottom", "0"),
            ".roadmap-timeline__group-name must be stretched to the group's height with top: 0 and bottom: 0, "
            + "so the label is exactly as tall as the rows beside it. Not inset-block: in the label's vertical "
            + "writing mode that means left and right.");
        Assert.False(
            Regex.IsMatch(label, @"(^|[;\s])inset-block\s*:"),
            ".roadmap-timeline__group-name declares inset-block, which its vertical writing mode reads as left "
            + "and right. Use top and bottom.");
    }

    [Fact]
    public void A_group_is_the_labels_containing_block_and_clips_what_does_not_fit()
    {
        var group = Rule(".roadmap-timeline__group");

        Assert.True(Declares(group, "position", "relative"),
            ".roadmap-timeline__group must be position: relative, or the absolute label is placed against "
            + "some ancestor and spans the wrong height.");
        Assert.True(Declares(group, "overflow", "hidden"),
            ".roadmap-timeline__group must clip, so a label with less room than it wants is cut off inside "
            + "its own group rather than drawn over the next one.");
    }

    [Fact]
    public void The_group_rows_leave_room_for_the_label_they_no_longer_sit_beside_in_flow()
    {
        Assert.True(
            Declares(Rule(".roadmap-timeline__group-rows"), "margin-left", "var(--roadmap-band-width)"),
            "With the label out of flow, .roadmap-timeline__group-rows must step aside by --roadmap-band-width "
            + "or the row names are drawn under the label.");
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
