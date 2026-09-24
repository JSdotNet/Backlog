using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// A plan's step is laid out as numbers — an offset and a width in rem, from
/// <c>RoadmapStepLayout</c> — and the stylesheet must draw exactly those numbers.
/// <para>
/// Found in the desktop harness while validating the combined import: a plan whose
/// bar was eight days of a zoomed-out timeline laid its estimated steps out a quarter
/// of a rem wide, and each was drawn half as wide again, because <c>.roadmap-step</c>
/// carried inline padding and a border-box element cannot be narrower than its
/// padding. The steps overlapped the one after them. The layout's own tests could not
/// see it — the numbers were right — so the rule is asserted where the defect was.
/// </para>
/// </summary>
public class RoadmapStepWidthTests
{
    [Fact]
    public void A_step_carries_no_inline_padding_that_would_widen_it_past_its_laid_out_width()
    {
        var stylesheet = new FileInfo(Path.Combine(
            Repository.Root.FullName, "src", "Core", "Backlog.UI.Components", "wwwroot", "components.css"));
        Assert.True(stylesheet.Exists, "The library's stylesheet is not where this test looks for it.");

        var rule = Regex.Match(
            File.ReadAllText(stylesheet.FullName),
            @"^\.roadmap-step\s*\{(?<body>[^}]*)\}",
            RegexOptions.Multiline);
        Assert.True(rule.Success, "components.css has no .roadmap-step rule, so nothing positions a step.");

        // Comments first, so a sentence about padding is not read as a declaration.
        var body = Regex.Replace(rule.Groups["body"].Value, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        foreach (Match declaration in Regex.Matches(body, @"padding(?<side>-[a-z-]+)?\s*:\s*(?<value>[^;]+);"))
        {
            var side = declaration.Groups["side"].Value;
            var value = declaration.Groups["value"].Value.Trim();

            if (side is "-top" or "-bottom" or "-block") continue;

            var horizontal = side.Length == 0 ? Horizontal(value) : value;
            Assert.True(
                horizontal is "0",
                $".roadmap-step declares padding{side}: {value}. The step's width arrives inline from the layout, "
                + "and any inline padding is a floor under it: a narrow step is drawn wider than laid out and "
                + "overlaps the next. Put the inset on .roadmap-step__title or .roadmap-step__marker instead.");
        }
    }

    /// <summary>The inline part of a <c>padding</c> shorthand: its only value, or its
    /// second.</summary>
    private static string Horizontal(string shorthand)
    {
        var parts = shorthand.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1 ? parts[0] : parts[1];
    }
}
