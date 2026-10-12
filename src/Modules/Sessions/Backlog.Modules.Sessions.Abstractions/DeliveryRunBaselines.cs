using System.Globalization;

namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>How a cost reads against its usual: the four ways a ratio to the median is
/// drawn.</summary>
public enum CostBand
{
    /// <summary>At 0.8 times the median or below — drawn green.</summary>
    Lower,

    /// <summary>Within 5% of the median either side — read as "typical".</summary>
    Typical,

    /// <summary>Anywhere else short of 1.5 times — the ratio, drawn plain.</summary>
    Plain,

    /// <summary>From 1.5 times the median up — highlighted.</summary>
    Higher
}

/// <summary>
/// A cost against the median of the same thing over earlier runs of the same flow.
/// </summary>
/// <param name="Ratio">The cost divided by the median.</param>
/// <param name="MedianUsdMicros">The median, in micro-dollars.</param>
/// <param name="Runs">How many earlier runs the median was taken over — 30 at most,
/// <see cref="DeliveryRunBaselines.MinimumRuns"/> at least.</param>
public sealed record CostAgainstUsual(double Ratio, decimal MedianUsdMicros, int Runs)
{
    /// <summary>Which of the four ways the ratio is drawn.</summary>
    public CostBand Band => DeliveryRunBaselines.Band(Ratio);

    /// <summary>"typical" within 5% of the median, else the ratio as "2.4×".</summary>
    public string Label => Band is CostBand.Typical ? "typical" : DeliveryRunBaselines.RatioLabel(Ratio);

    /// <summary>The comparison in words, for a tooltip: the ratio and what it was taken
    /// over.</summary>
    public string Title =>
        $"{DeliveryRunBaselines.RatioLabel(Ratio)} the median of {new CostFigure((long)Math.Round(MedianUsdMicros, MidpointRounding.AwayFromZero), CostSource.Reported).Label} over the last {(Runs == 1 ? "run" : $"{Runs} runs")} of this flow that finished";
}

/// <summary>
/// One stage's cost read against the run and against its usual.
/// </summary>
/// <param name="Cost">What the stage cost.</param>
/// <param name="Share">Its share of the run's cost, 0 to 1, or null where the run's
/// cost is not known.</param>
/// <param name="Usual">Its ratio to the stage's median, or null until the stage
/// finishes, and while fewer than <see cref="DeliveryRunBaselines.MinimumRuns"/> earlier
/// runs priced the stage.</param>
public sealed record StageCostAgainstUsual(CostFigure Cost, double? Share, CostAgainstUsual? Usual);

/// <summary>
/// What the run's insight line says: its ratio to the flow's median, and the stage
/// that explains most of the difference.
/// </summary>
/// <param name="Usual">The run's cost against the median of the flow's earlier runs.</param>
/// <param name="Stage">The stage whose cost moved furthest from its own median in the
/// direction the run moved, or null where the run reads as typical or no stage had a
/// median.</param>
/// <param name="StageUsual">That stage's comparison.</param>
/// <param name="StageDifferenceUsdMicros">That stage's cost less its median.</param>
/// <param name="ReEntries">How many times the stage was entered again after it first
/// finished — the implement–review loop and every revise round — from its done count.
/// Review rounds join it once the run records them.</param>
public sealed record DeliveryRunCostInsight(
    string SkillId,
    CostAgainstUsual Usual,
    string? Stage,
    CostAgainstUsual? StageUsual,
    decimal StageDifferenceUsdMicros,
    int ReEntries)
{
    /// <summary>The line, such as "2.1× the usual flow-code run · Implement explains most
    /// of it: $3.40 over its usual, entered again twice".</summary>
    public string Text
    {
        get
        {
            var head = Usual.Band is CostBand.Typical
                ? $"About the usual {SkillId} run ({DeliveryRunBaselines.RatioLabel(Usual.Ratio)} its median)"
                : $"{DeliveryRunBaselines.RatioLabel(Usual.Ratio)} the usual {SkillId} run";

            if (Stage is null) return head;

            var over = StageDifferenceUsdMicros >= 0 ? "over" : "under";
            var amount = new CostFigure((long)Math.Round(Math.Abs(StageDifferenceUsdMicros), MidpointRounding.AwayFromZero), CostSource.Reported).Label;
            var loop = ReEntries switch
            {
                0 => "",
                1 => ", entered again once",
                2 => ", entered again twice",
                var times => $", entered again {times} times"
            };

            return $"{head} · {Stage} explains most of it: {amount} {over} its usual{loop}";
        }
    }
}

/// <summary>
/// A run's costs read against its usual — per stage and as a whole.
/// </summary>
/// <param name="Total">The run's cost, the sum of its stages', or null where no stage
/// was priced.</param>
/// <param name="Stages">One per stage in run order, null where the stage was not
/// priced.</param>
/// <param name="Insight">The insight line, or null while the run is under way, or where
/// fewer than <see cref="DeliveryRunBaselines.MinimumRuns"/> earlier runs were priced.</param>
public sealed record DeliveryRunCostComparison(
    CostFigure? Total,
    IReadOnlyList<StageCostAgainstUsual?> Stages,
    DeliveryRunCostInsight? Insight)
{
    /// <summary>A run nothing could price.</summary>
    public static DeliveryRunCostComparison None(int stages) => new(null, [.. Enumerable.Repeat<StageCostAgainstUsual?>(null, stages)], null);
}

