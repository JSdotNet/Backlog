namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>Where a cost figure came from.</summary>
public enum CostSource
{
    /// <summary>Summed from the <c>api_request</c> events Claude Code reported, each
    /// carrying the price Claude Code applied.</summary>
    Reported,

    /// <summary>Worked out from the tokens the run counted and the rates the person
    /// entered in Settings — "estimated from tokens".</summary>
    Estimated
}

/// <summary>
/// What something cost, in millionths of a US dollar, and where the figure came from.
/// <para>
/// There is no "no cost" value: where neither telemetry nor a price answers, the figure
/// is absent — a null <see cref="CostFigure"/> — and a surface shows nothing rather than
/// a zero nobody measured.
/// </para>
/// </summary>
/// <param name="UsdMicros">The cost in micro-dollars, whole, so a sum does not drift.</param>
/// <param name="Source">Reported by Claude Code, or estimated from tokens.</param>
public sealed record CostFigure(long UsdMicros, CostSource Source)
{
    /// <summary>The words an estimated figure carries wherever it is shown.</summary>
    public const string EstimatedNote = "estimated from tokens";

    /// <summary>Whether the figure was worked out from tokens rather than reported.</summary>
    public bool Estimated => Source is CostSource.Estimated;

    /// <summary>The figure in US dollars.</summary>
    public decimal Usd => UsdMicros / 1_000_000m;

    /// <summary>
    /// The figure as a reader sees it: two decimals from a cent up, four below one, and
    /// "&lt; $0.0001" for a sliver that would otherwise round to the zero this type
    /// never shows.
    /// </summary>
    public string Label => UsdMicros switch
    {
        >= 10_000 => Usd.ToString("$#,0.00", System.Globalization.CultureInfo.InvariantCulture),
        >= 100 => Usd.ToString("$0.0000", System.Globalization.CultureInfo.InvariantCulture),
        _ => "< $0.0001"
    };

    /// <summary>Two figures added: estimated as soon as either part is.</summary>
    public static CostFigure operator +(CostFigure left, CostFigure right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        return new(left.UsdMicros + right.UsdMicros, left.Estimated || right.Estimated ? CostSource.Estimated : CostSource.Reported);
    }

    /// <summary>The sum of the figures that are there, or null where none is.</summary>
    public static CostFigure? Sum(IEnumerable<CostFigure?> figures)
    {
        ArgumentNullException.ThrowIfNull(figures);

        CostFigure? total = null;

        foreach (var figure in figures)
        {
            if (figure is null) continue;

            total = total is null ? figure : total + figure;
        }

        return total;
    }
}

/// <summary>
/// One model's rates, in US dollars per million tokens — the unit Anthropic quotes its
/// prices in. A rate left empty is unknown, not free.
/// </summary>
/// <param name="Model">The model as the person typed it: an id such as
/// <c>claude-opus-5-5</c>, a name such as "Opus 5.5", or a family alias such as
/// <c>opus</c>.</param>
/// <param name="InputPerMTok">Uncached input.</param>
/// <param name="OutputPerMTok">Output.</param>
/// <param name="CacheReadPerMTok">Input read from the prompt cache.</param>
/// <param name="CacheWritePerMTok">Input written to the prompt cache.</param>
public sealed record ModelPrice(
    string Model,
    decimal? InputPerMTok,
    decimal? OutputPerMTok,
    decimal? CacheReadPerMTok,
    decimal? CacheWritePerMTok);

/// <summary>
/// The rates the person entered in Settings, which price a run Claude Code reported no
/// cost for. Empty until somebody fills it in: this product ships no price list, because
/// one it shipped would be out of date the day a price changed.
/// </summary>
public sealed record ModelPriceTable(IReadOnlyList<ModelPrice> Prices)
{
    /// <summary>The table nobody has filled in.</summary>
    public static ModelPriceTable Empty { get; } = new([]);

