namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>A flow the page can show, and how many of its runs finished inside the
/// window.</summary>
/// <param name="SkillId">The flow's skill id, as its latest run spells it.</param>
/// <param name="Runs">How many of its runs finished inside the window.</param>
public sealed record DeliveryFlow(string SkillId, int Runs);

/// <summary>
/// What a phase ran under: the model family, the effort, the agent and the mode. Built
/// from what ran rather than from what was configured, because the page compares the
/// configurations that ran a phase.
/// </summary>
/// <param name="Family">The model family, such as <c>opus</c>, or null where no model
/// is known.</param>
/// <param name="Effort">The effort, lower case, or null where none was recorded. It is
/// never defaulted: an effort nobody recorded is not the default one.</param>
/// <param name="Agent">The agent whose instructions ran, or null in the owner session —
/// and null where a comparison leaves it out because every configuration of the phase
/// shares it.</param>
/// <param name="Mode">How it ran, or null where a comparison leaves it out because every
/// configuration of the phase shares it.</param>
public sealed record DeliveryConfiguration(string? Family, string? Effort, string? Agent, DeliveryRunStageMode? Mode)
{
    /// <summary>The family as a reader names it, "Opus", or "Session model" where no
    /// model is known.</summary>
    public string ModelLabel => Family is { Length: > 0 } family
        ? char.ToUpperInvariant(family[0]) + family[1..]
        : "Session model";

    /// <summary>The configuration in words, such as "Sonnet at high effort via
    /// csharp-coding:coding, delegated" — each part only where it is known.</summary>
    public string Label
    {
        get
        {
            var text = ModelLabel;

            if (Effort is not null) text += $" at {Effort} effort";
            if (Agent is not null) text += $" via {Agent}";
            if (Mode is { } mode) text += $", {ModeWord(mode)}";

            return text;
        }
    }

    private static string ModeWord(DeliveryRunStageMode mode) => mode switch
    {
        DeliveryRunStageMode.Inline => "inline",
        DeliveryRunStageMode.Delegate => "delegated",
        DeliveryRunStageMode.Fork => "forked",
        _ => "gate"
    };
}

/// <summary>
/// What a phase's quality is read as, by the phase's name. Only the phases that review or
/// check the work, or that the work goes back to, have one; every other phase has none.
/// </summary>
public enum DeliveryQualityMeasure
{
    /// <summary>The phase has no quality measure.</summary>
    None,

    /// <summary>Scope: the share of runs scoped again after Implement started.</summary>
    ScopedAgain,

    /// <summary>Implement: the mean implement ⇄ review rounds a run took.</summary>
    ReviewRounds,

    /// <summary>Review: the mean blockers a reported round left open, beside the defects
    /// that escaped to Verify.</summary>
    BlockersPerRound,

    /// <summary>Build &amp; Test: the share of runs whose build and tests passed on the
    /// first pass.</summary>
    GreenFirstTime,

    /// <summary>Verify: the share of runs in which Verify found a defect review had
    /// passed.</summary>
    DefectFound
}

/// <summary>
/// A phase's quality measure over a set of runs — a phase row's, or one configuration's.
/// </summary>
/// <param name="Measure">What the value is.</param>
/// <param name="Value">The mean over the runs that reported it: a share from 0 to 1 for
/// <see cref="DeliveryQualityMeasure.ScopedAgain"/>, <see cref="DeliveryQualityMeasure.GreenFirstTime"/>
/// and <see cref="DeliveryQualityMeasure.DefectFound"/>, a count otherwise. Null where no
/// run reported it, or the phase has no measure — never a zero nobody reported.</param>
/// <param name="Runs">How many runs <paramref name="Value"/> stands on.</param>
public sealed record DeliveryQuality(DeliveryQualityMeasure Measure, double? Value, int Runs)
{
    /// <summary>A phase with no measure.</summary>
    public static DeliveryQuality None { get; } = new(DeliveryQualityMeasure.None, null, 0);

    /// <summary>Review only: the defects Verify found that review had passed, summed over
    /// the runs whose Verify reported a count, or null where none did.</summary>
    public int? Escaped { get; init; }

    /// <summary>How many runs <see cref="Escaped"/> stands on.</summary>
    public int EscapedRuns { get; init; }

    /// <summary>Whether anything was reported for the measure.</summary>
    public bool Reported => Value is not null || Escaped is not null;

    /// <summary>The measure in words, such as "1.4 rounds", "83% green first time" or
    /// "0.5 blockers per round · 2 escaped to Verify"; "no measure" for a phase without
    /// one, and "not reported" where no run reported it.</summary>
    public string Label
    {
        get
        {
            if (Measure is DeliveryQualityMeasure.None) return "no measure";

            var value = Value switch
            {
                null => null,
                { } share when Measure is DeliveryQualityMeasure.ScopedAgain => $"{Percent(share)} scoped again",
                { } share when Measure is DeliveryQualityMeasure.GreenFirstTime => $"{Percent(share)} green first time",
                { } share when Measure is DeliveryQualityMeasure.DefectFound => $"{Percent(share)} found a defect",
                { } rounds when Measure is DeliveryQualityMeasure.ReviewRounds => $"{Number(rounds)} {(Number(rounds) == "1" ? "round" : "rounds")}",
                { } blockers => $"{Number(blockers)} blockers per round"
            };

            var escaped = Measure is DeliveryQualityMeasure.BlockersPerRound && Escaped is { } count
                ? $"{count} escaped to Verify"
                : null;

            return (value, escaped) switch
            {
                (null, null) => "not reported",
                (null, { } only) => $"not reported · {only}",
                ({ } only, null) => only,
                var (both, also) => $"{both} · {also}"
            };
        }
    }

    /// <summary>What the measure is and how many runs it stands on, for a tooltip.</summary>
    public string Title => Measure switch
    {
        DeliveryQualityMeasure.None => "This phase has no quality measure",
        _ when !Reported => "No run in the window reported it",
        DeliveryQualityMeasure.ScopedAgain => $"Share of runs scoped again after Implement started, over {RunsWord(Runs)}",
        DeliveryQualityMeasure.ReviewRounds => $"Mean implement-review rounds, over {RunsWord(Runs)}",
        DeliveryQualityMeasure.BlockersPerRound => $"Mean blockers a reported round left open, over {RunsWord(Runs)}; defects Verify found that review had passed, over {RunsWord(EscapedRuns)}",
        DeliveryQualityMeasure.GreenFirstTime => $"Share of runs whose Build & Test passed on its first pass, over {RunsWord(Runs)}",
        _ => $"Share of runs in which Verify found a defect review had passed, over {RunsWord(Runs)}"
    };