/// <summary>An earlier run with what each of its stages cost.</summary>
public sealed record PricedDeliveryRun(DeliveryRun Run, IReadOnlyList<DeliveryRunStageCost> Costs);

/// <summary>
/// Reads a run's cost against its usual: each stage against the median of that stage
/// over the last <see cref="Window"/> finished runs of the same flow, and the run against
/// the median of those runs' totals.
/// <list type="bullet">
/// <item><b>Earlier runs.</b> Runs of the same skill id that finished — done — and started
/// before this one, latest first, at most <see cref="Window"/>.</item>
/// <item><b>Per stage.</b> A stage is matched by name. A stage that has not finished,
/// or one fewer than <see cref="MinimumRuns"/> of those runs finished and priced, is
/// compared with nothing.</item>
/// <item><b>Bands.</b> 0.8× or below is <see cref="CostBand.Lower"/>; within 5% of the
/// median <see cref="CostBand.Typical"/>; 1.5× and up <see cref="CostBand.Higher"/>;
/// the rest <see cref="CostBand.Plain"/>.</item>
/// <item><b>Insight.</b> Once the run has left in-progress: its total against the
/// median total of the earlier runs that finished and priced every stage this run
/// priced, over those same stages — like for like — and the stage whose cost less its own median is largest in the
/// direction the run moved — above when the run cost more, below when it cost less.</item>
/// </list>
/// The median rather than the mean, because one runaway run would otherwise move the
/// baseline for the next thirty.
/// </summary>
public static class DeliveryRunBaselines
{
    /// <summary>How many earlier runs a median is taken over, at most.</summary>
    public const int Window = 30;

    /// <summary>How many earlier runs a median needs before anything is compared.</summary>
    public const int MinimumRuns = 5;

    /// <summary>How far either side of the median still reads as typical.</summary>
    public const double TypicalTolerance = 0.05;

    /// <summary>The ratio from which a cost is highlighted.</summary>
    public const double HigherFrom = 1.5;

    /// <summary>The ratio at or below which a cost is drawn green.</summary>
    public const double LowerAtOrBelow = 0.8;