    /// <summary>
    /// The rates for a model as a run or a request names it: the entry naming the same
    /// id, then the one naming the same model in other words ("Opus 5.5" for
    /// <c>claude-opus-5-5</c>), then the first naming its family (<c>opus</c>) — or null
    /// where none does. A context suffix such as <c>[1m]</c> is ignored.
    /// </summary>
    public ModelPrice? PriceFor(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return null;

        var id = Bare(model);
        var label = AgentModels.Label(id);
        var family = DeliveryRunStageResolution.Family(id);

        return Prices.FirstOrDefault(price => string.Equals(Bare(price.Model), id, StringComparison.OrdinalIgnoreCase))
            ?? Prices.FirstOrDefault(price => string.Equals(AgentModels.Label(Bare(price.Model)), label, StringComparison.OrdinalIgnoreCase))
            ?? Prices.FirstOrDefault(price => !string.IsNullOrWhiteSpace(price.Model) && DeliveryRunStageResolution.Family(Bare(price.Model)) == family);
    }

    /// <summary>
    /// What the tokens cost at the model's rates, or null where the table has no entry
    /// for the model, where a kind of token was spent whose rate is empty, or where no
    /// token was spent at all — none of which is a cost of zero.
    /// </summary>
    public CostFigure? Estimate(string? model, long input, long output, long cacheRead, long cacheWrite)
    {
        if (input + output + cacheRead + cacheWrite <= 0) return null;
        if (PriceFor(model) is not { } price) return null;

        decimal micros = 0;

        foreach (var (tokens, rate) in new[]
                 {
                     (input, price.InputPerMTok),
                     (output, price.OutputPerMTok),
                     (cacheRead, price.CacheReadPerMTok),
                     (cacheWrite, price.CacheWritePerMTok)
                 })
        {
            if (tokens <= 0) continue;
            if (rate is not { } perMillion) return null;

            // Dollars per million tokens is micro-dollars per token.
            micros += tokens * perMillion;
        }

        return new CostFigure((long)Math.Round(micros, MidpointRounding.AwayFromZero), CostSource.Estimated);
    }

    private static string Bare(string model)
    {
        var trimmed = model.Trim();
        var cut = trimmed.IndexOf('[', StringComparison.Ordinal);

        return cut > 0 ? trimmed[..cut].Trim() : trimmed;
    }
}

/// <summary>
/// Where the person's <see cref="ModelPriceTable"/> is kept: this machine's local
/// database, beside the requests it prices the absence of.
/// </summary>
public interface IModelPriceStore
{
    /// <summary>The table as last saved, or <see cref="ModelPriceTable.Empty"/>.</summary>
    Task<ModelPriceTable> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the table with this one.</summary>
    Task SaveAsync(ModelPriceTable table, CancellationToken cancellationToken = default);
}

/// <summary>
/// What one stage of a delivery run cost, and what each of its sub-agent calls did, in
/// the order <see cref="DeliveryRunStageExecution.Workers"/> lists them.
/// </summary>
/// <param name="Stage">The stage as a whole, owner session and sub-agents together, or
/// null where neither source answers.</param>
/// <param name="Workers">One figure per worker, null where neither source answers.</param>
public sealed record DeliveryRunStageCost(CostFigure? Stage, IReadOnlyList<CostFigure?> Workers)
{
    /// <summary>A stage nothing could price.</summary>
    public static DeliveryRunStageCost None(int workers) => new(null, [.. Enumerable.Repeat<CostFigure?>(null, workers)]);
}

/// <summary>
/// Attributes what Claude Code reported a model request cost to the session, the
/// delivery-run stage and the sub-agent call that made it — and, where it reported
/// nothing, estimates the cost from the run's own token counts and the person's
/// <see cref="ModelPriceTable"/>.
/// <list type="bullet">
/// <item><b>Session.</b> Every stored request of the session, summed.</item>
/// <item><b>Stage.</b> The requests of the run's owner session — every session the run
/// names, or the session it was joined to by worktree and time — made between the
/// stage's start and its completion by the main loop; plus the requests a sub-agent made
/// whose <c>agent.name</c>, or the name in a <c>query_source</c> of <c>agent:&lt;name&gt;</c>,
/// matches one of the stage's workers, within the stage's window or within that worker's
/// own call. A stage still under way has no end yet.</item>
/// <item><b>Sub-agent call.</b> The matching sub-agent's requests between the call's
/// start — its end less its duration — and its end; where the call recorded no end, and
/// it is the stage's only call of that agent, the agent's requests within the stage.</item>
/// <item><b>Estimate.</b> Where no request answers, the stage's token counts: the owner
/// session's share at the owner's model, the sub-agents' share at theirs; and a
/// sub-agent call's share of the stage's sub-agent tokens, in proportion to the tokens
/// it reported, at its own model.</item>
/// </list>
/// A clock tolerance of <see cref="Tolerance"/> either side of a sub-agent call absorbs
/// the gap between when Claude Code stamped a request and when the hook stamped the call.
/// </summary>
public static class DeliveryRunCosts
{
    /// <summary>How far either side of a sub-agent call a request may fall and still be
    /// that call's.</summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(2);

