using Backlog.UI.Components.Badges;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The mode chips, the stage marks and the legend of
/// <c>.devbook/design/effort-and-mode-chips.md</c>.
/// </summary>
public sealed class ModeChipAndStageMarkTests
{
    [Theory]
    [InlineData("inline", "Ran in the owner session")]
    [InlineData("delegate", "Handed to a sub-agent")]
    [InlineData("fork", "Ran in a fork of the owner session")]
    [InlineData("gate", "Your approval: no model runs it")]
    public void Each_mode_prints_its_word_in_its_own_shape_and_says_what_it_means(string mode, string meaning)
    {
        using var context = new BunitContext();

        var chip = context.Render<ModeChip>(parameters => parameters.Add(p => p.Mode, mode)).Find(".mode-chip");

        Assert.Equal(mode, chip.TextContent.Trim());
        Assert.Contains($"mode-chip--{mode}", chip.ClassName);
        Assert.Equal(meaning, chip.GetAttribute("title"));

        // Requirement: Mode is told apart by shape — named for assistive technology.
        Assert.Equal($"{mode}: {meaning}", chip.GetAttribute("aria-label"));
    }

    [Fact]
    public void A_word_no_mode_names_is_drawn_verbatim_and_names_the_modes()
    {
        using var context = new BunitContext();

        var chip = context.Render<ModeChip>(parameters => parameters.Add(p => p.Mode, "teleport")).Find(".mode-chip");

        Assert.Contains("mode-chip--unknown", chip.ClassName);
        Assert.Contains("expected one of inline, delegate, fork, gate", chip.GetAttribute("title"));
    }

    [Fact]
    public void No_mode_draws_nothing()
    {
        using var context = new BunitContext();

        Assert.Equal(string.Empty, context.Render<ModeChip>(parameters => parameters.Add(p => p.Mode, " ")).Markup.Trim());
    }

    [Fact]
    public void The_drift_mark_is_announced_as_its_differences_never_as_its_glyph()
    {
        using var context = new BunitContext();

        var mark = context.Render<StageMark>(parameters => parameters
            .Add(p => p.Kind, StageMarkKind.Drift)
            .Add(p => p.Title, "Not as configured: model Opus 5.5 → Sonnet 5.5")).Find(".stage-mark");

        Assert.Contains("stage-mark--drift", mark.ClassName);
        Assert.Equal("Not as configured: model Opus 5.5 → Sonnet 5.5", mark.GetAttribute("aria-label"));
        Assert.Equal("true", mark.QuerySelector("span")!.GetAttribute("aria-hidden"));
        Assert.Equal("≠", mark.TextContent.Trim());
    }

    [Fact]
    public void The_inferred_mark_says_so_without_a_title_of_its_own()
    {
        using var context = new BunitContext();

        var mark = context.Render<StageMark>(parameters => parameters.Add(p => p.Kind, StageMarkKind.Inferred)).Find(".stage-mark");

        Assert.Equal("?", mark.TextContent.Trim());
        Assert.Equal("Inferred, not recorded by the run", mark.GetAttribute("title"));
    }

    [Fact]
    public void No_mark_draws_nothing()
    {
        using var context = new BunitContext();

        Assert.Equal(string.Empty, context.Render<StageMark>().Markup.Trim());
    }

    [Fact]
    public void The_legend_shows_the_four_modes_then_the_two_marks_and_no_effort()
    {
        using var context = new BunitContext();

        var legend = context.Render<StageLegend>();
        var entries = legend.FindAll(".stage-legend__entry");

        Assert.Equal(6, entries.Count);
        Assert.Equal(["inline", "delegate", "fork", "gate"], legend.FindAll(".mode-chip").Select(chip => chip.TextContent.Trim()));
        Assert.Equal(["inferred", "drift"], legend.FindAll(".stage-mark").Select(mark => mark.GetAttribute("data-mark")));
        Assert.Contains("not as configured", entries[5].TextContent);
        Assert.Empty(legend.FindAll(".effort-chip"));
    }
}