    /// <summary>The middle value, or the mean of the middle two for an even count; null
    /// for none.</summary>
    public static decimal? Median(IEnumerable<long> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var sorted = values.Order().ToList();

        if (sorted.Count == 0) return null;

        var middle = sorted.Count / 2;

        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + (decimal)sorted[middle]) / 2;
    }

    /// <summary>Which of the four ways a ratio is drawn. Green wins over typical where
    /// both could hold — they never do at these thresholds.</summary>
    public static CostBand Band(double ratio) =>
        ratio <= LowerAtOrBelow ? CostBand.Lower
        : Math.Abs(ratio - 1) <= TypicalTolerance + 1e-9 ? CostBand.Typical
        : ratio >= HigherFrom ? CostBand.Higher
        : CostBand.Plain;

    /// <summary>A ratio as a reader sees it: one decimal, "2.4×" — two where one would
    /// round it across a band's edge, so 1.46 reads "1.46×" rather than an unhighlighted
    /// "1.5×".</summary>
    public static string RatioLabel(double ratio)
    {
        var rounded = Math.Round(ratio, 1, MidpointRounding.AwayFromZero);
        var format = Band(rounded) == Band(ratio) && (ratio >= 0.095 || ratio <= 0) ? "0.0" : "0.00";

        return ratio.ToString(format, CultureInfo.InvariantCulture) + "×";
    }

    /// <summary>Whether a dashboard's status word says finished.</summary>
    public static bool Finished(string status) =>
        status.ToLowerInvariant() is "done" or "completed" or "success";

    /// <summary>
    /// The runs a run is compared with: the same skill, finished, started before it —
    /// latest first, at most <see cref="Window"/>. A run with no start is placed by its
    /// last update.
    /// </summary>
    public static IReadOnlyList<DeliveryRun> Earlier(DeliveryRun run, IEnumerable<DeliveryRun> catalog)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(catalog);

        var at = run.StartedAt ?? run.UpdatedAt;

        return
        [
            .. catalog
                .Where(other => !ReferenceEquals(other, run)
                                && !(string.Equals(other.Id, run.Id, StringComparison.Ordinal)
                                     && string.Equals(other.Dashboard, run.Dashboard, StringComparison.Ordinal)
                                     && string.Equals(other.Worktree, run.Worktree, StringComparison.Ordinal))
                                && string.Equals(other.SkillId, run.SkillId, StringComparison.OrdinalIgnoreCase)
                                && Finished(other.Status)
                                && (other.StartedAt ?? other.UpdatedAt) < at)
                .OrderByDescending(other => other.StartedAt ?? other.UpdatedAt)
                .Take(Window)
        ];
    }

    /// <summary>
    /// The run's costs against its usual.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="costs">What each of its stages cost, in stage order.</param>
    /// <param name="earlier">The runs it is compared with and what their stages cost —
    /// <see cref="Earlier"/> chooses them; more than <see cref="Window"/> are cut to the
    /// latest.</param>
    public static DeliveryRunCostComparison Compare(
        DeliveryRun run,
        IReadOnlyList<DeliveryRunStageCost> costs,
        IReadOnlyList<PricedDeliveryRun> earlier)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(costs);
        ArgumentNullException.ThrowIfNull(earlier);

        var window = earlier
            .OrderByDescending(other => other.Run.StartedAt ?? other.Run.UpdatedAt)
            .Take(Window)
            .ToList();

        var count = Math.Min(costs.Count, run.Stages.Count);
        var total = CostFigure.Sum(costs.Take(count).Select(cost => cost.Stage));
        var stages = new List<StageCostAgainstUsual?>(run.Stages.Count);
        var medians = new Dictionary<int, CostAgainstUsual>();

        for (var index = 0; index < run.Stages.Count; index++)
        {
            if (index >= count || costs[index].Stage is not { } cost)
            {
                stages.Add(null);
                continue;
            }

            var stage = run.Stages[index];
            double? share = total is { UsdMicros: > 0 } whole ? (double)cost.UsdMicros / whole.UsdMicros : null;
            CostAgainstUsual? usual = null;

            if (Finished(stage.Status) && Against(cost.UsdMicros, StageSamples(stage.Name, window)) is { } compared)
            {
                usual = compared;
                medians[index] = compared;
            }

            stages.Add(new StageCostAgainstUsual(cost, share, usual));
        }

        return new DeliveryRunCostComparison(total, stages, Insight(run, total, stages, medians, window));
    }

    /// <summary>A figure against the median of the samples, or null with fewer than
    /// <see cref="MinimumRuns"/> or a median of nothing.</summary>
    public static CostAgainstUsual? Against(long usdMicros, IReadOnlyList<long> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Count < MinimumRuns || Median(samples) is not { } median || median <= 0) return null;

        return new CostAgainstUsual((double)(usdMicros / median), median, samples.Count);
    }

    /// <summary>The costs earlier runs' stage of this name finished at.</summary>
    private static List<long> StageSamples(string name, IEnumerable<PricedDeliveryRun> window) =>
        [.. window.Select(priced => FinishedCost(priced, name)).OfType<long>()];

    /// <summary>What the run's first finished stage of this name cost, or null where it
    /// has none or nothing priced it.</summary>
    private static long? FinishedCost(PricedDeliveryRun priced, string name)
    {
        for (var index = 0; index < priced.Run.Stages.Count && index < priced.Costs.Count; index++)
        {
            var stage = priced.Run.Stages[index];

            if (!string.Equals(stage.Name, name, StringComparison.OrdinalIgnoreCase) || !Finished(stage.Status)) continue;

            return priced.Costs[index].Stage?.UsdMicros;
        }

        return null;
    }

    private static DeliveryRunCostInsight? Insight(
        DeliveryRun run,
        CostFigure? total,
        IReadOnlyList<StageCostAgainstUsual?> stages,
        IReadOnlyDictionary<int, CostAgainstUsual> medians,
        IReadOnlyList<PricedDeliveryRun> window)
    {
        if (run.InProgress || total is null) return null;

        // Like for like: an earlier run's total is the same stages this run priced, and
        // only a run that finished and priced every one of them is a sample. Summed
        // over whatever each earlier run happened to price, a run from before its
        // tokens were counted per stage reads as a cheap run, the median sinks, and
        // every run that follows reads many times its usual while each of its stages
        // reads typical.
        var names = new List<string>();

        for (var index = 0; index < stages.Count; index++)
        {
            if (stages[index] is not null) names.Add(run.Stages[index].Name);
        }

        var totals = new List<long>();

        foreach (var priced in window)
        {
            long sum = 0;
            var whole = true;

            foreach (var name in names)
            {
                if (FinishedCost(priced, name) is not { } cost)
                {
                    whole = false;
                    break;
                }

                sum += cost;
            }

            if (whole) totals.Add(sum);
        }

        if (Against(total.UsdMicros, totals) is not { } usual) return null;

        if (usual.Band is CostBand.Typical || medians.Count == 0)
        {
            return new DeliveryRunCostInsight(run.SkillId, usual, null, null, 0, 0);
        }

        var above = usual.Ratio > 1;
        int? pick = null;
        decimal pickDifference = 0;

        foreach (var (index, stageUsual) in medians)
        {
            var difference = stages[index]!.Cost.UsdMicros - stageUsual.MedianUsdMicros;

            if (above ? difference <= 0 : difference >= 0) continue;

            if (pick is null || (above ? difference > pickDifference : difference < pickDifference))
            {
                pick = index;
                pickDifference = difference;
            }
        }

        if (pick is not { } chosen) return new DeliveryRunCostInsight(run.SkillId, usual, null, null, 0, 0);

        var stage = run.Stages[chosen];

        return new DeliveryRunCostInsight(run.SkillId, usual, stage.Name, medians[chosen], pickDifference, Math.Max(0, stage.DoneCount - 1));
    }
}
