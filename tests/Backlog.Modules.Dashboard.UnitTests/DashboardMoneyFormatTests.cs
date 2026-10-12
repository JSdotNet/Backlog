using Backlog.Modules.Dashboard.Abstractions.Insights;

namespace Backlog.Modules.Dashboard.UnitTests;

/// <summary>
/// One way to write an amount on the dashboard. Verify read "€23.28" in the Cost
/// headline and "23.28 EUR" on the card under it; every surface now writes it the way
/// the board does (AC4, AC34): the currency's sign before it where it has a common one,
/// its code after it otherwise, whole from a hundred up or when there are no cents.
/// </summary>
public sealed class DashboardMoneyFormatTests
{
    [Theory]
    [InlineData(23.28, "EUR", "€23.28")]
    [InlineData(212.4, "EUR", "€212")]
    [InlineData(50, "EUR", "€50")]
    [InlineData(1234.5, "USD", "$1,235")]
    [InlineData(0.42, "GBP", "£0.42")]
    [InlineData(12.4, "CHF", "12.40 CHF")]
    [InlineData(0, "eur", "€0")]
    public void An_amount_is_written_as_the_board_writes_it(decimal amount, string currency, string expected)
    {
        Assert.Equal(expected, new DashboardMoney(amount, currency).Format());
        Assert.Equal(expected, DashboardMoney.Format(amount, currency));
    }
}
