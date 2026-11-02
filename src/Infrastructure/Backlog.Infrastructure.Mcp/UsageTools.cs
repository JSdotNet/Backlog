using System.ComponentModel;

using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel.Results;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// What the delivery runs on this machine spent, measured — the figures <c>get_run</c>
/// leaves out on purpose.
/// <para>
/// <b>Its own tool, not a field on <c>get_run</c>.</b> <see cref="RunPayload"/> is what a
/// flow reads back about the run it is driving, and it carries no token figure because a
/// figure handed to the author of a run invites it into the run's summary as if the run
/// had claimed it. A report about many runs, written after the fact by a session that
/// drove none of them, is the reader those figures are for. So they reach it here, by a
/// tool whose name says what it answers, and never through the surface's own read.
/// </para>
/// <para>
/// <b>Every run on the machine, every repository.</b> A run belongs to a worktree, and the
/// surface tools take one; a usage report asks across all of them, so this takes a window
/// instead. Each run is placed in a repository by what it named — its own
/// <c>repo</c>, else the session that drove it, else the work it was linked to — and a run
/// nothing places is answered with a null repository rather than dropped.
/// </para>
/// <para>
/// <b>Measured, never worked out.</b> The totals, the stages and the sub-agents are what
/// the run file recorded from the session's own transcript. The per-model figures are
/// what Claude Code itself reported in its <c>api_request</c> telemetry for the run's
/// sessions while the run was open; where none arrived, the run's models are named with
/// no figures beside them. A figure nothing recorded is null, never zero, which is the
/// rule every other measured figure in this context follows.
/// </para>
/// <para>
/// Its own group (<see cref="BacklogMcpTools.Usage"/>) behind the sessions key: what it
/// reads is the run files and sessions the Sessions area shows, so switching that area
/// off withdraws this as well. A class of its own so that its three ports cost nothing to
/// the other session tools.
/// </para>
/// </summary>
/// <param name="clock">Where "now" comes from — for a window counted back in days, and for
/// the end of a run still under way. Optional, as on <see cref="RoadmapTools"/>.</param>
[McpServerToolType]
public sealed class UsageTools(
    IDeliveryRunSource runs,
    IAgentSessionSource sessions,
    IClaudeApiRequestStore requests,
    TimeProvider? clock = null)
{
    internal const string ListRunUsage = "list_run_usage";

    /// <summary>The window when nothing is asked: the week the cost analysis reads.</summary>
    internal const int DefaultDays = 7;

    /// <summary>The longest window counted in days — a year and a day, so a leap year fits.</summary>
    internal const int MaxDays = 366;

    [McpServerTool(Name = ListRunUsage, Title = "List the token usage of delivery runs", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Every delivery run this machine recorded whose start falls in a window, across every repository, with "
        + "its measured token usage: totals and uncached totals, per stage, per sub-agent, and per model where "
        + "Claude Code reported its requests; the context peak and compactions; and a total per repository. "
        + "Uncached is input plus cache writes plus output - the prompt cache's reads left out. A figure nothing "
        + "recorded is null, never zero. Read-only.")]
    public async Task<UsagePayload> ListRunUsageAsync(
        [Description("Optional. How many days back from now the window reaches; 7 when omitted. Ignored when from is given.")]
        int? days = null,
        [Description("Optional. The window's first day, yyyy-MM-dd, in UTC. With it, the window runs through to.")]
        DateOnly? from = null,
        [Description("Optional, with from. The window's last day, inclusive, yyyy-MM-dd, in UTC; today when omitted.")]
        DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var (start, end) = Window(days, from, to, now).ValueOrThrow();

        var catalog = await runs.GetRunsAsync(cancellationToken).ConfigureAwait(false);

        var inside = catalog.Runs
            .Where(run => PlacedAt(run) >= start && PlacedAt(run) < end)
            .ToList();

        // The session that drove each run, where the list holds it: the same join the
        // pane makes, so a run is placed here in the repository its row shows there.
        var owners = new Dictionary<DeliveryRun, AgentSession>(ReferenceEqualityComparer.Instance);

        if (inside.Count > 0)
        {
            var known = await sessions.GetSessionsAsync(cancellationToken).ConfigureAwait(false);

            foreach (var row in SessionRows.Of(known.Sessions, inside))
            {
                if (row.Session is not { } session) continue;

                foreach (var run in row.Runs) owners[run] = session;
            }
        }

        var reported = new Dictionary<string, IReadOnlyList<ClaudeApiRequest>>(StringComparer.Ordinal);
        var answered = new List<UsageRunPayload>(inside.Count);

        // A request is one run's, never two: the latest-started run whose span holds it
        // takes it, so a session that drove several runs is not counted once per run.
        var claimed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var run in inside.OrderByDescending(PlacedAt))
        {
            var owner = owners.GetValueOrDefault(run);
            var sessionIds = run.SessionIds.Count > 0 ? run.SessionIds : owner is null ? [] : [owner.Id];
            var (opened, closed) = Span(run, sessionIds, catalog.Runs, now);
            var during = new List<ClaudeApiRequest>();

            foreach (var sessionId in sessionIds.Distinct(StringComparer.Ordinal))
            {
                if (!reported.TryGetValue(sessionId, out var ofSession))
                {
                    reported[sessionId] = ofSession =
                        await requests.ListAsync(sessionId, cancellationToken).ConfigureAwait(false);
                }

                during.AddRange(ofSession.Where(request =>
                    request.Timestamp >= opened
                    && request.Timestamp <= closed
                    && claimed.Add(request.RequestId)));
            }

            answered.Add(RunUsage.Of(run, RepositoryOf(run, owner), during));
        }

        var ordered = answered
            .OrderBy(run => run.Repository is null)
            .ThenBy(run => run.Repository, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(run => run.StartedAt ?? run.UpdatedAt)
            .ToList();

        return new UsagePayload(start, end, ordered.Count, catalog.Unreadable, RunUsage.ByRepository(ordered), ordered);
    }

    /// <summary>
    /// The window asked for, as a half-open span: <paramref name="from"/> through
    /// <paramref name="to"/> as whole UTC days, or the last <paramref name="days"/> days to
    /// <paramref name="now"/>.
    /// </summary>
    internal static Result<(DateTimeOffset Start, DateTimeOffset End)> Window(int? days, DateOnly? from, DateOnly? to, DateTimeOffset now)
    {
        if (from is { } first)
        {
            var last = to ?? DateOnly.FromDateTime(now.UtcDateTime);

            if (last < first)
            {
                return Error.Validation("usage.window_reversed", $"The window ends on {last:yyyy-MM-dd}, before it starts on {first:yyyy-MM-dd}.");
            }

            return (Midnight(first), Midnight(last.AddDays(1)));
        }

        if (to is not null)
        {
            return Error.Validation("usage.to_without_from", "A window's last day needs its first: pass from with to, or days alone.");
        }

        var span = days ?? DefaultDays;

        if (span is < 1 or > MaxDays)
        {
            return Error.Validation("usage.days_out_of_range", $"days is {span}; a window is 1 to {MaxDays} days.");
        }

        return (now.AddDays(-span), now);
    }

    /// <summary>
    /// The span a run's requests are read over, with the clock tolerance the cost
    /// attribution allows either side: from its start to its last update, or, while it is
    /// still marked under way, to now — cut short at the start of the next run one of its
    /// sessions drove, since a run abandoned in progress did not go on spending through the
    /// runs that came after it.
    /// </summary>
    internal static (DateTimeOffset Opened, DateTimeOffset Closed) Span(DeliveryRun run, IReadOnlyList<string> sessionIds, IReadOnlyList<DeliveryRun> catalog, DateTimeOffset now)
    {
        var opened = PlacedAt(run);
        var closed = run.UpdatedAt;

        if (run.InProgress)
        {
            closed = catalog
                .Where(other => !ReferenceEquals(other, run)
                    && PlacedAt(other) > opened
                    && other.SessionIds.Intersect(sessionIds, StringComparer.Ordinal).Any())
                .Select(PlacedAt)
                .Append(now)
                .Min();
        }

        return (opened - DeliveryRunCosts.Tolerance, closed + DeliveryRunCosts.Tolerance);
    }

    /// <summary>Where a run sits on the calendar: its start, else its last update — the one
    /// moment a file that does not date its start is known to have existed.</summary>
    internal static DateTimeOffset PlacedAt(DeliveryRun run) => run.StartedAt ?? run.UpdatedAt;

    /// <summary>
    /// The repository a run is about, in confidence order: what the run itself named, what
    /// the session that drove it recorded, what this machine placed that session's folder
    /// under, then the repository of the first work item the run was linked to.
    /// </summary>
    internal static string? RepositoryOf(DeliveryRun run, AgentSession? owner) =>
        new[] { run.Repository, owner?.Repository, owner?.ResolvedRepository }
            .Concat(run.References.Select(reference => reference.Repository))
            .FirstOrDefault(repository => !string.IsNullOrWhiteSpace(repository))
            ?.Trim();

    private static DateTimeOffset Midnight(DateOnly day) =>
        new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}

