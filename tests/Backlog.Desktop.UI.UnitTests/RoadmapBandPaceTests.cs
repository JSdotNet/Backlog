using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.UI;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The reader's pace in the roadmap's heading: the one they type, three measured
/// from what they finished over the last two, four and eight weeks, and which of
/// them places an imported plan (ADR 0013, ruling 4).
/// </summary>
public sealed class RoadmapBandPaceTests : RoadmapBandHarness
{
    [Fact]
    public void The_pace_opens_at_one_point_a_day_typed_with_nothing_measured()
    {
        using var context = Context();
        var band = Paced(context);

        Assert.Equal("1", Manual(band).GetAttribute("value"));
        Assert.Equal("number", Manual(band).GetAttribute("type"));
        Assert.Equal("true", Option(band, "manual-option").GetAttribute("aria-pressed"));

        foreach (var stretch in new[] { "two-weeks", "four-weeks", "eight-weeks" })
        {
            var option = Option(band, stretch);
            Assert.True(option.HasAttribute("disabled"));
            Assert.Equal("—", Measured(band, stretch));
        }
    }

    [Fact]
    public void Each_stretch_shows_what_was_finished_in_it_per_day()
    {
        Finished.Add(new CompletedEffortDto(PaceToday, 14));

        using var context = Context();
        var band = Paced(context);

        Assert.Equal("1", Measured(band, "two-weeks"));
        Assert.Equal("0.5", Measured(band, "four-weeks"));
        Assert.Equal("0.25", Measured(band, "eight-weeks"));
        Assert.False(Option(band, "two-weeks").HasAttribute("disabled"));
    }

    [Fact]
    public void A_typed_pace_is_stored_straight_away()
    {
        using var context = Context();
        var band = Paced(context);

        Manual(band).Change("2.5");

        Assert.Equal(2.5m, PaceFile.StoryPointsPerDay);
        band.WaitForAssertion(() => Assert.Equal("2.5", Manual(band).GetAttribute("value")));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("quickly")]
    public void A_pace_the_roadmap_could_not_divide_by_says_so_and_changes_nothing(string refused)
    {
        _ = PaceFile.Set(4m);

        using var context = Context();
        var band = Paced(context);

        Manual(band).Change(refused);

        Assert.Equal(4m, PaceFile.StoryPointsPerDay);
        band.WaitForAssertion(() => Assert.Contains(
            "field__error",
            band.Find("[data-testid='roadmap-pace']").InnerHtml,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Choosing_a_measured_pace_is_kept_and_shown_pressed()
    {
        Finished.Add(new CompletedEffortDto(PaceToday, 14));

        using var context = Context();
        var band = Paced(context);

        Option(band, "four-weeks").Click();

        Assert.Equal(PaceSource.LastFourWeeks, PaceFile.Source);
        band.WaitForAssertion(() =>
        {
            Assert.Equal("true", Option(band, "four-weeks").GetAttribute("aria-pressed"));
            Assert.Equal("false", Option(band, "manual-option").GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public void A_chosen_pace_that_measured_nothing_says_the_typed_one_is_used()
    {
        _ = PaceFile.Choose(PaceSource.LastTwoWeeks);

        using var context = Context();
        var band = Paced(context);

        Assert.Contains(
            "your own pace is used",
            band.Find("[data-testid='roadmap-pace-fell-back']").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_pace_changed_elsewhere_is_shown_without_a_reload()
    {
        using var context = Context();
        var band = Paced(context);

        _ = PaceFile.Set(3m);

        band.WaitForAssertion(() => Assert.Equal("3", Manual(band).GetAttribute("value")));
    }

    [Fact]
    public void Finished_work_updates_the_measured_paces()
    {
        using var context = Context();
        var band = Paced(context);

        Finished.Add(new CompletedEffortDto(PaceToday, 28));
        WorkChanges.Raise();

        band.WaitForAssertion(() =>
            Assert.Equal("2", Measured(band, "two-weeks")));
    }

    private static IRenderedComponent<RoadmapBand> Paced(BunitContext context)
    {
        var band = context.Render<RoadmapBand>();
        band.WaitForElement("[data-testid='roadmap-pace-manual'] input");
        return band;
    }

    private static AngleSharp.Dom.IElement Manual(IRenderedComponent<RoadmapBand> band) =>
        band.Find("[data-testid='roadmap-pace-manual'] input");

    private static AngleSharp.Dom.IElement Option(IRenderedComponent<RoadmapBand> band, string key) =>
        band.Find($"[data-testid='roadmap-pace-{key}']");

    private static string Measured(IRenderedComponent<RoadmapBand> band, string key) =>
        band.Find($"[data-testid='roadmap-pace-{key}'] .roadmap-pace__option-value").TextContent.Trim();
}
