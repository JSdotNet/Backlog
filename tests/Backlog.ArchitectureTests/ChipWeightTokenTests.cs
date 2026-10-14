using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The effort chip, the mode chip and the stage mark take their weights from the tokens
/// <c>.devbook/design/effort-and-mode-chips.md</c> names — <c>font-weight-medium</c>,
/// <c>-normal</c> and <c>-bold</c> — and the library declares each of them at the value
/// <c>.devbook/design/typography-and-layout.md</c> gives it. A literal in their place
/// reads the same today and drifts the day the scale moves.
/// </summary>
public class ChipWeightTokenTests
{
    [Theory]
    [InlineData(".effort-chip", "medium")]
    [InlineData(".mode-chip", "normal")]
    [InlineData(".stage-mark", "bold")]
    public void A_chips_base_rule_takes_its_weight_from_the_token_its_chapter_names(string selector, string step)
    {
        var css = File.ReadAllText(DesignPalette.LibraryStylesheet);

        var rule = Regex.Match(css, @"(?m)^" + Regex.Escape(selector) + @"\s*\{(?<body>[^}]*)\}");

        Assert.True(rule.Success, $"components.css has no {selector} rule.");
        Assert.Matches(@"font-weight\s*:\s*var\(--font-weight-" + step + @"\)\s*;", rule.Groups["body"].Value);
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("medium")]
    [InlineData("semibold")]
    [InlineData("bold")]
    public void A_weight_token_is_declared_at_the_value_the_type_scale_gives_it(string step)
    {
        var scale = File.ReadAllText(RepositoryRoot.File(".devbook", "design", "typography-and-layout.md"));
        var specified = Regex.Match(scale, @"(?m)^\|\s*`font-weight-" + step + @"`\s*\|\s*(?<value>\d+)\s*\|");

        Assert.True(specified.Success, $"typography-and-layout.md declares no font-weight-{step}.");

        var declared = Regex.Match(File.ReadAllText(DesignPalette.LibraryStylesheet), @"--font-weight-" + step + @"\s*:\s*(?<value>\d+)\s*;");

        Assert.True(declared.Success, $"components.css declares no --font-weight-{step}.");
        Assert.Equal(specified.Groups["value"].Value, declared.Groups["value"].Value);
    }
}
