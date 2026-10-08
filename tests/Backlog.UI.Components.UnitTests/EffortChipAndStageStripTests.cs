using Backlog.UI.Components.Badges;
using Backlog.UI.Components.Feedback;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The effort chip of <c>.devbook/design/effort-and-mode-chips.md#effort-chips</c> and
/// the compact stage strip a list row draws a run's progress with.
/// </summary>
public sealed class EffortChipAndStageStripTests
{
    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("xhigh")]
    [InlineData("max")]
    public void Each_step_prints_its_word_in_its_own_treatment(string effort)
    {
        using var context = new BunitContext();

        var chip = context.Render<EffortChip>(parameters => parameters.Add(p => p.Effort, effort)).Find(".effort-chip");

        Assert.Equal(effort, chip.TextContent.Trim());
        Assert.Contains($"effort-chip--{effort}", chip.ClassName);
        Assert.Equal($"Effort: {effort}", chip.GetAttribute("title"));
        Assert.Equal($"Effort: {effort}", chip.GetAttribute("aria-label"));
    }

    /// <summary>Requirement: No chip without a recorded effort — no empty chip and no
    /// "unknown" chip in its place.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_recorded_effort_draws_nothing(string? effort)
    {
        using var context = new BunitContext();

        var chip = context.Render<EffortChip>(parameters => parameters.Add(p => p.Effort, effort));

        Assert.Equal(string.Empty, chip.Markup.Trim());
    }

    [Fact]
    public void A_value_off_the_ladder_is_drawn_verbatim_as_low_and_names_the_ladder()
    {
        using var context = new BunitContext();

        var chip = context.Render<EffortChip>(parameters => parameters.Add(p => p.Effort, "turbo")).Find(".effort-chip");

        Assert.Equal("turbo", chip.TextContent.Trim());
        Assert.Contains("effort-chip--unknown", chip.ClassName);
        Assert.Contains("expected one of low, medium, high, xhigh, max", chip.GetAttribute("title"));
    }

    [Fact]
    public void A_step_is_matched_whatever_its_case()
    {
        using var context = new BunitContext();

        var chip = context.Render<EffortChip>(parameters => parameters.Add(p => p.Effort, "High")).Find(".effort-chip");

        Assert.Contains("effort-chip--high", chip.ClassName);
    }

    [Fact]
    public void The_strip_is_a_mark_per_stage_in_its_tone_named_by_the_stages_done()
    {
        using var context = new BunitContext();

        var strip = context.Render<StageStrip>(parameters => parameters
            .Add(p => p.Steps, (IReadOnlyList<FlowStep>)
            [
                new("Scope", FlowStepTone.Done, "Done"),
                new("Implement", FlowStepTone.Active, "In progress"),
                new("Review", FlowStepTone.Pending, "Pending")
            ])
            .Add(p => p.Summary, "flow-code · Implement (2 of 3)"));

        var marks = strip.FindAll(".stage-strip__mark");

        Assert.Equal(["done", "active", "pending"], marks.Select(mark => mark.ClassName!.Split("--")[1]));
        Assert.Equal("Implement: In progress", marks[1].GetAttribute("title"));
        Assert.Equal("1 of 3 stages done", strip.Find(".stage-strip__marks").GetAttribute("aria-label"));
        Assert.Equal("img", strip.Find(".stage-strip__marks").GetAttribute("role"));
        Assert.Equal("flow-code · Implement (2 of 3)", strip.Find(".stage-strip__summary").TextContent.Trim());
    }

    [Fact]
    public void With_no_stages_the_summary_stands_alone_and_with_neither_nothing_draws()
    {
        using var context = new BunitContext();

        var summaryOnly = context.Render<StageStrip>(parameters => parameters.Add(p => p.Summary, "orch-feature · Done"));

        Assert.Empty(summaryOnly.FindAll(".stage-strip__marks"));
        Assert.Equal("orch-feature · Done", summaryOnly.Find(".stage-strip__summary").TextContent.Trim());

        var nothing = context.Render<StageStrip>();

        Assert.Equal(string.Empty, nothing.Markup.Trim());
    }
}