/// <summary>
/// The figures of one run, put together from what was recorded. Pure, so the rules are
/// tested without a profile, a database or a transport underneath them.
/// </summary>
internal static class RunUsage
{
    internal static UsageRunPayload Of(DeliveryRun run, string? repository, IReadOnlyList<ClaudeApiRequest> requests)
    {
        var usage = run.TokenUsage;

        return new UsageRunPayload(
            run.Id,
            run.WorktreeName,
            repository,
            run.SkillId,
            run.Title,
            run.Status,
            run.Trigger,
            run.Schedule,
            run.StartedAt,
            run.UpdatedAt,
            usage is null ? null : Bucket(usage.Total),
            usage is null ? null : Bucket(usage.SubAgent),
            usage is null ? [] : [.. usage.ByStage.Select(stage => new UsageStagePayload(stage.StageName, Bucket(stage.Total), Bucket(stage.SubAgent)))],
            Models(run, requests),
            SubAgents(run),
            // A gauge that read 0 of 0 is unsampled, not empty — the rule PeakShare keeps —
            // and a context the telemetry opened only to list a compaction is one too.
            run.Context is { TokenLimit: > 0 } sampled ? sampled.PeakTokens : null,
            run.Context is { TokenLimit: > 0 } limited ? limited.TokenLimit : null,
            run.Context?.Compactions);
    }