    /// <summary>
    /// Whether the cheaper configuration's measure is within a tenth of the dearer one's,
    /// in the measure's worse direction: Build &amp; Test reads higher as better, every
    /// other measure lower. The tenth is of the dearer value, at least of 1 — ten points
    /// for a share, a tenth of the rounds or blockers for a count — so a dearer value of
    /// zero still leaves room. True for a phase with no measure: there is nothing for the
    /// cheaper one to fall short on. Null where either side reported nothing: unknown,
    /// never assumed level.
    /// </summary>
    public static bool? Within(DeliveryQuality cheaper, DeliveryQuality dearer)
    {
        ArgumentNullException.ThrowIfNull(cheaper);
        ArgumentNullException.ThrowIfNull(dearer);

        if (dearer.Measure is DeliveryQualityMeasure.None) return true;
        if (cheaper.Value is not { } low || dearer.Value is not { } high) return null;

        var worse = dearer.Measure is DeliveryQualityMeasure.GreenFirstTime ? high - low : low - high;

        return worse <= Tolerance * Math.Max(Math.Abs(high), 1) + 1e-9;
    }

    /// <summary>How far the cheaper configuration's quality may fall short and still count
    /// as level: a tenth.</summary>
    public const double Tolerance = 0.1;

    internal static string Percent(double share) =>
        $"{Math.Round(share * 100, MidpointRounding.AwayFromZero).ToString("0", System.Globalization.CultureInfo.InvariantCulture)}%";

    private static string Number(double value) =>
        Math.Round(value, 1, MidpointRounding.AwayFromZero).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    private static string RunsWord(int count) => count == 1 ? "1 run" : $"{count} runs";
}

/// <summary>
/// One run's part in a phase: the configuration it ran the phase under and what that
/// cost.
/// </summary>
/// <param name="Run">The run.</param>
/// <param name="Configuration">What ran the phase, with the agent and the mode left out
/// where every run of the phase shares them.</param>
/// <param name="Cost">What the phase cost in this run, or null where nothing priced it.</param>
/// <param name="RunTotal">What the run cost end to end: the sum over the stages it
/// finished that were priced, or null where none was.</param>
public sealed record DeliveryPhaseSample(DeliveryRun Run, DeliveryConfiguration Configuration, CostFigure? Cost, CostFigure? RunTotal)
{
    /// <summary>Whether <see cref="RunTotal"/> leaves out a stage the run finished that
    /// nothing priced — a total over part of the run, which a surface says it stands on
    /// rather than dropping the run.</summary>
    public bool RunTotalPartial { get; init; }

    /// <summary>The phase's quality measure in this run (<see cref="DeliveryQualityMeasure"/>):
    /// 1 or 0 for a share's yes or no, a count otherwise — or null where the run reported
    /// nothing it could be read from, or the phase has no measure.</summary>
    public double? Quality { get; init; }

    /// <summary>Review only: the defects this run's Verify found that review had passed,
    /// or null where its Verify reported no count.</summary>
    public int? EscapedDefects { get; init; }
}

/// <summary>
/// One phase of a flow over the runs in the window: one row of the page's table.
/// </summary>
/// <param name="Name">The stage name, as the latest run spells it.</param>
/// <param name="Now">The configuration now: what the latest run that carries the phase
/// configured for it, each field it left to the session filled from what ran — or null
/// where no run carries it.</param>
/// <param name="Median">The median of what the phase cost, over the runs that finished
/// and priced it — estimated where any of them was — or null where none did. Never a
/// zero nobody measured.</param>
/// <param name="Share">The phase's part of the flow as it runs now: its median over the
/// sum of the medians of the phases the latest run carries — 0 to 1, those shares adding
/// up to 1 — or null where no run priced it, or where the latest run does not carry it,
/// so a phase the flow has dropped neither shows a share nor shrinks today's. Phase medians rather than run totals, because a run with one
/// stage unpriced has no honest total, and with recorded runs most have one.</param>
/// <param name="Samples">One per run that finished the phase, latest first.</param>
public sealed record DeliveryPhase(
    string Name,
    DeliveryConfiguration? Now,
    CostFigure? Median,
    double? Share,
    IReadOnlyList<DeliveryPhaseSample> Samples)
{
    /// <summary>How many runs finished the phase.</summary>
    public int Runs => Samples.Count;

    /// <summary>How many runs finished and priced the phase.</summary>
    public int PricedRuns => Samples.Count(sample => sample.Cost is not null);

    /// <summary>How many configurations ran the phase.</summary>
    public int Configurations => Samples.Select(sample => sample.Configuration).Distinct().Count();

    /// <summary>Whether the phase's configurations keep the agent — they differ on it.
    /// Where they do not, a card's null agent means "the agent every run shared"; where
    /// they do, it means the owner session.</summary>
    public bool AgentKept { get; init; }

    /// <summary>Whether the phase's configurations keep the mode — they differ on it.</summary>
    public bool ModeKept { get; init; }

    /// <summary>The agent every run of the phase shared, where <see cref="AgentKept"/> is
    /// false and they shared one — so a card can still say which agent ran it, as
    /// recorded — else null.</summary>
    public string? SharedAgent { get; init; }

    /// <summary>What the phase's quality is read as.</summary>
    public DeliveryQualityMeasure Measure { get; init; }

    /// <summary>The phase's quality measure over every run that finished it.</summary>
    public DeliveryQuality Quality => DeliveryEconomics.QualityOf(Measure, Samples);
}

/// <summary>
/// One configuration that ran a phase: one card of the selected phase's panel.
/// </summary>
/// <param name="Configuration">The configuration.</param>
/// <param name="Runs">How many runs finished the phase under it.</param>
/// <param name="PricedRuns">How many of them priced the phase — a lesson's sample size.</param>
/// <param name="MedianPhase">The median phase cost over those, or null where none was
/// priced.</param>
/// <param name="MedianRun">The median whole-run cost of its runs, each over the stages it
/// finished that were priced, or null where none priced any.</param>
/// <param name="TotalRuns">How many runs <paramref name="MedianRun"/> stands on; of those,
/// <see cref="PartialRuns"/> left a finished stage unpriced.</param>
public sealed record DeliveryConfigurationCost(
    DeliveryConfiguration Configuration,
    int Runs,
    int PricedRuns,
    CostFigure? MedianPhase,
    CostFigure? MedianRun,
    int TotalRuns)
{
    /// <summary>The phase's quality measure over this configuration's runs.</summary>
    public DeliveryQuality Quality { get; init; } = DeliveryQuality.None;

    /// <summary>How many runs <see cref="Quality"/> stands on.</summary>
    public int QualityRuns => Quality.Runs;

    /// <summary>The agent that ran the phase under it, as recorded: the configuration's
    /// own where the phase's cards tell agents apart, else the one every run shared —
    /// null where it ran in the owner session.</summary>
    public string? Agent { get; init; }

    /// <summary>How many of the <see cref="TotalRuns"/> left a stage they finished
    /// unpriced, so their whole-run figure stands on part of the run.</summary>
    public int PartialRuns { get; init; }

    /// <summary>The median whole-run cost over the runs priced in every stage they
    /// finished only, or null where none was — what a lesson compares whole runs on, since
    /// a total over part of a run would read as a cheap run.</summary>
    public CostFigure? MedianCompleteRun { get; init; }

    /// <summary>How many runs <see cref="MedianCompleteRun"/> stands on.</summary>
    public int CompleteRuns { get; init; }
}