    /// <summary>
    /// What a session cost: the sum Claude Code reported for it, or with no report, an
    /// estimate — the session's own tokens per model, from its transcript, plus the
    /// sub-agents' share of each of its runs' stages; where the session carries no token
    /// counts of its own, its runs' whole stages. Null where neither answers, or where
    /// any part that spent tokens could not be priced, since a partial sum would read as
    /// the whole.
    /// </summary>
    /// <param name="sessionId">The session.</param>
    /// <param name="requests">The stored requests — more is fine, they are filtered to
    /// the session's own.</param>
    /// <param name="runs">The runs the session drove.</param>
    /// <param name="prices">The person's rates.</param>
    /// <param name="ownerModel">The session's model, for a stage that recorded none.</param>
    /// <param name="usage">The session's own spend per model, from its transcript, or
    /// null where none was read. Its own only: the sub-agents it spawned write theirs
    /// elsewhere, which is why their share comes from the runs.</param>
    public static CostFigure? Session(
        string? sessionId,
        IReadOnlyList<ClaudeApiRequest> requests,
        IReadOnlyList<DeliveryRun> runs,
        ModelPriceTable prices,
        string? ownerModel,
        IReadOnlyList<AgentModelUsage>? usage = null)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(prices);

        if (sessionId is not null && Reported(requests.Where(request => request.SessionId == sessionId)) is { } reported)
        {
            return reported;
        }

        var own = usage is { Count: > 0 };
        var parts = new List<CostFigure?>();

        if (own)
        {
            foreach (var model in usage!)
            {
                var spent = model.InputTokens + model.OutputTokens + model.CacheReadInputTokens + model.CacheCreationInputTokens > 0;
                var estimate = prices.Estimate(model.Model, model.InputTokens, model.OutputTokens, model.CacheReadInputTokens, model.CacheCreationInputTokens);

                if (estimate is null && spent) return null;

                parts.Add(estimate);
            }
        }

        foreach (var run in runs)
        {
            var resolved = DeliveryRunStageResolution.Of(run);

            for (var index = 0; index < resolved.Count && index < run.Stages.Count; index++)
            {
                if (EstimateParts(run, run.Stages[index], resolved[index], prices, ownerModel) is not { } stage) return null;

                // The owner's share is already in the session's own usage where it has one.
                parts.Add(own ? stage.SubAgent : CostFigure.Sum([stage.Owner, stage.SubAgent]));
            }
        }

