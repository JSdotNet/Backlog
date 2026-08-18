using System.Globalization;

namespace Backlog.UI.Components.Metrics;

/// <summary>
/// How a metric reads. A cost figure gets a formatter rather than a component:
/// a component whose whole body is one string earns a story and a test file for
/// no review value.
/// </summary>
/// <remarks>
/// Everything here formats with <see cref="CultureInfo.InvariantCulture"/>. Not
/// a style preference — a machine with a comma decimal separator would otherwise
/// render "12,35 USD" and every assertion that spells a number out would fail on
/// that machine and nowhere else, which is exactly how this kind of thing gets
/// found in CI instead of on a desk.
/// </remarks>
public static class MetricFormat
{
    /// <summary>The compacting steps, largest first.</summary>
    private static readonly (decimal Scale, string Suffix)[] Steps =
    [
        (1_000_000_000m, "B"),
        (1_000_000m, "M"),
        (1_000m, "K")
    ];

    /// <summary>Below this the exact figure still fits a tile, and "9,984
    /// sessions" reads fine where "10.0K" throws away the only digits anyone
    /// looks at.</summary>
    private const decimal CompactAbove = 10_000m;

    /// <summary>1,284 / 12.9K / 4.2M / 1.3B.</summary>
    public static string Compact(long value) => Compact((decimal)value);

    /// <summary>1,284 / 12.35 / 12.9K / 4.2M / 1.3B. A figure under the
    /// compacting threshold keeps its cents: a day's spend of 12.40 rounded to
    /// 12 is the whole number thrown away.</summary>
    public static string Compact(decimal value)
    {
        if (Math.Abs(value) >= CompactAbove) return Shorten(value);

        return value == decimal.Truncate(value)
            ? value.ToString("N0", CultureInfo.InvariantCulture)
            : value.ToString("N2", CultureInfo.InvariantCulture);
    }

    /// <summary>"12.35 USD". The ISO code, never a symbol: the amount arrives
    /// exactly as reported and nothing here knows it is dollars.
    /// <paramref name="decimals"/> defaults to 2 and goes higher for the
    /// fractions-of-a-cent figures per-model costs actually carry.</summary>
    public static string Money(MoneyAmount amount, int decimals = 2)
    {
        ArgumentNullException.ThrowIfNull(amount);

        var figure = amount.Amount.ToString("N" + Math.Max(0, decimals).ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture);

        return string.IsNullOrWhiteSpace(amount.Currency) ? figure : $"{figure} {amount.Currency}";
    }

    /// <summary>
    /// "68.4", "50", "91.2" — a point on a bounded scale, to at most one decimal
    /// with the trailing zero dropped.
    /// </summary>
    /// <remarks>
    /// Not <see cref="Compact"/>, which keeps two decimals for anything fractional
    /// because it was written for money: a day's spend of 12.40 must not round to 12.
    /// A score is not money. "91.20 out of 100" claims a precision the score does not
    /// have and reads like a currency figure with the code missing, and a score is the
    /// one number on a dashboard whose credibility is the whole point.
    /// </remarks>
    public static string Score(decimal value)
    {
        var rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);

        return rounded.ToString(
            rounded == decimal.Truncate(rounded) ? "0" : "0.#",
            CultureInfo.InvariantCulture);
    }

    /// <summary>"12.4%", "&lt;0.1%", "100%". Trailing zeros go: a share of exactly
    /// one is "100%", not "100.0%".</summary>
    public static string Percent(decimal fraction, int decimals = 1)
    {
        var places = Math.Clamp(decimals, 0, 6);
        var format = places == 0 ? "0" : "0." + new string('#', places);
        var percent = Math.Round(fraction * 100m, places, MidpointRounding.AwayFromZero);

        // A part that rounds away entirely still exists. Printing 0% for it says
        // the opposite of what the data says.
        if (percent == 0m && fraction != 0m)
        {
            var smallest = (decimal)Math.Pow(10, -places);
            var floorText = smallest.ToString(format, CultureInfo.InvariantCulture);

            return fraction > 0m ? $"<{floorText}%" : $">-{floorText}%";
        }

        return percent.ToString(format, CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>"up 12.4%", "down 3", "unchanged". Words, not arrows — the arrow
    /// beside this is decoration and is aria-hidden, so the direction has to
    /// survive without it.</summary>
    public static string Delta(MetricDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);

        if (delta.Value == 0m) return "unchanged";

        var direction = delta.Value > 0m ? "up" : "down";
        var magnitude = Math.Abs(delta.Value);

        var size = delta.Unit == MetricDeltaUnit.Percent
            ? Percent(magnitude)
            : Compact(magnitude);

        return $"{direction} {size}";
    }

    private static string Shorten(decimal value)
    {
        for (var index = 0; index < Steps.Length; index++)
        {
            var step = Steps[index];

            if (Math.Abs(value) < step.Scale) continue;

            var scaled = Math.Round(value / step.Scale, 1, MidpointRounding.AwayFromZero);

            // 999,950 rounds to 1000.0K, which is a thousand of the wrong unit.
            // Stepping up keeps the figure in the unit the reader expects.
            if (Math.Abs(scaled) >= 1000m && index > 0)
            {
                var bigger = Steps[index - 1];

                return Math.Round(value / bigger.Scale, 1, MidpointRounding.AwayFromZero)
                    .ToString("0.0", CultureInfo.InvariantCulture) + bigger.Suffix;
            }

            return scaled.ToString("0.0", CultureInfo.InvariantCulture) + step.Suffix;
        }

        return value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