/// <summary>
/// How far a lesson can be trusted, from the smaller of its two sample sizes.
/// </summary>
public enum DeliveryConfidence
{
    /// <summary>Under 10 priced runs on either side.</summary>
    NotEnoughToCall,

    /// <summary>10 to 20 priced runs on the smaller side.</summary>
    KeepBothRunning,

    /// <summary>More than 20 priced runs on both sides.</summary>
    FairlyConfident
}

/// <summary>
/// The phase table's verdict on a phase: whether its configuration is worth tuning, and
/// which way.
/// </summary>
public enum DeliveryVerdict
{
    /// <summary>The dearer configuration lowers the median run cost: the cheaper phase
    /// raises the run's cost.</summary>
    WorthIt,

    /// <summary>The cheaper configuration lowers the run cost and is within a tenth of the
    /// dearer one on the quality measure.</summary>
    TryCheaper,

    /// <summary>The phase is under 2% of the run's cost.</summary>
    NothingToTune,

    /// <summary>One configuration only has run the phase.</summary>
    NotCompared,

    /// <summary>Two configurations ran it, and the comparison does not settle either way
    /// yet: too few runs for a lesson, the whole-run difference unknown or nil, or the
    /// cheaper one's quality short of the dearer one's or not reported.</summary>
    NoCall
}

/// <summary>A phase's verdict and why, for the table's verdict column.</summary>
/// <param name="Kind">The verdict.</param>
/// <param name="Reason">Why, in a sentence, for a tooltip.</param>
public sealed record DeliveryPhaseVerdict(DeliveryVerdict Kind, string Reason)
{
    /// <summary>The verdict as the column reads it.</summary>
    public string Label => Kind switch
    {
        DeliveryVerdict.WorthIt => "Worth it",
        DeliveryVerdict.TryCheaper => "Try cheaper",
        DeliveryVerdict.NothingToTune => "Nothing to tune",
        DeliveryVerdict.NotCompared => "Not compared",
        _ => "No call yet"
    };
}

/// <summary>
/// One lesson card: two configurations of a phase compared on phase cost, whole-run cost
/// and the phase's quality measure side by side, with how far the sample sizes let it be
/// trusted.
/// </summary>
/// <param name="SkillId">The flow.</param>
/// <param name="Phase">The phase, as the table names it.</param>
/// <param name="Cheaper">The configuration with the lower median phase cost.</param>
/// <param name="Dearer">The one it is compared with: the configuration now where it
/// qualifies, else the other with most priced runs.</param>
/// <param name="PhaseSavingUsdMicros">The dearer median phase cost less the cheaper one.</param>
/// <param name="RunDifferenceUsdMicros">The cheaper configuration's median whole-run cost
/// less the dearer one's, over the runs each priced in every stage they finished — below
/// zero where its runs cost less too — or null where either side has fewer than
/// <see cref="DeliveryRunBaselines.MinimumRuns"/> such runs: the difference is unknown.</param>
/// <param name="KeepsNow">Whether the cheaper configuration is the configuration now and
/// its whole runs cost no more: the card then confirms it, and copies no request, since
/// there is nothing to change.</param>
public sealed record DeliveryLesson(
    string SkillId,
    string Phase,
    DeliveryConfigurationCost Cheaper,
    DeliveryConfigurationCost Dearer,
    long PhaseSavingUsdMicros,
    long? RunDifferenceUsdMicros,
    bool KeepsNow = false)
{
    /// <summary>The cheaper configuration, marked best only where its whole runs cost no
    /// more either; null where the two medians disagree or the whole runs were not
    /// compared.</summary>
    public DeliveryConfiguration? Best => RunDifferenceUsdMicros is <= 0 ? Cheaper.Configuration : null;

    /// <summary>Whether a figure the lesson stands on was estimated from tokens.</summary>
    public bool Estimated =>
        Cheaper.MedianPhase?.Estimated == true || Dearer.MedianPhase?.Estimated == true
        || Cheaper.MedianRun?.Estimated == true || Dearer.MedianRun?.Estimated == true;

    /// <summary>The lesson in a sentence, such as "Sonnet at high effort runs Implement
    /// for $2.00 less than Opus (medians), but whole runs cost $3.00 more" — or, where the
    /// cheaper is the configuration now, "Keep Sonnet at high effort for Implement: it runs
    /// it for $2.00 less than Opus (medians), …".</summary>
    public string Headline
    {
        get
        {
            var head = KeepsNow
                ? $"Keep {Cheaper.Configuration.Label} for {Phase}: it runs it for {Amount(PhaseSavingUsdMicros)} less than {Dearer.Configuration.Label} (medians)"
                : $"{Cheaper.Configuration.Label} runs {Phase} for {Amount(PhaseSavingUsdMicros)} less than {Dearer.Configuration.Label} (medians)";

            return RunDifferenceUsdMicros switch
            {
                null => $"{head}; the whole-run difference is unknown",
                < 0 and var less => $"{head}, and whole runs cost {Amount(-less)} less",
                0 => $"{head}, and whole runs cost the same",
                var more => $"{head}, but whole runs cost {Amount(more.Value)} more: the cheaper phase raises the run's cost"
            };
        }
    }

    /// <summary>Both sample sizes, such as "Sonnet at high effort: 6 runs · Opus: 5
    /// runs" — a lesson from five runs must not read like one from forty.</summary>
    public string Samples =>
        $"{Cheaper.Configuration.Label}: {Cheaper.PricedRuns} runs · {Dearer.Configuration.Label}: {Dearer.PricedRuns} runs";

    /// <summary>How far the lesson can be trusted, from the smaller sample: under 10 runs
    /// on either side it is not enough to call, 10 to 20 keep both running, more is
    /// fairly confident.</summary>
    public DeliveryConfidence Confidence => Math.Min(Cheaper.PricedRuns, Dearer.PricedRuns) switch
    {
        < 10 => DeliveryConfidence.NotEnoughToCall,
        <= 20 => DeliveryConfidence.KeepBothRunning,
        _ => DeliveryConfidence.FairlyConfident
    };

    /// <summary>The confidence in words, with the smaller sample it stands on.</summary>
    public string ConfidenceLine
    {
        get
        {
            var smaller = Math.Min(Cheaper.PricedRuns, Dearer.PricedRuns);

            return Confidence switch
            {
                DeliveryConfidence.NotEnoughToCall => $"Not enough to call: {smaller} runs on the smaller side, under 10.",
                DeliveryConfidence.KeepBothRunning => $"Keep both running: {smaller} runs on the smaller side, 10 to 20.",
                _ => $"Fairly confident: {smaller} runs on the smaller side, over 20."
            };
        }
    }

    /// <summary>The phase's quality measure for both configurations side by side, such as
    /// "Quality measure: Sonnet at high effort 1.2 rounds · Opus 1.4 rounds", or that the
    /// phase has none.</summary>
    public string QualityNote => Cheaper.Quality.Measure is DeliveryQualityMeasure.None
        ? "This phase has no quality measure: this compares cost only."
        : $"Quality measure: {Cheaper.Configuration.Label} {Cheaper.Quality.Label} · {Dearer.Configuration.Label} {Dearer.Quality.Label}";

    /// <summary>Whether the cheaper configuration is within a tenth of the dearer one on
    /// the quality measure (<see cref="DeliveryQuality.Within"/>), or null where either
    /// side reported nothing.</summary>
    public bool? QualityWithin => DeliveryQuality.Within(Cheaper.Quality, Dearer.Quality);

    /// <summary>Whether the dearer configuration is the configuration now.</summary>
    public bool DearerIsNow { get; init; }

    /// <summary>The configuration the lesson points to: the dearer one where its whole
    /// runs cost less — the cheaper phase raises the run's cost — else the cheaper one.</summary>
    public DeliveryConfigurationCost Target => RunDifferenceUsdMicros > 0 ? Dearer : Cheaper;

    /// <summary>The request the card's button copies, for the person to paste into a
    /// session: the <c>devbook-config:local</c> skill, the flow, the phase, and the model
    /// and effort of <see cref="Target"/> — its agent and mode too where the phase's
    /// configurations differ on them. Null where the target is the configuration now:
    /// setting what is already set changes nothing. The app never writes the overlay
    /// itself.</summary>
    public string? Request
    {
        get
        {
            var dearer = ReferenceEquals(Target, Dearer);

            if (dearer ? DearerIsNow : CheaperIsNow) return null;

            var target = Target.Configuration;
            var other = dearer ? Cheaper : Dearer;

            var model = target.Family is { Length: > 0 } family ? $"its model to {family}" : "its model to the session's";
            var effort = target.Effort is { } level ? $"its effort to {level}" : "leave its effort unset";
            // The agent and the mode only where the phase's configurations differ on them —
            // a request that set the model and effort alone would change nothing there.
            var agent = target.Agent is { } name ? $", its agent to {name}" : string.Empty;
            var mode = target.Mode is { } how ? $", its mode to {how.ToString().ToLowerInvariant()}" : string.Empty;

            var evidence = dearer
                ? $"The Delivery economics page found its whole runs {Amount(RunDifferenceUsdMicros!.Value)} cheaper (medians) than with {other.Configuration.Label}, "
                  + $"though the phase itself costs {Amount(PhaseSavingUsdMicros)} more"
                : $"The Delivery economics page found it {Amount(PhaseSavingUsdMicros)} cheaper per run of that phase than {other.Configuration.Label}"
                  + RunDifferenceUsdMicros switch
                  {
                      null => ", the whole-run difference unknown",
                      < 0 and var less => $", and the whole run {Amount(-less)} cheaper",
                      _ => ", and the whole run the same"
                  };

            var quality = Cheaper.Quality.Measure is DeliveryQualityMeasure.None
                ? "The phase has no quality measure."
                : $"Quality measure: {target.Label} {Target.Quality.Label}, against {other.Quality.Label}.";

            return $"Use devbook-config:local to set the {Phase} phase of {SkillId} in my personal overlay: {model}{agent}{mode}, {effort}. "
                   + $"{evidence}, over {Target.PricedRuns} and {other.PricedRuns} priced runs. {quality} {ConfidenceLine}";
        }
    }

    /// <summary>Whether the cheaper configuration is the configuration now, whether or
    /// not its whole runs cost more.</summary>
    public bool CheaperIsNow { get; init; }

    private static string Amount(long usdMicros) => new CostFigure(usdMicros, CostSource.Reported).Label;
}

