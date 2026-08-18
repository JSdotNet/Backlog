namespace Backlog.UI.Components.Metrics;

/// <summary>One point on a series. Decimal, not double: a cost figure arrives as
/// a decimal and must reach the axis label without a round trip through binary
/// floating point. Geometry converts to double at the last moment.</summary>
public sealed record MetricPoint(string Label, decimal Value);

/// <summary>An amount exactly as the provider reported it. The currency travels
/// with the number and is never assumed, never rescaled, never converted — a
/// usage API reports whatever it reports, and a dashboard that prints a dollar
/// sign over a euro figure is worse than one that prints the code.</summary>
public sealed record MoneyAmount(decimal Amount, string Currency);

/// <summary>One segment of a part-to-whole bar. Order is meaning: parts render in
/// the order given and take ramp steps 1..4 in that order, so the caller decides
/// the ordinal scale — for AI tokens, unit price, cheapest first.</summary>
public sealed record MetricPart(string Label, decimal Value);

/// <summary>Change against a named earlier period.
/// <para><c>Value</c> is a fraction when <c>Unit</c> is
/// <see cref="MetricDeltaUnit.Percent"/> — 0.124 is "up 12.4%" — and a raw count
/// when it is <see cref="MetricDeltaUnit.Absolute"/>.</para>
/// <para><c>HigherIsBetter</c> belongs to the caller, because the answer differs
/// per metric: more sessions is good, more spend is not. It drives the wording
/// and the optional surface, never a text colour — the product has no strong
/// semantic text palette to draw one from.</para></summary>
public sealed record MetricDelta(
    decimal Value,
    MetricDeltaUnit Unit,
    string ComparedTo,
    bool HigherIsBetter = true);

/// <summary>One row of a breakdown: a model, an actor, a workspace, a seat.
/// <para>Every measure is nullable because the providers disagree about what they
/// report. A coding-assistant usage API gives tokens and an estimated cost per
/// model; a seat API gives a login, an editor and a last-activity date and no
/// figures at all. A null renders as an em dash, not a zero — "not reported" and
/// "zero" are different facts about the world.</para></summary>
public sealed record MetricRow(
    string Name,
    long? Tokens = null,
    MoneyAmount? Cost = null,
    string? Detail = null);

/// <summary>Which column a breakdown's share bars are a share of.</summary>
public enum MetricShareOf
{
    Tokens,
    Cost
}

/// <summary>Whether a delta is a proportion of the earlier figure or a count.</summary>
public enum MetricDeltaUnit
{
    Percent,
    Absolute
}

/// <summary>Why a metric is not showing a number.
/// <para><see cref="Unavailable"/> is not an error. A usage client answers an
/// availability question before it answers a data question, and "no credential is
/// configured" is the answer this UI gets most often. It is a calm, explained,
/// permanent-until-you-act state — the same posture design-principles.md requires
/// of offline.</para></summary>
public enum MetricStatusKind
{
    Ready,
    Loading,
    Empty,
    Unavailable
}

/// <summary>One named series over the same buckets as its neighbours — a
/// repository, a team, a channel.
/// <para>The name is what identifies it, not a colour. The palette carries one
/// saturated hue, so every component that draws more than one of these
/// distinguishes them by position or by label; a second hue would be the second
/// semantic palette components.css rules out.</para></summary>
public sealed record MetricSeries(string Name, IReadOnlyList<MetricPoint> Points, string? Detail = null)
{
    /// <summary>The most recent bucket, which is the figure a reader looks for
    /// first. Null for a series with no buckets at all.</summary>
    public MetricPoint? Latest => Points.Count == 0 ? null : Points[^1];

    /// <summary>Change from the first bucket to the last, as a fraction of the
    /// first. Null when there is nothing to compare against, and null rather than
    /// infinity when the series started at zero — "up from nothing" is not a
    /// percentage.</summary>
    public decimal? Change
    {
        get
        {
            if (Points.Count < 2) return null;

            var first = Points[0].Value;

            return first == 0m ? null : (Points[^1].Value - first) / first;
        }
    }
}

/// <summary>
/// One input to a score, and how much of the score it is allowed to be worth.
/// </summary>
/// <remarks>
/// <para><c>Max</c> is what counts as full marks for this input, so the component
/// normalises to 0..1 before the weight applies. Without it a score would be
/// dominated by whichever input happens to be counted in the largest units —
/// lines of code would bury pull requests every time.</para>
/// <para>A score is only ever as trustworthy as its inputs are visible, which is
/// why this type exists at all rather than a bare number: <c>MetricScore</c> shows
/// the composition, so a reader who disagrees with the score can see which weight
/// to argue with.</para>
/// </remarks>
public sealed record MetricScoreComponent(string Label, decimal Value, decimal Max, decimal Weight = 1m)
{
    /// <summary>This input as 0..1, clamped: an input past full marks does not earn
    /// extra, or one runaway week would carry a quarter.</summary>
    public decimal Normalized => Max <= 0m ? 0m : Math.Clamp(Value / Max, 0m, 1m);

    /// <summary>What this input contributes to the final score, in points.</summary>
    public decimal Contribution(decimal totalWeight) =>
        totalWeight <= 0m ? 0m : Normalized * Weight / totalWeight * 100m;
}

/// <summary>A named region of the score scale — "Strong" from 75 up. Bands are
/// what turn a bare 68 into something a reader can act on.</summary>
public sealed record MetricBand(string Name, decimal Floor);