        return CostFigure.Sum(parts);
    }

    /// <summary>
    /// What each stage of a run cost, in stage order — reported where the owner session's
    /// requests answer for the stage, estimated from tokens where they do not.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="requests">The stored requests of the run's sessions — more is fine,
    /// they are filtered to the run's own.</param>
    /// <param name="prices">The person's rates.</param>
    /// <param name="sessionId">The session the run was joined to, used where the run names
    /// none of its own.</param>
    /// <param name="ownerModel">The owner session's model, for the owner's share of an
    /// estimate where the stage recorded none.</param>
    public static IReadOnlyList<DeliveryRunStageCost> Of(
        DeliveryRun run,
        IReadOnlyList<ClaudeApiRequest> requests,
        ModelPriceTable prices,
        string? sessionId = null,
        string? ownerModel = null)
    {
        ArgumentNullException.ThrowIfNull(run);

        return Of(run, DeliveryRunStageResolution.Of(run), requests, prices, sessionId, ownerModel);
    }

    /// <summary>The same, over stages already resolved.</summary>
    public static IReadOnlyList<DeliveryRunStageCost> Of(
        DeliveryRun run,
        IReadOnlyList<DeliveryRunStageExecution> stages,
        IReadOnlyList<ClaudeApiRequest> requests,
        ModelPriceTable prices,
        string? sessionId = null,
        string? ownerModel = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(prices);

        var sessions = run.SessionIds.Count > 0
            ? new HashSet<string>(run.SessionIds, StringComparer.Ordinal)
            : sessionId is null ? [] : new HashSet<string>([sessionId], StringComparer.Ordinal);
        var own = requests.Where(request => request.SessionId is { } id && sessions.Contains(id)).ToList();

        var costs = new List<DeliveryRunStageCost>(stages.Count);

        for (var index = 0; index < stages.Count && index < run.Stages.Count; index++)
        {
            costs.Add(Stage(run, run.Stages[index], stages[index], own, prices, ownerModel));
        }

        return costs;
    }

    /// <summary>The requests a stage's window and its workers match, deduplicated — the
    /// seam the tests hold the matching to.</summary>
    public static IReadOnlyList<ClaudeApiRequest> StageRequests(
        DeliveryRunStage stage,
        DeliveryRunStageExecution resolved,
        IReadOnlyList<ClaudeApiRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(requests);

        if (!WindowIsWhole(stage) || stage.StartedAt is not { } start) return [];

        var end = stage.CompletedAt;
        var matched = new Dictionary<string, ClaudeApiRequest>(StringComparer.Ordinal);

        foreach (var request in requests)
        {
            var inWindow = request.Timestamp >= start && (end is null || request.Timestamp <= end);

            if (SubAgentOf(request) is not { } agent)
            {
                if (inWindow) matched.TryAdd(request.RequestId, request);
                continue;
            }

            foreach (var worker in resolved.Workers)
            {
                if (!SameAgent(agent, worker.Agent)) continue;

                if (inWindow || InCall(request, worker))
                {
                    matched.TryAdd(request.RequestId, request);
                    break;
                }
            }
        }

        return [.. matched.Values.OrderBy(request => request.Timestamp)];
    }

    /// <summary>The requests one sub-agent call made.</summary>
    public static IReadOnlyList<ClaudeApiRequest> WorkerRequests(
        DeliveryRunStage stage,
        DeliveryRunStageExecution resolved,
        int worker,
        IReadOnlyList<ClaudeApiRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(requests);

        if (worker < 0 || worker >= resolved.Workers.Count) return [];

        var call = resolved.Workers[worker];
        var mine = requests.Where(request => SubAgentOf(request) is { } agent && SameAgent(agent, call.Agent));

        if (call.EndedAt is not null && call.DurationMs is not null)
        {
            // Two calls of one agent whose times overlap - a fan-out, or two back to back
            // inside the clock tolerance - cannot be told apart by time, and each would
            // claim the other's requests. Neither claims any; each falls back to its
            // estimate.
            var overlapped = resolved.Workers
                .Where((other, index) => index != worker && SameAgent(other.Agent, call.Agent))
                .Any(other => Overlap(call, other));

            return overlapped ? [] : [.. mine.Where(request => InCall(request, call))];
        }

        // No end to place the call by: only the stage's one call of that agent can claim
        // the agent's requests in the stage, since two calls would each claim the other's.
        var calls = resolved.Workers.Count(other => SameAgent(other.Agent, call.Agent));

        if (calls != 1 || !WindowIsWhole(stage) || stage.StartedAt is not { } start) return [];

        return [.. mine.Where(request => request.Timestamp >= start && (stage.CompletedAt is not { } end || request.Timestamp <= end))];
    }

    private static DeliveryRunStageCost Stage(
        DeliveryRun run,
        DeliveryRunStage stage,
        DeliveryRunStageExecution resolved,
        IReadOnlyList<ClaudeApiRequest> own,
        ModelPriceTable prices,
        string? ownerModel)
    {
        var workers = new List<CostFigure?>(resolved.Workers.Count);

        for (var index = 0; index < resolved.Workers.Count; index++)
        {
            workers.Add(Reported(WorkerRequests(stage, resolved, index, own)) ?? EstimateWorker(run, stage, resolved, index, prices));
        }

        var figure = Reported(StageRequests(stage, resolved, own)) ?? EstimateStage(run, stage, resolved, prices, ownerModel);

        return new DeliveryRunStageCost(figure, workers);
    }

    /// <summary>The reported sum, or null where no request carried a cost.</summary>
    private static CostFigure? Reported(IEnumerable<ClaudeApiRequest> requests)
    {
        long? micros = null;

        foreach (var request in requests)
        {
            if (request.CostUsdMicros is { } cost) micros = (micros ?? 0) + cost;
        }

        return micros is { } total ? new CostFigure(total, CostSource.Reported) : null;
    }

    private static DeliveryRunStageTokens? TokensOf(DeliveryRun run, DeliveryRunStage stage) =>
        run.TokenUsage?.ByStage.FirstOrDefault(usage => string.Equals(usage.StageName, stage.Name, StringComparison.Ordinal));

    private static bool Spent(DeliveryRunTokens tokens) =>
        tokens.InputTokens + tokens.OutputTokens + tokens.CacheReadTokens + tokens.CacheWriteTokens > 0;

    /// <summary>
    /// The stage's tokens at the person's rates: the owner session's share — the total
    /// less the sub-agents' — at the stage's model where it ran inline, else the owner's;
    /// the sub-agents' share at the one model its calls ran on, else the stage's. Null
    /// where a share that spent tokens has no price.
    /// </summary>
    private static CostFigure? EstimateStage(
        DeliveryRun run,
        DeliveryRunStage stage,
        DeliveryRunStageExecution resolved,
        ModelPriceTable prices,
        string? ownerModel) =>
        EstimateParts(run, stage, resolved, prices, ownerModel) is { } parts ? CostFigure.Sum([parts.Owner, parts.SubAgent]) : null;

    /// <summary>A stage's estimate in its two shares, each null where it spent nothing;
    /// null as a whole where a share that spent tokens has no price.</summary>
    private static (CostFigure? Owner, CostFigure? SubAgent)? EstimateParts(
        DeliveryRun run,
        DeliveryRunStage stage,
        DeliveryRunStageExecution resolved,
        ModelPriceTable prices,
        string? ownerModel)
    {
        if (TokensOf(run, stage) is not { } usage || !Spent(usage.Total)) return (null, null);

        var sub = usage.SubAgent;
        var owner = new DeliveryRunTokens(
            0,
            Math.Max(0, usage.Total.InputTokens - sub.InputTokens),
            Math.Max(0, usage.Total.OutputTokens - sub.OutputTokens),
            0,
            Math.Max(0, usage.Total.CacheReadTokens - sub.CacheReadTokens),
            Math.Max(0, usage.Total.CacheWriteTokens - sub.CacheWriteTokens));

        var ownerAt = resolved.Mode is DeliveryRunStageMode.Inline ? resolved.Model ?? ownerModel : ownerModel;
        var models = resolved.Workers.Select(worker => worker.Model).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        CostFigure? ownerPart = null;

        if (Spent(owner))
        {
            ownerPart = prices.Estimate(ownerAt, owner.InputTokens, owner.OutputTokens, owner.CacheReadTokens, owner.CacheWriteTokens);

            if (ownerPart is null) return null;
        }

        CostFigure? subPart = null;

        if (Spent(sub))
        {
            if (models.Count > 1)
            {
                // Calls on several models: each call's share at its own model, which
                // needs every call to have reported its tokens.
                var shares = Enumerable.Range(0, resolved.Workers.Count).Select(index => EstimateWorker(run, stage, resolved, index, prices)).ToList();

                subPart = shares.Any(share => share is null) ? null : CostFigure.Sum(shares);
            }
            else
            {
                var subAt = models.Count == 1 ? models[0] : resolved.Model ?? ownerModel;

                subPart = prices.Estimate(subAt, sub.InputTokens, sub.OutputTokens, sub.CacheReadTokens, sub.CacheWriteTokens);
            }

            if (subPart is null) return null;
        }

        return (ownerPart, subPart);
    }

    /// <summary>
    /// A sub-agent call's share of the stage's sub-agent tokens, by the tokens it
    /// reported against every call's in the stage, at its own model. Null where the call
    /// reported no tokens or no model, or the stage counted no sub-agent tokens.
    /// </summary>
    private static CostFigure? EstimateWorker(
        DeliveryRun run,
        DeliveryRunStage stage,
        DeliveryRunStageExecution resolved,
        int index,
        ModelPriceTable prices)
    {
        var worker = resolved.Workers[index];

        if (worker.Tokens is not { } tokens || tokens <= 0 || worker.Model is null) return null;
        if (TokensOf(run, stage) is not { } usage || !Spent(usage.SubAgent)) return null;

        var all = resolved.Workers.Sum(other => other.Tokens ?? 0);

        if (all <= 0) return null;

        var share = (decimal)tokens / all;
        var sub = usage.SubAgent;

        return prices.Estimate(
            worker.Model,
            Share(sub.InputTokens, share),
            Share(sub.OutputTokens, share),
            Share(sub.CacheReadTokens, share),
            Share(sub.CacheWriteTokens, share));
    }

    private static long Share(long tokens, decimal share) => (long)Math.Round(tokens * share, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The sub-agent a request was made by — <c>agent.name</c>, or the name after
    /// <c>agent:</c> in its <c>query_source</c> — or null for the main loop.
    /// </summary>
    public static string? SubAgentOf(ClaudeApiRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.IsNullOrWhiteSpace(request.AgentName)) return request.AgentName.Trim();

        return request.QuerySource is { } source && source.StartsWith("agent:", StringComparison.OrdinalIgnoreCase)
            ? source["agent:".Length..].Trim()
            : null;
    }

    /// <summary>
    /// Whether the stage's stamped window covers all of it. The writer restamps a
    /// stage's start on every pass, so a stage re-entered after requested changes keeps
    /// only its latest pass's window, and its earlier passes' requests would be nobody's:
    /// a reported figure over that window would read as the whole stage and be part of
    /// it. Such a stage is estimated from its tokens, which the run counts over every
    /// pass.
    /// </summary>
    private static bool WindowIsWhole(DeliveryRunStage stage) =>
        stage.DoneCount <= 1 && !(stage.DoneCount == 1 && stage.CompletedAt is null);

    /// <summary>Whether two calls' times overlap, within the clock tolerance; a call
    /// that recorded no end could have been any time, so it overlaps.</summary>
    private static bool Overlap(DeliveryRunStageWorker one, DeliveryRunStageWorker other)
    {
        if (one.EndedAt is not { } oneEnd || one.DurationMs is not { } oneMs) return false;
        if (other.EndedAt is not { } otherEnd || other.DurationMs is not { } otherMs) return true;

        var oneStart = oneEnd - TimeSpan.FromMilliseconds(oneMs) - Tolerance;
        var otherStart = otherEnd - TimeSpan.FromMilliseconds(otherMs) - Tolerance;

        return oneStart <= otherEnd + Tolerance && otherStart <= oneEnd + Tolerance;
    }

    private static bool InCall(ClaudeApiRequest request, DeliveryRunStageWorker worker) =>
        worker.EndedAt is { } ended
        && worker.DurationMs is { } duration
        && request.Timestamp >= ended - TimeSpan.FromMilliseconds(duration) - Tolerance
        && request.Timestamp <= ended + Tolerance;

    /// <summary>
    /// Whether a request's agent is a stage's worker: the same name, or one the other
    /// with a plugin prefix — <c>architect</c> is <c>architecture:architect</c>.
    /// </summary>
    private static bool SameAgent(string requested, string worker) =>
        string.Equals(requested, worker, StringComparison.OrdinalIgnoreCase)
        || worker.EndsWith($":{requested}", StringComparison.OrdinalIgnoreCase)
        || requested.EndsWith($":{worker}", StringComparison.OrdinalIgnoreCase);
}