/// <summary>
/// The page's figures for one flow over the runs in the window.
/// </summary>
/// <param name="Runs">How many runs the figures stand on.</param>
/// <param name="ReportedRuns">How many of them Claude Code reported every priced stage
/// of.</param>
/// <param name="EstimatedRuns">How many of them had a stage estimated from tokens. The
/// rest priced nothing.</param>
/// <param name="MedianRun">The median whole-run cost, each run over the stages it finished
/// that were priced, or null where no run priced any.</param>
/// <param name="P90Run">The 90th percentile of the same, by nearest rank.</param>
/// <param name="Phases">One per stage name the runs carry, in run order.</param>
public sealed record DeliveryEconomicsReport(
    int Runs,
    int ReportedRuns,
    int EstimatedRuns,
    CostFigure? MedianRun,
    CostFigure? P90Run,
    IReadOnlyList<DeliveryPhase> Phases)
{
    /// <summary>The figures over no runs.</summary>
    public static DeliveryEconomicsReport Empty { get; } = new(0, 0, 0, null, null, []);

    /// <summary>How many runs <see cref="MedianRun"/> stands on: those that priced any
    /// stage.</summary>
    public int TotalRuns { get; init; }

    /// <summary>How many of the <see cref="TotalRuns"/> left a stage they finished
    /// unpriced, so their figure stands on part of the run — said beside the median
    /// rather than dropping the run.</summary>
    public int PartialRuns { get; init; }

    /// <summary>Implement and Review's part of all the spend: what those two phases cost
    /// over every run, over what every stage the runs finished cost — 0 to 1 — or null
    /// where nothing was priced.</summary>
    public double? ImplementReviewShare { get; init; }

    /// <summary>How many runs reported their implement ⇄ review rounds — what
    /// <see cref="MultiRoundRuns"/> is counted among.</summary>
    public int RoundRuns { get; init; }

    /// <summary>How many of the <see cref="RoundRuns"/> took two rounds or more.</summary>
    public int MultiRoundRuns { get; init; }

    /// <summary>The median whole-run cost of the runs that took two rounds or more over
    /// that of the runs that took one, each over runs priced in every stage they
    /// finished — or null where either side has none, or the one-round median is zero.</summary>
    public double? MultiRoundCostRatio { get; init; }

    /// <summary>The defects that escaped review: what Verify found that review had passed,
    /// summed over the runs whose Verify reported a count, or null where none did.</summary>
    public int? EscapedDefects { get; init; }

    /// <summary>How many runs <see cref="EscapedDefects"/> stands on.</summary>
    public int EscapedRuns { get; init; }

    /// <summary>How many runs the quality measure stands on: those with a quality
    /// figure for any phase.</summary>
    public int QualityRuns =>
        Phases.SelectMany(phase => phase.Samples)
            .Where(sample => sample.Quality is not null)
            .Select(sample => sample.Run)
            .Distinct(ReferenceEqualityComparer.Instance)
            .Count();
}

