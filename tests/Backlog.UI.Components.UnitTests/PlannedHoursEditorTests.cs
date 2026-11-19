using System.Globalization;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The detail panel's planned hours for one task: a line a day to change or remove,
/// and a line to add another day.
/// </summary>
public sealed class PlannedHoursEditorTests : IDisposable
{
    private static readonly DateOnly Monday = new(2026, 10, 12);

    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public PlannedHoursEditorTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private static IRenderedComponent<PlannedHoursEditor> Render(BunitContext context, IReadOnlyList<PlannedHoursDay> blocks, List<PlannedHoursDayChange> changes) =>
        context.Render<PlannedHoursEditor>(parameters => parameters
            .Add(c => c.Blocks, blocks)
            .Add(c => c.DefaultDay, Monday)
            .Add(c => c.TestId, "ph")
            .Add(c => c.OnChanged, (PlannedHoursDayChange change) => changes.Add(change)));

    [Fact]
    public void Each_day_is_a_line_with_its_hours()
    {
        using var context = new BunitContext();

        var editor = Render(context, [new(Monday, 3m), new(Monday.AddDays(1), 1.5m)], []);

        Assert.Equal(["2026-10-12", "2026-10-13"], editor.FindAll("[data-testid='ph-row']").Select(row => row.GetAttribute("data-day")));
        Assert.Equal("1.5", editor.Find("[data-testid='ph-hours-2026-10-13'] input").GetAttribute("value"));
    }

    [Fact]
    public void A_changed_figure_is_reported_on_change()
    {
        using var context = new BunitContext();
        var changes = new List<PlannedHoursDayChange>();
        var editor = Render(context, [new(Monday, 3m)], changes);

        editor.Find("[data-testid='ph-hours-2026-10-12'] input").Change("4");

        Assert.Equal([new PlannedHoursDayChange(Monday, 4m)], changes);
    }

