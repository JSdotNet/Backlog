using System.Globalization;

namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// The usage week a share is taken over: the seven days ending at the reset time the
/// latest recorded weekly-limit refusal carried, or with no such refusal, the seven
/// days up to now (<c>.devbook/domain/sessions/features.md</c>, "Share of the week and
/// the weekly limit").
/// <para>
/// A reset that has already passed is rolled forward a whole week at a time until it
/// lies ahead, because Claude's weekly allowance renews on the same weekday and hour
/// each week — the refusal said when one week ended, and the weeks after it end on the
/// same beat.
/// </para>
/// </summary>
/// <param name="Start">The first instant inside the week.</param>
/// <param name="End">The instant the week ends: the reset, or now for a trailing week.</param>
/// <param name="FromReset">True where the week was placed by a refusal's reset time,
/// false for the trailing seven days.</param>
public sealed record UsageWeek(DateTimeOffset Start, DateTimeOffset End, bool FromReset)
{
    /// <summary>How long a usage week is.</summary>
    public static readonly TimeSpan Length = TimeSpan.FromDays(7);

    /// <summary>Whether an instant falls inside the week. A reset week ends before its
    /// reset — that instant belongs to the next; a trailing week includes now.</summary>
    public bool Contains(DateTimeOffset at) => at >= Start && (FromReset ? at < End : at <= End);

    /// <summary>The week as of <paramref name="now"/>, placed by the latest all-models
    /// weekly refusal that carried a reset time, or with none, the latest Fable one —
    /// Claude resets both allowances on the same beat, so either places the week.</summary>
    public static UsageWeek Of(IEnumerable<AgentLimitHit> hits, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(hits);

        var placing = hits.Where(hit => hit.ResetsAt is not null).ToList();
        var latest = placing.Where(hit => hit.Kind is AgentLimitKind.Weekly).MaxBy(hit => hit.At)
            ?? placing.Where(hit => hit.Kind is AgentLimitKind.WeeklyFable).MaxBy(hit => hit.At);

        if (latest?.ResetsAt is not { } reset) return new(now - Length, now, false);

        var end = reset;

        if (end <= now)
        {
            var weeks = (now - end).Ticks / Length.Ticks + 1;
            end += TimeSpan.FromTicks(Length.Ticks * weeks);
        }

        return new(end - Length, end, true);
    }
}

/// <summary>
/// The size of one weekly limit, learned from the most recent refusal for reaching it:
/// the cost Claude Code reported over that refusal's week up to the moment it was
/// refused. An estimate by construction — nothing the product reads states the limit's
/// size, and the telemetry may have missed requests made on another machine.
/// </summary>
/// <param name="Kind"><see cref="AgentLimitKind.Weekly"/> for the all-models limit,
/// <see cref="AgentLimitKind.WeeklyFable"/> for Fable's own.</param>
/// <param name="HitAt">When the refusal that calibrated it was recorded.</param>
/// <param name="UsdMicros">The cost reached at that refusal, in millionths of a dollar.</param>
public sealed record WeeklyLimit(AgentLimitKind Kind, DateTimeOffset HitAt, long UsdMicros)
{
    /// <summary>
    /// The limit of <paramref name="kind"/> as the most recent refusal of that kind sizes
    /// it, or null with no such refusal or no reported cost before it. The refusal's week
    /// starts seven days before its reset time where it carried one, else seven days
    /// before the refusal. Fable's limit counts Fable's requests only.
    /// </summary>
    public static WeeklyLimit? Calibrate(
        AgentLimitKind kind,
        IEnumerable<AgentLimitHit> hits,
        IReadOnlyList<ClaudeApiRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(hits);
        ArgumentNullException.ThrowIfNull(requests);

        if (!WeeklyUsage.IsWeekly(kind)) return null;

        var hit = hits.Where(candidate => candidate.Kind == kind).MaxBy(candidate => candidate.At);

        if (hit is null) return null;

        var start = (hit.ResetsAt is { } reset && reset > hit.At ? reset : hit.At) - UsageWeek.Length;
        var fable = kind is AgentLimitKind.WeeklyFable;

        var total = WeeklyUsage.Distinct(requests)
            .Where(request => request.Timestamp >= start && request.Timestamp <= hit.At)
            .Where(request => !fable || WeeklyUsage.IsFable(request.Model))
            .Sum(request => request.CostUsdMicros ?? 0);

        return total > 0 ? new(kind, hit.At, total) : null;
    }
}

/// <summary>
/// One session's or one run's part of the week.
/// </summary>
/// <param name="OfWeek">Its reported cost inside the week over the week's total, 0–1.</param>
/// <param name="OfLimit">The same cost over the calibrated weekly limit, or null while
/// no weekly-limit refusal has sized one. An estimate, and labelled so.</param>
/// <param name="FableOfLimit">Its Fable cost inside the week over Fable's calibrated
/// weekly limit, or null with no Fable refusal or no Fable cost of its own.</param>
public sealed record UsageShare(double OfWeek, double? OfLimit, double? FableOfLimit)
{
    /// <summary>A fraction as a reader takes it in: one decimal under ten percent, whole
    /// percents above, and "&lt; 0.1%" rather than a zero for a part too small to show.</summary>
    public static string Percent(double fraction)
    {
        var percent = fraction * 100;

        return percent switch
        {
            <= 0 => "0%",
            < 0.1 => "< 0.1%",
            < 10 => percent.ToString("0.#", CultureInfo.InvariantCulture) + "%",
            _ => percent.ToString("0", CultureInfo.InvariantCulture) + "%"
        };
    }
}