/// <summary>
/// The figures the Delivery economics page draws: per phase of a flow, the configurations
/// that ran it, side by side on phase cost and whole-run cost.
/// <list type="bullet">
/// <item><b>Runs.</b> The runs of one flow that finished, placed by their start — their
/// last update where they recorded none — inside a window ending now.</item>
/// </list>
/// </summary>
public static class DeliveryEconomics
{
    /// <summary>
    /// The runs of a flow the page compares: the same skill id in any casing, finished,
    /// and placed inside the window — latest first.
    /// </summary>
    /// <param name="catalog">Every run the page may show.</param>
    /// <param name="skillId">The flow.</param>
    /// <param name="window">How far back from <paramref name="now"/> a run may be placed.</param>
    /// <param name="now">The end of the window.</param>
    public static IReadOnlyList<DeliveryRun> Runs(IEnumerable<DeliveryRun> catalog, string skillId, TimeSpan window, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(skillId);

        return
        [
            .. Inside(catalog, window, now)
                .Where(run => string.Equals(run.SkillId, skillId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(At)
        ];
    }

    /// <summary>
    /// The flows the page offers: every skill id with a finished run inside the window,
    /// spelled as its latest run spells it, most runs first and then by name.
    /// </summary>
    public static IReadOnlyList<DeliveryFlow> Flows(IEnumerable<DeliveryRun> catalog, TimeSpan window, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return
        [
            .. Inside(catalog, window, now)
                .Where(run => !string.IsNullOrWhiteSpace(run.SkillId))
                .OrderByDescending(At)
                .GroupBy(run => run.SkillId, StringComparer.OrdinalIgnoreCase)
                .Select(flow => new DeliveryFlow(flow.First().SkillId, flow.Count()))
                .OrderByDescending(flow => flow.Runs)
                .ThenBy(flow => flow.SkillId, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>
    /// The flow the page opens on: the one asked for — a run's insight line asks for
    /// its own flow — else the one with most runs, or null where there is none.
    /// </summary>
    public static string? DefaultFlow(IReadOnlyList<DeliveryFlow> flows, string? requested)
    {
        ArgumentNullException.ThrowIfNull(flows);

        return !string.IsNullOrWhiteSpace(requested) ? requested : flows.FirstOrDefault()?.SkillId;
    }

    /// <summary>
    /// The configuration a stage ran under. A stage that ran in the owner session, or in
    /// a fork of it, and recorded no model ran on the owner session's model.
    /// </summary>
    /// <param name="stage">The stage, resolved.</param>
    /// <param name="ownerModel">The owner session's model, where known.</param>
    public static DeliveryConfiguration ConfigurationOf(DeliveryRunStageExecution stage, string? ownerModel = null)
    {
        ArgumentNullException.ThrowIfNull(stage);

        var model = stage.Model
            ?? (stage.Mode is DeliveryRunStageMode.Inline or DeliveryRunStageMode.Fork ? ownerModel : null);

        return new DeliveryConfiguration(
            string.IsNullOrWhiteSpace(model) ? null : DeliveryRunStageResolution.Family(model),
            string.IsNullOrWhiteSpace(stage.Effort) ? null : stage.Effort.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(stage.Agent) ? null : stage.Agent.Trim(),
            stage.Mode);
    }

    /// <summary>
    /// The page's figures over a flow's priced runs: the run figures and one row per
    /// stage name, in the latest run's order, a stage only older runs carry placed after
    /// the stage it followed there.
    /// </summary>
    /// <param name="runs">The runs <see cref="Runs"/> chose, each with what its stages
    /// cost, in stage order, and its owner session's model where known.</param>
    public static DeliveryEconomicsReport Of(IReadOnlyList<PricedDeliveryRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        if (runs.Count == 0) return DeliveryEconomicsReport.Empty;

        var ordered = runs.OrderByDescending(priced => At(priced.Run)).ToList();
        var resolved = ordered.Select(priced => DeliveryRunStageResolution.Of(priced.Run)).ToList();
        var totals = ordered.Select(Total).ToList();
        var whole = totals.Select(total => total.Figure).OfType<CostFigure>().ToList();
        var medianRun = MedianOf(whole);

        var reported = 0;
        var estimated = 0;

        foreach (var priced in ordered)
        {
            switch (CostFigure.Sum(StageCosts(priced)))
            {
                case { Estimated: true }:
                    estimated++;
                    break;
                case not null:
                    reported++;
                    break;
            }
        }

        var phases = PhaseNames(ordered)
            .Select(name => Phase(name, ordered, resolved, totals))
            .ToList();

        // Each phase's share is its part of the sum of the medians of the phases the
        // flow runs now — the latest run's: it never exceeds the whole and those shares
        // add up to it, whichever runs priced what, and a dropped phase does not count.
        var current = new HashSet<string>(ordered[0].Run.Stages.Select(stage => stage.Name), StringComparer.OrdinalIgnoreCase);
        var medians = phases.Where(phase => current.Contains(phase.Name)).Sum(phase => phase.Median?.UsdMicros ?? 0);

        phases =
        [
            .. phases.Select(phase => phase with
            {
                Share = phase.Median is { } median && medians > 0 && current.Contains(phase.Name) ? (double)median.UsdMicros / medians : null
            })
        ];

        // Implement and Review over all spend: summed across the runs, so a dear run
        // weighs as much as it cost, over the same finished stages the totals add up.
        var spend = totals.Sum(total => total.Figure?.UsdMicros ?? 0);
        var implementReview = ordered.Sum(priced => FinishedCosts(priced)
            .Where(stage => MeasureOf(stage.Name) is DeliveryQualityMeasure.ReviewRounds or DeliveryQualityMeasure.BlockersPerRound)
            .Sum(stage => stage.Cost.UsdMicros));

        // Two rounds or more against one, on the runs that reported their rounds and were
        // priced in every stage they finished: a total over part of a run reads cheap.
        var rounds = ordered.Select(priced => RoundsOf(priced.Run)).ToList();
        var multi = new List<CostFigure>();
        var single = new List<CostFigure>();

        for (var index = 0; index < ordered.Count; index++)
        {
            if (rounds[index] is not { } count || totals[index] is not { Figure: { } figure, Partial: false }) continue;

            (count >= 2 ? multi : single).Add(figure);
        }

        double? ratio = MedianOf(multi) is { } multiMedian && MedianOf(single) is { UsdMicros: > 0 } singleMedian
            ? (double)multiMedian.UsdMicros / singleMedian.UsdMicros
            : null;

        var escapes = ordered.Select(priced => VerifyDefectsOf(priced.Run)).OfType<int>().ToList();

        return new DeliveryEconomicsReport(ordered.Count, reported, estimated, medianRun, PercentileOf(whole, 0.9), phases)
        {
            TotalRuns = whole.Count,
            PartialRuns = totals.Count(total => total is { Figure: not null, Partial: true }),
            ImplementReviewShare = spend > 0 ? (double)implementReview / spend : null,
            RoundRuns = rounds.Count(count => count is not null),
            MultiRoundRuns = rounds.Count(count => count >= 2),
            MultiRoundCostRatio = ratio,
            EscapedDefects = escapes.Count == 0 ? null : escapes.Sum(),
            EscapedRuns = escapes.Count
        };
    }

    /// <summary>
    /// What a phase's quality is read as, by its name: Scope, Implement, Review,
    /// Build &amp; Test and Verify have a measure, in any casing and with "and" for the
    /// ampersand; every other phase has none.
    /// </summary>
    public static DeliveryQualityMeasure MeasureOf(string phase)
    {
        ArgumentNullException.ThrowIfNull(phase);

        var key = new string([.. phase.Where(char.IsLetter).Select(char.ToLowerInvariant)]);

        return key switch
        {
            "scope" => DeliveryQualityMeasure.ScopedAgain,
            "implement" => DeliveryQualityMeasure.ReviewRounds,
            "review" => DeliveryQualityMeasure.BlockersPerRound,
            "buildtest" or "buildandtest" => DeliveryQualityMeasure.GreenFirstTime,
            "verify" => DeliveryQualityMeasure.DefectFound,
            _ => DeliveryQualityMeasure.None
        };
    }

    /// <summary>
    /// A measure over a set of a phase's samples: the mean of what they reported, how
    /// many did, and for Review the defects their Verify found.
    /// </summary>
    public static DeliveryQuality QualityOf(DeliveryQualityMeasure measure, IEnumerable<DeliveryPhaseSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (measure is DeliveryQualityMeasure.None) return DeliveryQuality.None;

        var list = samples as IReadOnlyCollection<DeliveryPhaseSample> ?? [.. samples];
        var values = list.Select(sample => sample.Quality).OfType<double>().ToList();
        var escaped = list.Select(sample => sample.EscapedDefects).OfType<int>().ToList();

        return new DeliveryQuality(measure, values.Count == 0 ? null : values.Average(), values.Count)
        {
            Escaped = measure is DeliveryQualityMeasure.BlockersPerRound && escaped.Count > 0 ? escaped.Sum() : null,
            EscapedRuns = measure is DeliveryQualityMeasure.BlockersPerRound ? escaped.Count : 0
        };
    }

    /// <summary>
    /// The implement ⇄ review rounds a run took: the most rounds any one slice took,
    /// from the run's cumulative Quality Facts where it carries them, else the highest
    /// round its Implement or Review reported, or null where neither said. Never counted
    /// from the stages' passes: the delivery phases report it, and this product works
    /// out none of it itself.
    /// </summary>
    public static int? RoundsOf(DeliveryRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.Quality?.HighestSliceRounds is { } highest) return highest;

        var reported = run.Stages
            .Where(stage => MeasureOf(stage.Name) is DeliveryQualityMeasure.ReviewRounds or DeliveryQualityMeasure.BlockersPerRound)
            .Select(stage => stage.Round)
            .OfType<int>()
            .ToList();

        return reported.Count == 0 ? null : reported.Max();
    }

    /// <summary>
    /// Every Review pass the run made, over every slice: the Quality Facts' total where
    /// the run carries it, else the highest round its Implement or Review reported, or
    /// null where neither said.
    /// </summary>
    public static int? ReviewRoundsOf(DeliveryRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return run.Quality?.ReviewRounds ?? RoundsOf(run);
    }

    /// <summary>
    /// The defects a run's Verify found that review had passed, over every pass: the
    /// escaped defects the run's Quality Facts filed under Verify, else what its Verify
    /// stage's latest pass reported, or null where neither said.
    /// </summary>
    public static int? VerifyDefectsOf(DeliveryRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return run.Quality?.VerifyDefects ?? StageVerifyDefects(run);
    }

    private static int? StageVerifyDefects(DeliveryRun run) =>
        run.Stages.FirstOrDefault(stage => MeasureOf(stage.Name) is DeliveryQualityMeasure.DefectFound && stage.DefectsFound is not null)?.DefectsFound;

    /// <summary>
    /// One run's quality figure for a phase, from the stage's own pass and the run around
    /// it, or null where nothing it is read from was reported.
    /// </summary>
    private static double? SampleQuality(DeliveryQualityMeasure measure, DeliveryRun run, DeliveryRunStage stage) => measure switch
    {
        // Scoped again only counts once Implement has started: a run that never reached
        // it could not have gone back. Scope finished more than once is the second pass.
        DeliveryQualityMeasure.ScopedAgain => run.Stages.FirstOrDefault(other => MeasureOf(other.Name) is DeliveryQualityMeasure.ReviewRounds) is { } implement
                                               && Started(implement)
            ? stage.DoneCount > 1 ? 1 : 0
            : null,
        DeliveryQualityMeasure.ReviewRounds => RoundsOf(run),
        DeliveryQualityMeasure.BlockersPerRound => run.Quality?.BlockersPerRound ?? stage.Blockers,
        DeliveryQualityMeasure.GreenFirstTime => stage.DoneCount == 1 ? 1 : 0,
        DeliveryQualityMeasure.DefectFound => VerifyDefectsOf(run) is { } found ? found > 0 ? 1 : 0 : null,
        _ => null
    };

    /// <summary>Whether a stage got past pending: it is under way, finished, or was
    /// marked done at least once.</summary>
    private static bool Started(DeliveryRunStage stage) =>
        stage.DoneCount > 0 || stage.StartedAt is not null
        || !string.Equals(stage.Status, "pending", StringComparison.OrdinalIgnoreCase);

    /// <summary>The run's finished, priced stages with what each cost.</summary>
    private static IEnumerable<(string Name, CostFigure Cost)> FinishedCosts(PricedDeliveryRun priced)
    {
        for (var index = 0; index < priced.Run.Stages.Count && index < priced.Costs.Count; index++)
        {
            var stage = priced.Run.Stages[index];

            if (DeliveryRunBaselines.Finished(stage.Status) && priced.Costs[index].Stage is { } cost) yield return (stage.Name, cost);
        }
    }

    /// <summary>
    /// The configurations that ran a phase, side by side: most priced runs first, then
    /// the cheaper phase median, then by name.
    /// </summary>
    public static IReadOnlyList<DeliveryConfigurationCost> Compare(DeliveryPhase phase)
    {
        ArgumentNullException.ThrowIfNull(phase);

        return
        [
            .. phase.Samples
                .GroupBy(sample => sample.Configuration)
                .Select(group =>
                {
                    var samples = group.ToList();
                    var totals = samples.Select(sample => sample.RunTotal).OfType<CostFigure>().ToList();
                    var complete = samples.Where(sample => !sample.RunTotalPartial).Select(sample => sample.RunTotal).OfType<CostFigure>().ToList();
                    return new DeliveryConfigurationCost(
                        group.Key,
                        samples.Count,
                        samples.Count(sample => sample.Cost is not null),
                        MedianOf([.. samples.Select(sample => sample.Cost).OfType<CostFigure>()]),
                        MedianOf(totals),
                        totals.Count)
                    {
                        PartialRuns = samples.Count(sample => sample.RunTotal is not null && sample.RunTotalPartial),
                        MedianCompleteRun = MedianOf(complete),
                        CompleteRuns = complete.Count,
                        Quality = QualityOf(phase.Measure, samples),
                        Agent = group.Key.Agent ?? (phase.AgentKept ? null : phase.SharedAgent)
                    };
                })
                .OrderByDescending(cost => cost.PricedRuns)
                .ThenBy(cost => cost.MedianPhase?.UsdMicros ?? long.MaxValue)
                .ThenBy(cost => cost.Configuration.Label, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>
    /// The phase's lesson, or null unless two configurations each priced at least
    /// <see cref="DeliveryRunBaselines.MinimumRuns"/> runs of it at different median
    /// costs. The cheapest of those is compared with the configuration now where that
    /// qualifies, else with the other that priced most runs. The quality measure is set
    /// side by side, never folded into the cost.
    /// </summary>
    /// <param name="skillId">The flow, which the copied request names.</param>
    /// <param name="phase">The phase.</param>
    public static DeliveryLesson? Lesson(string skillId, DeliveryPhase phase)
    {
        ArgumentNullException.ThrowIfNull(skillId);
        ArgumentNullException.ThrowIfNull(phase);

        var qualifying = Compare(phase)
            .Where(cost => cost.PricedRuns >= DeliveryRunBaselines.MinimumRuns && cost.MedianPhase is not null)
            .ToList();

        if (qualifying.Count < 2) return null;

        var cheaper = qualifying.MinBy(cost => cost.MedianPhase!.UsdMicros)!;
        var others = qualifying.Where(cost => !ReferenceEquals(cost, cheaper)).ToList();
        var dearer = others.FirstOrDefault(cost => Matches(phase, cost.Configuration)) ?? others[0];

        var saving = dearer.MedianPhase!.UsdMicros - cheaper.MedianPhase!.UsdMicros;

        if (saving <= 0) return null;

        // Whole runs are compared over complete runs only, and only with enough of them
        // on both sides; otherwise the difference is unknown and nothing is marked best.
        long? difference = cheaper.CompleteRuns >= DeliveryRunBaselines.MinimumRuns
                           && dearer.CompleteRuns >= DeliveryRunBaselines.MinimumRuns
                           && cheaper.MedianCompleteRun is { } cheaperRun
                           && dearer.MedianCompleteRun is { } dearerRun
            ? cheaperRun.UsdMicros - dearerRun.UsdMicros
            : null;

        var cheaperIsNow = Matches(phase, cheaper.Configuration);

        // The card confirms the configuration now only where its whole runs cost no more:
        // a cheaper phase that raises the run's cost is no reason to keep it.
        return new DeliveryLesson(skillId, phase.Name, cheaper, dearer, saving, difference, cheaperIsNow && difference is not > 0)
        {
            CheaperIsNow = cheaperIsNow,
            DearerIsNow = Matches(phase, dearer.Configuration)
        };
    }

    /// <summary>The share of the run under which a phase has nothing to tune: 2%.</summary>
    public const double NothingToTuneShare = 0.02;

    /// <summary>
    /// The phase table's verdict on a phase, the first rule that holds:
    /// <list type="number">
    /// <item><b>Nothing to tune</b> — the phase is under 2% of the run's cost (its
    /// <see cref="DeliveryPhase.Share"/>).</item>
    /// <item><b>Not compared</b> — one configuration only has run it.</item>
    /// <item><b>Worth it</b> — the dearer configuration lowers the median run cost.</item>
    /// <item><b>Try cheaper</b> — the cheaper one lowers the run cost and is within a
    /// tenth of the dearer one on the quality measure.</item>
    /// <item><b>No call yet</b> — otherwise: no lesson stands, the whole-run difference
    /// is unknown or nil, or the cheaper one's quality is short or not reported.</item>
    /// </list>
    /// </summary>
    public static DeliveryPhaseVerdict VerdictOf(DeliveryPhase phase)
    {
        ArgumentNullException.ThrowIfNull(phase);

        if (phase.Share is { } share && share < NothingToTuneShare)
        {
            // Two decimals, so a share just under 2% never reads as "2%: under 2%".
            var percent = (share * 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

            return new(DeliveryVerdict.NothingToTune, $"{percent}% of the run's cost: under 2%, so a cheaper configuration would save little");
        }

        if (phase.Configurations < 2)
        {
            return new(DeliveryVerdict.NotCompared, "Only one configuration has run this phase, so there is nothing to compare it with");
        }

        if (Lesson(string.Empty, phase) is not { } lesson)
        {
            return new(DeliveryVerdict.NoCall, "No two configurations have five priced runs each at different medians yet");
        }

        return lesson.RunDifferenceUsdMicros switch
        {
            null => new(DeliveryVerdict.NoCall, "The whole-run difference is unknown: too few fully priced runs on a side"),
            > 0 => new(DeliveryVerdict.WorthIt, $"{lesson.Dearer.Configuration.Label} costs more in the phase but lowers the median run cost"),
            < 0 when lesson.QualityWithin is true => new(DeliveryVerdict.TryCheaper, $"{lesson.Cheaper.Configuration.Label} lowers the run cost and is within 10% on the quality measure"),
            < 0 when lesson.QualityWithin is null => new(DeliveryVerdict.NoCall, "The cheaper configuration lowers the run cost, but its quality measure was not reported on both sides"),
            < 0 => new(DeliveryVerdict.NoCall, "The cheaper configuration lowers the run cost, but falls more than 10% short on the quality measure"),
            _ => new(DeliveryVerdict.NoCall, "Whole runs cost the same either way")
        };
    }

    /// <summary>
    /// Whether a card's configuration is the phase's configuration now: the same family
    /// and effort, and the same agent and mode wherever the phase's cards keep them. A
    /// field the phase dropped — every run shared it — is not compared; a field it kept
    /// is, so a card that ran in the owner session never matches an agent. Public so the
    /// page's "current" mark and <see cref="DeliveryLesson.KeepsNow"/> rest on one
    /// definition.
    /// </summary>
    public static bool Matches(DeliveryPhase phase, DeliveryConfiguration card)
    {
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(card);

        return phase.Now is { } now
               && card.Family == now.Family
               && card.Effort == now.Effort
               && (!phase.AgentKept || string.Equals(card.Agent, now.Agent, StringComparison.OrdinalIgnoreCase))
               && (!phase.ModeKept || card.Mode == now.Mode);
    }

    private static DeliveryPhase Phase(
        string name,
        IReadOnlyList<PricedDeliveryRun> runs,
        IReadOnlyList<IReadOnlyList<DeliveryRunStageExecution>> resolved,
        IReadOnlyList<(CostFigure? Figure, bool Partial)> totals)
    {
        var spelling = name;
        DeliveryConfiguration? now = null;
        var measure = MeasureOf(name);
        var found = new List<(DeliveryRun Run, DeliveryRunStage Stage, DeliveryConfiguration Configuration, CostFigure? Cost, (CostFigure? Figure, bool Partial) Total)>();

        for (var index = 0; index < runs.Count; index++)
        {
            var priced = runs[index];
            var stages = resolved[index];
            var first = true;

            for (var at = 0; at < priced.Run.Stages.Count && at < stages.Count; at++)
            {
                var stage = priced.Run.Stages[at];

                if (!string.Equals(stage.Name, name, StringComparison.OrdinalIgnoreCase)) continue;

                if (now is null)
                {
                    spelling = stage.Name;
                    now = NowOf(stages[at], priced.OwnerModel);
                }

                // A stage entered under the same name twice counts once, by its first
                // finished pass — the way the cost baseline matches it.
                if (!first || !DeliveryRunBaselines.Finished(stage.Status)) continue;

                first = false;
                var cost = at < priced.Costs.Count ? priced.Costs[at].Stage : null;
                found.Add((priced.Run, stage, ConfigurationOf(stages[at], priced.OwnerModel), cost, totals[index]));
            }
        }

        // The agent and the mode join the key only where they tell the phase's
        // configurations apart, so the cards do not split on what every run shared.
        var agents = found.Select(sample => sample.Configuration.Agent).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
        var modes = found.Select(sample => sample.Configuration.Mode).Distinct().Count() > 1;

        var samples = found
            .Select(sample => new DeliveryPhaseSample(
                sample.Run,
                sample.Configuration with
                {
                    Agent = agents ? sample.Configuration.Agent : null,
                    Mode = modes ? sample.Configuration.Mode : null
                },
                sample.Cost,
                sample.Total.Figure)
            {
                RunTotalPartial = sample.Total.Partial,
                Quality = SampleQuality(measure, sample.Run, sample.Stage),
                EscapedDefects = measure is DeliveryQualityMeasure.BlockersPerRound ? VerifyDefectsOf(sample.Run) : null
            })
            .ToList();

        var median = MedianOf([.. samples.Select(sample => sample.Cost).OfType<CostFigure>()]);

        // The share needs every phase's median, so Of fills it in once all are known.
        return new DeliveryPhase(spelling, now, median, null, samples)
        {
            AgentKept = agents,
            ModeKept = modes,
            SharedAgent = agents ? null : found.Select(sample => sample.Configuration.Agent).FirstOrDefault(),
            Measure = measure
        };
    }

    /// <summary>What the run configured for the stage, each field it left to the session
    /// filled from what ran; the mode is always what ran.</summary>
    private static DeliveryConfiguration NowOf(DeliveryRunStageExecution stage, string? ownerModel)
    {
        var ran = ConfigurationOf(stage, ownerModel);

        if (stage.Configured is not { } configured) return ran;

        return new DeliveryConfiguration(
            string.IsNullOrWhiteSpace(configured.Model) ? ran.Family : DeliveryRunStageResolution.Family(configured.Model),
            string.IsNullOrWhiteSpace(configured.Effort) ? ran.Effort : configured.Effort.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(configured.Agent) ? ran.Agent : configured.Agent.Trim(),
            ran.Mode);
    }

    /// <summary>Every stage name the runs carry, once each: the latest run's in its
    /// order, then each name only an older run carries, placed after the stage it
    /// followed in that run.</summary>
    private static List<string> PhaseNames(IEnumerable<PricedDeliveryRun> runs)
    {
        var names = new List<string>();

        foreach (var priced in runs)
        {
            var insertAt = 0;

            foreach (var stage in priced.Run.Stages)
            {
                var found = names.FindIndex(name => string.Equals(name, stage.Name, StringComparison.OrdinalIgnoreCase));

                if (found >= 0)
                {
                    insertAt = found + 1;
                    continue;
                }

                names.Insert(insertAt, stage.Name);
                insertAt++;
            }
        }

        return names;
    }

    /// <summary>The costs of the run's stages, as far as both lists go.</summary>
    private static IEnumerable<CostFigure?> StageCosts(PricedDeliveryRun priced) =>
        priced.Costs.Take(priced.Run.Stages.Count).Select(cost => cost.Stage);

    /// <summary>The run's whole cost: the sum over the stages it finished that were
    /// priced, and whether a stage it finished was left out unpriced. Null where none
    /// was priced. A run with one stage unpriced keeps the figure for the rest rather
    /// than dropping out, because with recorded runs most have one; the surfaces say how
    /// many figures stand on part of a run.</summary>
    private static (CostFigure? Figure, bool Partial) Total(PricedDeliveryRun priced)
    {
        CostFigure? total = null;
        var partial = false;

        for (var index = 0; index < priced.Run.Stages.Count; index++)
        {
            if (!DeliveryRunBaselines.Finished(priced.Run.Stages[index].Status)) continue;

            if (index >= priced.Costs.Count || priced.Costs[index].Stage is not { } cost)
            {
                partial = true;
                continue;
            }

            total = total is null ? cost : total + cost;
        }

        return (total, partial);
    }

    /// <summary>The median of the figures, estimated where any of them is, or null for
    /// none.</summary>
    private static CostFigure? MedianOf(IReadOnlyList<CostFigure> figures) =>
        DeliveryRunBaselines.Median(figures.Select(figure => figure.UsdMicros)) is { } median
            ? new CostFigure((long)Math.Round(median, MidpointRounding.AwayFromZero), SourceOf(figures))
            : null;

    /// <summary>The figure at the percentile by nearest rank, estimated where any of the
    /// figures is, or null for none.</summary>
    private static CostFigure? PercentileOf(IReadOnlyList<CostFigure> figures, double percentile)
    {
        if (figures.Count == 0) return null;

        var sorted = figures.Select(figure => figure.UsdMicros).Order().ToList();
        var rank = Math.Clamp((int)Math.Ceiling(percentile * sorted.Count) - 1, 0, sorted.Count - 1);

        return new CostFigure(sorted[rank], SourceOf(figures));
    }

    private static CostSource SourceOf(IEnumerable<CostFigure> figures) =>
        figures.Any(figure => figure.Estimated) ? CostSource.Estimated : CostSource.Reported;

    /// <summary>Where a run is placed in time: its start, else its last update.</summary>
    private static DateTimeOffset At(DeliveryRun run) => run.StartedAt ?? run.UpdatedAt;

    private static IEnumerable<DeliveryRun> Inside(IEnumerable<DeliveryRun> catalog, TimeSpan window, DateTimeOffset now)
    {
        var from = now - window;

        return catalog.Where(run => DeliveryRunBaselines.Finished(run.Status) && At(run) >= from && At(run) <= now);
    }
}
