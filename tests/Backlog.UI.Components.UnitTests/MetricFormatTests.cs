using System.Globalization;

namespace Backlog.UI.Components.UnitTests;

public sealed class MetricFormatCompactTests
{
    [Theory]
    [InlineData(0L, "0")]
    [InlineData(1_284L, "1,284")]
    // Under ten thousand the exact figure still fits a tile, and "10.0K" throws
    // away the only digits anyone reads.
    [InlineData(9_999L, "9,999")]
    [InlineData(10_000L, "10.0K")]
    [InlineData(412_400_000L, "412.4M")]
    [InlineData(1_300_000_000L, "1.3B")]
    [InlineData(-42_000L, "-42.0K")]
    public void A_count_reads_at_the_scale_it_is_read_at(long value, string expected) =>
        Assert.Equal(expected, MetricFormat.Compact(value));

    [Fact]
    public void A_days_spend_keeps_its_cents()
    {
        // 12.40 rounded to 12 is the whole number thrown away.
        Assert.Equal("12.40", MetricFormat.Compact(12.40m));
        Assert.Equal("12", MetricFormat.Compact(12m));
    }

    [Fact]
    public void A_figure_that_would_round_past_its_unit_steps_up()
    {
        // 999,950 / 1000 rounds to 1000.0K, which is a thousand of the wrong unit.
        Assert.Equal("1.0M", MetricFormat.Compact(999_950L));
        Assert.Equal("1.0B", MetricFormat.Compact(999_950_000L));
    }

    [Fact]
    public void The_largest_step_has_nowhere_left_to_go()
    {
        // Past billions there is no bigger suffix, so the figure keeps growing in
        // the unit it has rather than stepping off the end of the table.
        Assert.Equal("1400.0B", MetricFormat.Compact(1_400_000_000_000L));
    }
}

public sealed class MetricFormatMoneyTests
{
    [Fact]
    public void The_code_travels_with_the_number_and_is_never_a_symbol()
    {
        // A dashboard that prints a dollar sign over a euro figure is worse than
        // one that prints the code.
        Assert.Equal("12.35 USD", MetricFormat.Money(new MoneyAmount(12.3456m, "USD")));
        Assert.Equal("12.35 EUR", MetricFormat.Money(new MoneyAmount(12.3456m, "EUR")));
    }

    [Fact]
    public void Per_model_costs_need_more_than_two_decimals()
    {
        // Two decimals turns a column of real figures into a column of 0.00.
        Assert.Equal("0.00 USD", MetricFormat.Money(new MoneyAmount(0.0043m, "USD")));
        Assert.Equal("0.0043 USD", MetricFormat.Money(new MoneyAmount(0.0043m, "USD"), 4));
    }

    [Fact]
    public void A_missing_currency_leaves_the_figure_alone()
    {
        Assert.Equal("12.35", MetricFormat.Money(new MoneyAmount(12.3456m, "")));
        Assert.Equal("12.35", MetricFormat.Money(new MoneyAmount(12.3456m, "   ")));
    }

    [Fact]
    public void A_comma_decimal_machine_formats_the_same_as_every_other()
    {
        // The reason everything here is invariant: this assertion would otherwise
        // pass in CI and fail on a Dutch desk, or the reverse.
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("nl-NL");

            Assert.Equal("12.35 USD", MetricFormat.Money(new MoneyAmount(12.3456m, "USD")));
            Assert.Equal("412.4M", MetricFormat.Compact(412_400_000L));
            Assert.Equal("12.4%", MetricFormat.Percent(0.1236m));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}

public sealed class MetricFormatPercentTests
{
    [Theory]
    [InlineData(0.1236, "12.4%")]
    [InlineData(0.5, "50%")]
    // Trailing zeros go: a share of exactly one is "100%", not "100.0%".
    [InlineData(1.0, "100%")]
    [InlineData(0.0, "0%")]
    public void A_share_reads_to_one_decimal_and_no_further(double fraction, string expected) =>
        Assert.Equal(expected, MetricFormat.Percent((decimal)fraction));