/// <summary>
/// The week's telemetry cost and the weekly limits it has learned, worked out once over
/// every stored request so each session and run can be given its share of it.
/// <para>
/// Reported cost only: the share is "of this week's total telemetry cost", and a token
/// estimate on one side of the division with a reported sum on the other would compare
/// two different measurements.
/// </para>
/// </summary>
/// <param name="Week">The week the shares are taken over.</param>
/// <param name="AsOf">When the total was read. A share counts nothing later, so a
/// session still running cannot outgrow a total read before its newest requests.</param>
/// <param name="TotalUsdMicros">The cost reported inside the week, every model.</param>
/// <param name="Limit">The all-models weekly limit, once a refusal has sized it.</param>
/// <param name="FableLimit">Fable's own weekly limit, once a refusal has sized it.</param>
public sealed record WeeklyUsage(UsageWeek Week, DateTimeOffset AsOf, long TotalUsdMicros, WeeklyLimit? Limit, WeeklyLimit? FableLimit)
{
    /// <summary>The week as of <paramref name="now"/>, over every stored request and
    /// every recorded limit refusal.</summary>
    public static WeeklyUsage Of(
        IReadOnlyList<ClaudeApiRequest> requests,
        IReadOnlyList<AgentLimitHit> hits,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(hits);

        var week = UsageWeek.Of(hits, now);
        var total = Distinct(requests).Where(request => week.Contains(request.Timestamp) && request.Timestamp <= now).Sum(request => request.CostUsdMicros ?? 0);

        return new(
            week,
            now,
            total,
            WeeklyLimit.Calibrate(AgentLimitKind.Weekly, hits, requests),
            WeeklyLimit.Calibrate(AgentLimitKind.WeeklyFable, hits, requests));
    }

    /// <summary>
    /// The share of the week the given requests make up, or null where they reported no
    /// cost inside the week or the week reported none at all — a part of nothing is not
    /// a share.
    /// </summary>
    public UsageShare? ShareOf(IEnumerable<ClaudeApiRequest> own)
    {
        ArgumentNullException.ThrowIfNull(own);

        if (TotalUsdMicros <= 0) return null;

        var inWeek = Distinct(own).Where(request => Week.Contains(request.Timestamp) && request.Timestamp <= AsOf).ToList();
        var cost = inWeek.Sum(request => request.CostUsdMicros ?? 0);

        if (cost <= 0) return null;

        var fable = inWeek.Where(request => IsFable(request.Model)).Sum(request => request.CostUsdMicros ?? 0);

        return new(
            cost / (double)TotalUsdMicros,
            Limit is { } limit ? cost / (double)limit.UsdMicros : null,
            FableLimit is { } fableLimit && fable > 0 ? fable / (double)fableLimit.UsdMicros : null);
    }

    /// <summary>The share one session's requests make up.</summary>
    public UsageShare? ShareOfSession(string? sessionId, IReadOnlyList<ClaudeApiRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        return sessionId is null ? null : ShareOf(requests.Where(request => request.SessionId == sessionId));
    }

    /// <summary>The share one run's requests make up: every request its stages' windows
    /// and their sub-agent calls claim, by the same matching its stage costs use.</summary>
    public UsageShare? ShareOfRun(DeliveryRun run, IReadOnlyList<ClaudeApiRequest> requests, string? sessionId = null) =>
        ShareOf(RunRequests(run, requests, sessionId));

    /// <summary>The requests a run claims, deduplicated: each stage's window and its
    /// workers, over the run's own sessions — or <paramref name="sessionId"/> where the
    /// run names none.</summary>
    public static IReadOnlyList<ClaudeApiRequest> RunRequests(DeliveryRun run, IReadOnlyList<ClaudeApiRequest> requests, string? sessionId = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(requests);

        var sessions = run.SessionIds.Count > 0
            ? new HashSet<string>(run.SessionIds, StringComparer.Ordinal)
            : sessionId is null ? [] : new HashSet<string>([sessionId], StringComparer.Ordinal);
        var own = requests.Where(request => request.SessionId is { } id && sessions.Contains(id)).ToList();

        if (own.Count == 0) return [];

        var resolved = DeliveryRunStageResolution.Of(run);
        var claimed = new Dictionary<string, ClaudeApiRequest>(StringComparer.Ordinal);

        for (var index = 0; index < resolved.Count && index < run.Stages.Count; index++)
        {
            foreach (var request in DeliveryRunCosts.StageRequests(run.Stages[index], resolved[index], own))
            {
                claimed.TryAdd(request.RequestId, request);
            }

            for (var worker = 0; worker < resolved[index].Workers.Count; worker++)
            {
                foreach (var request in DeliveryRunCosts.WorkerRequests(run.Stages[index], resolved[index], worker, own))
                {
                    claimed.TryAdd(request.RequestId, request);
                }
            }
        }

        return [.. claimed.Values.OrderBy(request => request.Timestamp)];
    }

    /// <summary>Whether a limit kind is one of the two weekly ones.</summary>
    public static bool IsWeekly(AgentLimitKind kind) => kind is AgentLimitKind.Weekly or AgentLimitKind.WeeklyFable;

    /// <summary>Whether a request went to a Fable model, by the same family reading the
    /// run's stage resolution uses.</summary>
    public static bool IsFable(string? model) =>
        !string.IsNullOrWhiteSpace(model) && DeliveryRunStageResolution.Family(model) == "fable";

    internal static IEnumerable<ClaudeApiRequest> Distinct(IEnumerable<ClaudeApiRequest> requests) =>
        requests.DistinctBy(request => request.RequestId, StringComparer.Ordinal);
}