    /// <summary>
    /// The runs' totals per repository, in the runs' order. A run with no usage counts
    /// as a run and adds no figure; a repository none of whose runs recorded usage has
    /// null totals rather than zeroes.
    /// </summary>
    internal static IReadOnlyList<UsageRepositoryPayload> ByRepository(IReadOnlyList<UsageRunPayload> runs) =>
    [
        .. runs
            .GroupBy(run => run.Repository ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(group => new UsageRepositoryPayload(
                group.Key.Length == 0 ? null : group.First().Repository,
                group.Count(),
                group.Count(run => run.Total is null),
                Sum(group.Select(run => run.Total)),
                Sum(group.Select(run => run.SubAgent))))
    ];

    internal static UsageTokensPayload Bucket(DeliveryRunTokens tokens) =>
        Bucket(tokens.ModelCalls, tokens.InputTokens, tokens.OutputTokens, tokens.CacheReadTokens, tokens.CacheWriteTokens);

    private static UsageTokensPayload Bucket(long calls, long input, long output, long cacheRead, long cacheWrite) =>
        new(calls, input, output, cacheRead, cacheWrite, input + output + cacheRead + cacheWrite, input + cacheWrite + output);

    private static UsageTokensPayload? Sum(IEnumerable<UsageTokensPayload?> buckets)
    {
        UsageTokensPayload? total = null;

        foreach (var bucket in buckets)
        {
            if (bucket is null) continue;

            total = total is null
                ? bucket
                : Bucket(
                    total.ModelCalls + bucket.ModelCalls,
                    total.InputTokens + bucket.InputTokens,
                    total.OutputTokens + bucket.OutputTokens,
                    total.CacheReadTokens + bucket.CacheReadTokens,
                    total.CacheWriteTokens + bucket.CacheWriteTokens);
        }

        return total;
    }

    /// <summary>
    /// The run's spend per model: Claude Code's own report of the requests its sessions
    /// made while it was open — each request once, already given to this run alone by the
    /// tool — largest first. A model whose requests carried no token count has null
    /// tokens rather than zeroes. Where no request answers, the models the run file names,
    /// each with no figures, so a reader still learns what ran without being handed a guess.
    /// </summary>
    private static IReadOnlyList<UsageModelPayload> Models(DeliveryRun run, IReadOnlyList<ClaudeApiRequest> requests)
    {
        var during = requests;

        if (during.Count > 0)
        {
            return
            [
                .. during
                    .GroupBy(request => request.Model ?? string.Empty, StringComparer.Ordinal)
                    .Select(model => new UsageModelPayload(
                        model.Key.Length == 0 ? null : model.Key,
                        UsageModelPayload.ReportedSource,
                        model.Any(Counted)
                            ? Bucket(
                                model.LongCount(),
                                model.Sum(request => request.InputTokens ?? 0),
                                model.Sum(request => request.OutputTokens ?? 0),
                                model.Sum(request => request.CacheReadTokens ?? 0),
                                model.Sum(request => request.CacheCreationTokens ?? 0))
                            : null,
                        model.Any(request => request.CostUsdMicros is not null)
                            ? model.Sum(request => request.CostUsdMicros ?? 0) / 1_000_000m
                            : null))
                    .OrderByDescending(model => model.Tokens?.UncachedTokens ?? -1)
                    .ThenBy(model => model.Model, StringComparer.Ordinal)
            ];
        }

        return
        [
            .. (run.TokenUsage?.Models ?? [])
                .Where(model => !string.IsNullOrWhiteSpace(model))
                .Distinct(StringComparer.Ordinal)
                .Select(model => new UsageModelPayload(model, UsageModelPayload.NamedSource, null, null))
        ];
    }

    /// <summary>Whether a request carried any token count at all.</summary>
    private static bool Counted(ClaudeApiRequest request) =>
        request.InputTokens is not null
        || request.OutputTokens is not null
        || request.CacheReadTokens is not null
        || request.CacheCreationTokens is not null;

    /// <summary>
    /// The agents the run delegated to, summed over its stages, most tokens first. A stage
    /// whose sub-agent calls were recorded one by one is read from them; one that only
    /// declared its agents is read from the declaration, with the call count it gives and
    /// no tokens — a declaration carries none.
    /// </summary>
    private static IReadOnlyList<UsageSubAgentPayload> SubAgents(DeliveryRun run)
    {
        var calls = new List<(string Agent, string? Model, string Stage, long? Tokens, long? DurationMs, int Count, int Failed)>();

        foreach (var stage in run.Stages)
        {
            if (stage.SubAgentRuns.Count > 0)
            {
                calls.AddRange(stage.SubAgentRuns.Select(call => (call.Agent, call.Model, stage.Name, call.Tokens, call.DurationMs, 1, call.Failed ? 1 : 0)));
            }
            else
            {
                calls.AddRange(stage.Agents
                    .Where(agent => agent.Count > 0)
                    .Select(agent => (agent.Name, agent.Model, stage.Name, (long?)null, (long?)null, agent.Count, agent.Failed)));
            }
        }

        return
        [
            .. calls
                .Where(call => !string.IsNullOrWhiteSpace(call.Agent))
                .GroupBy(call => call.Agent, StringComparer.OrdinalIgnoreCase)
                .Select(agent => new UsageSubAgentPayload(
                    agent.First().Agent,
                    agent.Sum(call => call.Count),
                    agent.Sum(call => call.Failed),
                    agent.Any(call => call.Tokens is not null) ? agent.Sum(call => call.Tokens ?? 0) : null,
                    agent.Any(call => call.DurationMs is not null) ? agent.Sum(call => call.DurationMs ?? 0) : null,
                    [.. agent.Select(call => call.Model).OfType<string>().Where(model => model.Length > 0).Distinct(StringComparer.Ordinal)],
                    [.. agent.Select(call => call.Stage).Distinct(StringComparer.Ordinal)]))
                .OrderByDescending(agent => agent.Tokens ?? -1)
                .ThenBy(agent => agent.Agent, StringComparer.OrdinalIgnoreCase)
        ];
    }
}

/// <summary>What <c>list_run_usage</c> answered.</summary>
/// <param name="From">The window's start, inclusive.</param>
/// <param name="To">The window's end, exclusive.</param>
/// <param name="Count">How many runs are placed inside it — by their start, or by their last
/// update where the run file does not date its start.</param>
/// <param name="Unreadable">The run folders that could not be read, by name: a run they
/// held is missing from the answer, and a report has to say so.</param>
/// <param name="Repositories">The totals per repository, a null repository for the runs
/// nothing placed.</param>
/// <param name="Runs">Every run, by repository and then newest first.</param>
public sealed record UsagePayload(
    DateTimeOffset From,
    DateTimeOffset To,
    int Count,
    IReadOnlyList<string> Unreadable,
    IReadOnlyList<UsageRepositoryPayload> Repositories,
    IReadOnlyList<UsageRunPayload> Runs);

/// <summary>One repository's runs, summed.</summary>
/// <param name="RunsWithoutUsage">How many of them recorded no token usage at all — a
/// session that did not report, or a dashboard generation that wrote none.</param>
/// <param name="Total">The runs' totals summed, or null where none recorded any.</param>
/// <param name="SubAgent">The part of <paramref name="Total"/> delegated agents spent.</param>
public sealed record UsageRepositoryPayload(
    string? Repository,
    int Runs,
    int RunsWithoutUsage,
    UsageTokensPayload? Total,
    UsageTokensPayload? SubAgent);

/// <summary>One bucket of tokens.</summary>
/// <param name="InputTokens">Uncached input.</param>
/// <param name="Tokens">Everything moved: input, output, cache reads and cache writes.</param>
/// <param name="UncachedTokens"><paramref name="Tokens"/> less the cache reads — input,
/// cache writes and output: the tokens not served again from the prompt cache. What a cost
/// analysis ranks on, since the cached prompt is re-read on every turn and the headline
/// figure can run to many windows' worth of it.</param>
public sealed record UsageTokensPayload(
    long ModelCalls,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    long Tokens,
    long UncachedTokens);

/// <summary>One run and what it spent.</summary>
/// <param name="Worktree">The worktree's name, as the pane shows it.</param>
/// <param name="Repository">The repository the run is placed in, or null where nothing
/// places it.</param>
/// <param name="Total">The whole run, owner session and sub-agents together; null where
/// the run recorded no usage.</param>
/// <param name="SubAgent">The part of <paramref name="Total"/> delegated agents spent.</param>
/// <param name="Stages">Per stage, in stage order, for the stages that recorded any.</param>
/// <param name="Models">Per model, largest first.</param>
/// <param name="SubAgents">Per delegated agent, most tokens first.</param>
/// <param name="PeakContextTokens">The fullest the owner session's context got, or null where
/// the gauge was never sampled.</param>
/// <param name="ContextLimit">The window that peak is measured against, or null with it.</param>
/// <param name="Compactions">How often the owner session's context was compacted while the
/// run was open, where the run file has a context — a lower bound past the hundred the
/// telemetry keeps.</param>
public sealed record UsageRunPayload(
    string Id,
    string Worktree,
    string? Repository,
    string SkillId,
    string Title,
    string Status,
    string? Trigger,
    string? Schedule,
    DateTimeOffset? StartedAt,
    DateTimeOffset UpdatedAt,
    UsageTokensPayload? Total,
    UsageTokensPayload? SubAgent,
    IReadOnlyList<UsageStagePayload> Stages,
    IReadOnlyList<UsageModelPayload> Models,
    IReadOnlyList<UsageSubAgentPayload> SubAgents,
    long? PeakContextTokens,
    long? ContextLimit,
    int? Compactions);

/// <summary>One stage's share of a run's usage.</summary>
public sealed record UsageStagePayload(string Stage, UsageTokensPayload Total, UsageTokensPayload SubAgent);

/// <summary>One model's share of a run's usage.</summary>
/// <param name="Model">The model as recorded, or null for requests that named none.</param>
/// <param name="Source"><c>reported</c> where Claude Code's own request telemetry gives the
/// figures; <c>named</c> where the run file only names the model, and the figures are
/// null.</param>
/// <param name="Tokens">The model's tokens, or null where only named or where none of its
/// requests carried a token count.</param>
/// <param name="ReportedCostUsd">What Claude Code reported those requests cost, in US
/// dollars, or null where none of them carried a cost.</param>
public sealed record UsageModelPayload(string? Model, string Source, UsageTokensPayload? Tokens, decimal? ReportedCostUsd)
{
    internal const string ReportedSource = "reported";

    internal const string NamedSource = "named";
}

/// <summary>One agent a run delegated to, over all its stages.</summary>
/// <param name="Calls">How many times it ran.</param>
/// <param name="Failed">How many of those did not complete.</param>
/// <param name="Tokens">The tokens its calls reported, or null where none reported any.</param>
/// <param name="DurationMs">How long its calls ran together, or null where none recorded it.</param>
/// <param name="Models">The models it ran on, where recorded.</param>
/// <param name="Stages">The stages it ran in.</param>
public sealed record UsageSubAgentPayload(
    string Agent,
    int Calls,
    int Failed,
    long? Tokens,
    long? DurationMs,
    IReadOnlyList<string> Models,
    IReadOnlyList<string> Stages);
