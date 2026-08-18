namespace Backlog.UI.Components.Metrics;

/// <summary>
/// How a productivity score is worked out from its inputs.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a static function of its arguments and nothing else. A score that
/// depends on hidden state cannot be reproduced, and a score nobody can reproduce
/// is a number people learn to ignore: given the same components this returns the
/// same figure, on any machine, in any order.
/// </para>
/// <para>
/// Order-independence is the reason the weighted sum is normalised by the total
/// weight rather than assuming the weights add to one. A caller adding a fifth
/// input should not have to rebalance the other four to keep the scale, and a
/// caller who sets every weight to 1 should get a plain average.
/// </para>
/// </remarks>
public static class MetricScoring
{
    /// <summary>The scale every score is on. Not a percentage of anything — 0 is
    /// "none of the inputs moved" and 100 is "every input at full marks".</summary>
    public const decimal MaxScore = 100m;

    /// <summary>
    /// The weighted, normalised score for these inputs, 0..100. An empty list or
    /// weights that add to nothing scores zero rather than dividing by it.
    /// </summary>
    public static decimal Score(IReadOnlyList<MetricScoreComponent>? components)
    {
        if (components is null || components.Count == 0) return 0m;

        var totalWeight = components.Sum(component => component.Weight);

        if (totalWeight <= 0m) return 0m;

        var weighted = components.Sum(component => component.Normalized * component.Weight);

        return Math.Round(weighted / totalWeight * MaxScore, 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The band a score falls in, or null when no band claims it. Bands are matched
    /// highest floor first, so a caller may list them in any order and does not have
    /// to write ceilings that then have to agree with the next band's floor.
    /// </summary>
    public static MetricBand? BandFor(decimal score, IReadOnlyList<MetricBand>? bands) =>
        bands is null || bands.Count == 0
            ? null
            : bands.Where(band => score >= band.Floor)
                .OrderByDescending(band => band.Floor)
                .FirstOrDefault();

    /// <summary>
    /// The ramp step 1..<paramref name="steps"/> a value earns against
    /// <paramref name="max"/>, or 0 for nothing at all.
    /// </summary>
    /// <remarks>
    /// The one place the ordinal scale of a single-hue chart is decided, shared by
    /// the heatmap and anything else that has to turn a figure into a shade. Zero is
    /// its own step — the track — because "no activity" and "a little activity" are
    /// different facts and the palest shade should mean the second one.
    /// </remarks>
    public static int RampStep(decimal value, decimal max, int steps = 4)
    {
        if (steps <= 0 || max <= 0m || value <= 0m) return 0;

        var share = Math.Clamp(value / max, 0m, 1m);
        var step = (int)Math.Ceiling(share * steps);

        return Math.Clamp(step, 1, steps);
    }

    /// <summary>
    /// The largest value across every series, which is the only y-max that makes a
    /// set of small multiples comparable.
    /// </summary>
    /// <remarks>
    /// Letting each panel scale to its own peak is the single most common way a
    /// trellis lies: every panel then looks equally busy, and a repository with a
    /// tenth of the activity of its neighbour draws the same mountain.
    /// </remarks>
    public static decimal SharedMax(IReadOnlyList<MetricSeries>? series) =>
        series is null || series.Count == 0
            ? 0m
            : series.SelectMany(one => one.Points).Aggregate(0m, (max, point) => Math.Max(max, point.Value));
}
