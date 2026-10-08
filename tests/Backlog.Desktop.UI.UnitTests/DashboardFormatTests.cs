using Backlog.Modules.Dashboard.UI.Parts;
using Backlog.UI.Components.Metrics;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The dashboard's own rounding and naming policy, asserted directly rather than only
/// through the parts that render it.
/// <para>
/// Worth its own file because the policy has three outcomes a reader must be able to tell
/// apart — a figure, a zero, and no figure at all — and two of them look alike until
/// something genuinely reads zero. The activity grid made that happen for the first time.
/// </para>
/// </summary>
public sealed class DashboardFormatTests
{
    /// <summary>
    /// The round-up-to-a-minute rule is about a session that ran: something happened, and
    /// "0m" would deny it. An hour of the grid nobody worked, and the waiting column of an
    /// assistant that cannot record one, are genuinely nothing — and rounding those up
    /// would invent the only activity they have.
    /// </summary>
    [Fact]
    public void A_zero_duration_reads_as_zero_rather_than_as_a_minute()
    {
        Assert.Equal("0m", DashboardFormat.Duration(TimeSpan.Zero));
    }

    /// <summary>The rule the zero above is carved out of, still intact: anything at all
    /// rounds up rather than down to nothing.</summary>
    [Fact]
    public void Anything_at_all_still_rounds_up_to_a_minute()
    {
        Assert.Equal("1m", DashboardFormat.Duration(TimeSpan.FromSeconds(4)));
    }

    /// <summary>Three outcomes, not two. No figure is an em dash; a figure of nothing is a
    /// zero; and a reader who could not tell those apart would read a missing measure as a
    /// quiet one.</summary>
    [Fact]
    public void Having_no_duration_is_still_a_different_statement_from_a_zero_one()
    {
        Assert.Equal("—", DashboardFormat.Duration(null));
        Assert.NotEqual(DashboardFormat.Duration(null), DashboardFormat.Duration(TimeSpan.Zero));
    }

    /// <summary>A count compares as the count it moved by, in either direction, and says
    /// which way up is good as the caller told it.</summary>
    [Fact]
    public void A_count_delta_is_the_difference()
    {
        var up = DashboardFormat.CountDelta(11m, 8m, DashboardFormat.PreviousWeeks(4));
        var down = DashboardFormat.CountDelta(5m, 9m, "previous 12 weeks", higherIsBetter: false);

        Assert.Equal(new MetricDelta(3m, MetricDeltaUnit.Absolute, "previous 4 weeks"), up);
        Assert.Equal(new MetricDelta(-4m, MetricDeltaUnit.Absolute, "previous 12 weeks", false), down);
    }

    /// <summary>A share is a fraction of the earlier figure, the shape the tile reads
    /// as a percentage — 0.25 is "up 25%".</summary>
    [Fact]
    public void A_share_delta_is_a_fraction_of_the_earlier_figure()
    {
        Assert.Equal(
            new MetricDelta(0.25m, MetricDeltaUnit.Percent, "previous 4 weeks"),
            DashboardFormat.ShareDelta(TimeSpan.FromHours(5), TimeSpan.FromHours(4), "previous 4 weeks"));
        Assert.Equal(
            new MetricDelta(-0.5m, MetricDeltaUnit.Percent, "same days last month", false),
            DashboardFormat.ShareDelta(4m, 8m, "same days last month", higherIsBetter: false));
    }

    /// <summary>No earlier figure is no delta, for both shapes; and a share of nothing is
    /// no delta either, because "up from nothing" is not a percentage. A count of
    /// nothing is still a count, so that one compares.</summary>
    [Fact]
    public void No_earlier_figure_and_a_share_of_nothing_are_no_delta()
    {
        Assert.Null(DashboardFormat.CountDelta(3m, null, "previous 4 weeks"));
        Assert.Null(DashboardFormat.ShareDelta(3m, null, "previous 4 weeks"));
        Assert.Null(DashboardFormat.ShareDelta(TimeSpan.FromHours(1), null, "previous 4 weeks"));
        Assert.Null(DashboardFormat.ShareDelta(3m, 0m, "previous 4 weeks"));
        Assert.Equal(3m, DashboardFormat.CountDelta(3m, 0m, "previous 4 weeks")!.Value);
    }

    /// <summary>Both halves: the weekday is what the question is about, and the date is
    /// what stops a fortnight-old screenshot reading as this week.</summary>
    [Fact]
    public void A_grid_row_is_named_by_its_weekday_and_its_date()
    {
        Assert.Equal("Wed 02", DashboardFormat.Day(new DateOnly(2026, 9, 2)));
    }

    /// <summary>Padded, because the grid matches its cells on this string: "7" and "07"
    /// would be two different columns.</summary>
    [Fact]
    public void A_grid_column_is_a_padded_hour()
    {
        Assert.Equal("07", DashboardFormat.Hour(7));
        Assert.Equal("23", DashboardFormat.Hour(23));
    }

    /// <summary>
    /// Invariant rather than the current culture, for the reason every other figure on
    /// this surface is: a heading that moved with the machine's language would make a
    /// screenshot untranslatable back to the data behind it, and every assertion that
    /// spells a day out would fail on one machine and nowhere else.
    /// </summary>
    [Fact]
    public void A_day_is_named_the_same_whatever_the_machines_language()
    {
        var original = Thread.CurrentThread.CurrentCulture;

        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("nl-NL");

            Assert.Equal("Wed 02", DashboardFormat.Day(new DateOnly(2026, 9, 2)));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }
}
