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

    /// <summary>The quality measure of the phase's work in this run, or null where the
    /// run reported none. No delivery engine reports the facts it is built from yet, so
    /// nothing sets it: it waits for an engine release that does.</summary>
    public double? Quality { get; init; }
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
/// <param name="Quality">The median quality its runs reported, or null where none did —
/// which, until the delivery engine reports quality, is always.</param>
/// <param name="QualityRuns">How many runs <paramref name="Quality"/> stands on.</param>
public sealed record DeliveryConfigurationCost(
    DeliveryConfiguration Configuration,
    int Runs,
    int PricedRuns,
    CostFigure? MedianPhase,
    CostFigure? MedianRun,
    int TotalRuns,
    double? Quality,
    int QualityRuns)
{
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
/// One lesson card: two configurations of a phase compared on cost. Quality joins it
/// once the delivery engine reports it; until then the card says it compares cost only
/// and claims nothing about quality.
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
/// <param name="KeepsNow">Whether the cheaper configuration is the configuration now: the
/// card then confirms it, and copies no request, since there is nothing to change.</param>
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
                var more => $"{head}, but whole runs cost {Amount(more.Value)} more"
            };
        }
    }

    /// <summary>Both sample sizes, such as "Sonnet at high effort: 6 runs · Opus: 5
    /// runs" — a lesson from five runs must not read like one from forty.</summary>
    public string Samples =>
        $"{Cheaper.Configuration.Label}: {Cheaper.PricedRuns} runs · {Dearer.Configuration.Label}: {Dearer.PricedRuns} runs";

    /// <summary>What the card says about quality, which it never compares.</summary>
    public string QualityNote => Cheaper.QualityRuns == 0 || Dearer.QualityRuns == 0
        ? "Quality not reported: this compares cost only."
        : "Quality not compared: this compares cost only.";

    /// <summary>The request the card's button copies, for the person to paste into a
    /// session: the <c>devbook-config:local</c> skill, the flow, the phase and the
    /// configuration. Null where the cheaper is the configuration now: setting what is
    /// already set changes nothing. The app never writes the overlay itself.</summary>
    public string? Request
    {
        get
        {
            if (KeepsNow) return null;

            var run = RunDifferenceUsdMicros switch
            {
                null => "; the whole-run difference is unknown",
                < 0 and var less => $", and the whole run {Amount(-less)} cheaper",
                0 => ", and the whole run the same",
                var more => $", and the whole run {Amount(more.Value)} dearer"
            };

            return $"Use devbook-config:local to set the {Phase} phase of {SkillId} to {Cheaper.Configuration.Label} in my personal overlay. "
                   + $"The Delivery economics page found it {Amount(PhaseSavingUsdMicros)} cheaper per run of that phase than {Dearer.Configuration.Label}, "
                   + $"over {Cheaper.PricedRuns} and {Dearer.PricedRuns} priced runs{run}. Quality was not compared.";
        }
    }

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

        return new DeliveryEconomicsReport(ordered.Count, reported, estimated, medianRun, PercentileOf(whole, 0.9), phases)
        {
            TotalRuns = whole.Count,
            PartialRuns = totals.Count(total => total is { Figure: not null, Partial: true })
        };
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
                    var quality = samples.Select(sample => sample.Quality).OfType<double>().Order().ToList();

                    return new DeliveryConfigurationCost(
                        group.Key,
                        samples.Count,
                        samples.Count(sample => sample.Cost is not null),
                        MedianOf([.. samples.Select(sample => sample.Cost).OfType<CostFigure>()]),
                        MedianOf(totals),
                        totals.Count,
                        quality.Count == 0 ? null : quality.Count % 2 == 1 ? quality[quality.Count / 2] : (quality[(quality.Count / 2) - 1] + quality[quality.Count / 2]) / 2,
                        quality.Count)
                    {
                        PartialRuns = samples.Count(sample => sample.RunTotal is not null && sample.RunTotalPartial),
                        MedianCompleteRun = MedianOf(complete),
                        CompleteRuns = complete.Count
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
    /// qualifies, else with the other that priced most runs. Cost only: quality is not
    /// reported, so it is not compared.
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

        var keepsNow = Matches(phase, cheaper.Configuration);

        return new DeliveryLesson(skillId, phase.Name, cheaper, dearer, saving, difference, keepsNow);
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
        var found = new List<(DeliveryRun Run, DeliveryConfiguration Configuration, CostFigure? Cost, (CostFigure? Figure, bool Partial) Total)>();

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
                found.Add((priced.Run, ConfigurationOf(stages[at], priced.OwnerModel), cost, totals[index]));
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
                RunTotalPartial = sample.Total.Partial
            })
            .ToList();

        var median = MedianOf([.. samples.Select(sample => sample.Cost).OfType<CostFigure>()]);

        // The share needs every phase's median, so Of fills it in once all are known.
        return new DeliveryPhase(spelling, now, median, null, samples)
        {
            AgentKept = agents,
            ModeKept = modes
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
