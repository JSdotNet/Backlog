using Backlog.Modules.Dashboard.UI.Parts;

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
