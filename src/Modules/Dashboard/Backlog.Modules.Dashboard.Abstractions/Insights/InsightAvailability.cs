namespace Backlog.Modules.Dashboard.Abstractions.Insights;

/// <summary>
/// Whether a source can answer at all, and — when it cannot — why, in words fit
/// to put on screen.
/// <para>
/// Unavailable is not an error. Every provider behind this dashboard answers an
/// availability question before it answers a data question, and "no credential
/// is configured" is the answer most of them give most of the time. Carrying the
/// reason as data is what lets a part explain itself instead of showing a zero
/// that reads as "you did nothing this month".
/// </para>
/// </summary>
public sealed record InsightAvailability(bool IsAvailable, string Reason)
{
    public static InsightAvailability Available { get; } = new(true, string.Empty);

    public static InsightAvailability Unavailable(string reason) => new(false, reason);
}

/// <summary>
/// An amount exactly as the provider reported it, with the currency it named.
/// <para>
/// Never converted and never rescaled. Anthropic reports an estimate in the
/// currency it bills in and GitHub reports United States dollars; a dashboard
/// that prints one symbol over the other's figure is worse than one that prints
/// the code. The metrics library has a type of the same shape, and this one is
/// deliberately separate — a module contract that borrowed it would drag a UI
/// library into every consumer of this module.
/// </para>
/// </summary>
public sealed record DashboardMoney(decimal Amount, string Currency)
{
    public static DashboardMoney Zero(string currency) => new(0m, currency);

    /// <summary>
    /// Adds two amounts of the same currency. Throws on a mismatch rather than
    /// picking one, because there is no exchange rate in this product and a
    /// silently wrong total is the failure mode worth being loud about.
    /// </summary>
    public static DashboardMoney operator +(DashboardMoney left, DashboardMoney right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (!string.Equals(left.Currency, right.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cannot add {left.Currency} to {right.Currency}: Backlog holds no exchange rate, so mixed "
                + "currencies are reported side by side rather than summed.");
        }

        return new DashboardMoney(left.Amount + right.Amount, left.Currency);
    }
}

/// <summary>One bucket of a series — a week, a day, or a month — and its value.</summary>
public sealed record InsightPoint(string Label, decimal Value);

/// <summary>One named series over the same buckets as its neighbours.</summary>
public sealed record InsightSeries(string Name, IReadOnlyList<InsightPoint> Points, string? Detail = null);

/// <summary>One row of a breakdown — a model, a provider — with whatever
/// measures the source reported. A null is "not reported", which is a different
/// fact from zero and is displayed differently.</summary>
public sealed record InsightRow(string Name, long? Tokens = null, DashboardMoney? Cost = null, string? Detail = null);