    [Fact]
    public void A_part_that_rounds_away_entirely_still_exists()
    {
        // Printing 0% for it says the opposite of what the data says.
        Assert.Equal("<0.1%", MetricFormat.Percent(0.00004m));
        Assert.Equal(">-0.1%", MetricFormat.Percent(-0.00004m));
    }

    [Fact]
    public void Asking_for_no_decimals_gets_a_whole_number()
    {
        Assert.Equal("12%", MetricFormat.Percent(0.1236m, 0));
        Assert.Equal("<1%", MetricFormat.Percent(0.004m, 0));
    }
}

public sealed class MetricFormatDeltaTests
{
    [Fact]
    public void Direction_is_a_word_because_the_arrow_is_decoration()
    {
        // The arrow beside this in the tile is aria-hidden, so the direction has
        // to survive without it.
        Assert.Equal("up 12.4%", MetricFormat.Delta(new MetricDelta(0.124m, MetricDeltaUnit.Percent, "last week")));
        Assert.Equal("down 12.4%", MetricFormat.Delta(new MetricDelta(-0.124m, MetricDeltaUnit.Percent, "last week")));
    }

    [Fact]
    public void An_absolute_delta_is_a_count_not_a_fraction()
    {
        Assert.Equal("up 17", MetricFormat.Delta(new MetricDelta(17m, MetricDeltaUnit.Absolute, "last week")));
        Assert.Equal("down 3", MetricFormat.Delta(new MetricDelta(-3m, MetricDeltaUnit.Absolute, "last week")));
    }

    [Fact]
    public void No_movement_is_a_word_rather_than_up_zero()
    {
        Assert.Equal("unchanged", MetricFormat.Delta(new MetricDelta(0m, MetricDeltaUnit.Percent, "last week")));
    }

    [Fact]
    public void Whether_up_is_good_changes_nothing_about_how_it_reads()
    {
        // HigherIsBetter drives the tile's modifier, not the sentence: spend that
        // rose still "went up".
        var worse = new MetricDelta(0.224m, MetricDeltaUnit.Percent, "last week", HigherIsBetter: false);
        var better = new MetricDelta(0.224m, MetricDeltaUnit.Percent, "last week");

        Assert.Equal(MetricFormat.Delta(better), MetricFormat.Delta(worse));
    }
}

public sealed class MetricTokenUsageTests
{
    [Fact]
    public void A_money_amount_keeps_the_currency_it_arrived_with()
    {
        // Nothing in the group rescales or converts: the whole point of carrying
        // the code is that no layer gets to assume dollars.
        var amount = new MoneyAmount(487.20m, "USD");

        Assert.Equal(487.20m, amount.Amount);
        Assert.Equal("USD", amount.Currency);
    }
}

public sealed class MetricFormatScoreTests
{
    [Theory]
    [InlineData(68.4, "68.4")]
    [InlineData(50.0, "50")]
    [InlineData(91.2, "91.2")]
    [InlineData(100.0, "100")]
    [InlineData(0.0, "0")]
    [InlineData(37.55, "37.6")]
    public void A_score_reads_to_at_most_one_decimal(double value, string expected) =>
        Assert.Equal(expected, MetricFormat.Score((decimal)value));

    [Fact]
    public void A_score_is_not_money()
    {
        // Compact keeps two decimals for anything fractional because it was written
        // for a day's spend, where 12.40 must not round to 12. Run a score through it
        // and 91.2 becomes "91.20", which claims a precision the score does not have
        // and reads like a currency figure with the code missing.
        Assert.Equal("91.20", MetricFormat.Compact(91.2m));
        Assert.Equal("91.2", MetricFormat.Score(91.2m));
    }

    [Fact]
    public void A_trailing_zero_is_not_precision()
    {
        // A score of exactly fifty is "50", the way a share of exactly one is "100%".
        Assert.Equal("50", MetricFormat.Score(50.00m));
        Assert.Equal("50", MetricFormat.Score(49.98m));
    }
}