    [Fact]
    public void A_figure_that_is_no_number_of_hours_is_said_and_not_reported()
    {
        using var context = new BunitContext();
        var changes = new List<PlannedHoursDayChange>();
        var editor = Render(context, [new(Monday, 3m)], changes);

        editor.Find("[data-testid='ph-hours-2026-10-12'] input").Change("0");

        Assert.Empty(changes);
        Assert.Contains("more than 0", editor.Find("[data-testid='ph-error']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>Entry 22's finding: a refused figure used to stay in the field as typed,
    /// reading as kept while the sentence under it said it was not.</summary>
    [Fact]
    public void A_refused_figure_goes_back_to_the_stored_one_and_the_refusal_still_shows()
    {
        using var context = new BunitContext();
        var changes = new List<PlannedHoursDayChange>();
        var editor = Render(context, [new(Monday, 3m)], changes);

        editor.Find("[data-testid='ph-hours-2026-10-12'] input").Input("30");
        Assert.Equal("30", editor.Find("[data-testid='ph-hours-2026-10-12'] input").GetAttribute("value"));

        editor.Find("[data-testid='ph-hours-2026-10-12'] input").Change("30");

        Assert.Empty(changes);
        Assert.Equal("3", editor.Find("[data-testid='ph-hours-2026-10-12'] input").GetAttribute("value"));
        Assert.Contains("at most 24", editor.Find("[data-testid='ph-error']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_figure_that_rounds_to_the_stored_one_shows_the_stored_one()
    {
        using var context = new BunitContext();
        var changes = new List<PlannedHoursDayChange>();
        var editor = Render(context, [new(Monday, 3m)], changes);

        editor.Find("[data-testid='ph-hours-2026-10-12'] input").Input("3.1");
        editor.Find("[data-testid='ph-hours-2026-10-12'] input").Change("3.1");

        Assert.Empty(changes);
        Assert.Equal("3", editor.Find("[data-testid='ph-hours-2026-10-12'] input").GetAttribute("value"));
        Assert.Empty(editor.FindAll("[data-testid='ph-error']"));
    }

    [Fact]
    public void Taking_a_suggested_figure_for_a_day_commits_it_at_once()
    {
        using var context = new BunitContext();
        var changes = new List<PlannedHoursDayChange>();
        var editor = Render(context, [new(Monday, 3m)], changes);

        editor.Find("[data-testid='ph-hours-2026-10-12'] input").Click();
        Assert.Equal(
            ["0.5h", "1h", "2h", "3h", "4h", "6h", "8h"],
            editor.FindAll("[data-testid='ph-hours-2026-10-12'] [role='option']").Select(option => option.TextContent));

        editor.Find("[data-testid='ph-hours-2026-10-12-option-0']").Click();

        Assert.Equal([new PlannedHoursDayChange(Monday, 0.5m)], changes);
    }

    [Fact]
    public void A_typed_figure_for_a_day_commits_on_enter_and_once()
    {
        using var context = new BunitContext();
        var changes = new List<PlannedHoursDayChange>();
        var editor = Render(context, [new(Monday, 3m)], changes);

        editor.Find("[data-testid='ph-hours-2026-10-12'] input").Input("2.5");
        Assert.Empty(changes);

        editor.Find("[data-testid='ph-hours-2026-10-12'] input").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        // The change a browser raises after Enter carries the same figure.
        editor.Find("[data-testid='ph-hours-2026-10-12'] input").Change("2.5");

        Assert.Equal([new PlannedHoursDayChange(Monday, 2.5m)], changes);
    }

    /// <summary>The add line's day is picked beside its figure, so a suggestion only
    /// fills the figure and "Add hours" commits both.</summary>
    [Fact]
    public void Taking_a_suggestion_on_the_add_line_waits_for_add_hours()
    {
        using var context = new BunitContext();
        var changes = new List<PlannedHoursDayChange>();
        var editor = Render(context, [], changes);

        editor.Find("[data-testid='ph-add-hours'] input").Click();
        editor.Find("[data-testid='ph-add-hours-option-3']").Click();

        Assert.Empty(changes);
        Assert.Equal("3", editor.Find("[data-testid='ph-add-hours'] input").GetAttribute("value"));

        editor.Find("[data-testid='ph-add']").Click();
        Assert.Equal([new PlannedHoursDayChange(Monday, 3m)], changes);
    }

    [Fact]
    public void Remove_reports_the_day_with_no_hours()
    {
        using var context = new BunitContext();
        var changes = new List<PlannedHoursDayChange>();
        var editor = Render(context, [new(Monday, 3m)], changes);

        editor.Find("[data-testid='ph-remove-2026-10-12']").Click();

        Assert.Equal([new PlannedHoursDayChange(Monday, null)], changes);
    }

    [Fact]
    public void Add_reports_the_day_picked_and_its_hours()
    {
        using var context = new BunitContext();
        var changes = new List<PlannedHoursDayChange>();
        var editor = Render(context, [], changes);

        Assert.Equal("2026-10-12", editor.Find("[data-testid='ph-add-day'] input").GetAttribute("value"));
        editor.Find("[data-testid='ph-add-day'] input").Input("2026-10-14");
        editor.Find("[data-testid='ph-add-hours'] input").Input("2");
        editor.Find("[data-testid='ph-add']").Click();

        Assert.Equal([new PlannedHoursDayChange(new DateOnly(2026, 10, 14), 2m)], changes);
    }
}

/// <summary>The hours arithmetic and wording, apart from the markup.</summary>
public sealed class CalendarHoursTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    /// <summary>A number field hands its value over with a point whatever the culture, so
    /// "1.5" is one and a half in a culture that groups thousands with a point too.</summary>
    [Theory]
    [InlineData("nl-NL", "1.5", 1.5)]
    [InlineData("nl-NL", "2,5", 2.5)]
    [InlineData("en-US", "2.5h", 2.5)]
    [InlineData("en-US", "24", 24)]
    public void A_typed_figure_is_read_with_a_point_first_and_never_grouped(string culture, string typed, double hours)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

        Assert.Equal((decimal)hours, CalendarHours.Parse(typed));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("24.25")]
    [InlineData("1,000")]
    [InlineData("0.1")]
    [InlineData("")]
    public void A_figure_that_is_no_number_of_hours_reads_as_none(string typed)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

        Assert.Null(CalendarHours.Parse(typed));
    }

    [Fact]
    public void The_field_starts_from_an_invariant_figure()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nl-NL");

        Assert.Equal("2.5", CalendarHours.Edit(2.5m));
    }
}
