using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.SharedKernel;

namespace Backlog.Modules.Dashboard.Services;

/// <summary>
/// Turns everything the assistants left on this installation into the one sessions
/// part, scoped to the window and the machine the reader has chosen.
/// </summary>
/// <remarks>
/// <para>
/// One cache entry rather than one per scope, which is the opposite of what
/// <see cref="ProductivityInsights"/> does and is right for the opposite reason. That
/// one narrows the <em>call</em> — a repository focus is a different GitHub fetch — so
/// its key carries the focus. These sources are local reads, so narrowing is a pure
/// function over what they returned and the reader can move the machine filter or the
/// window without touching the disk again. It is also why <see cref="Invalidate"/>
/// takes no scope: there is nothing per-scope to drop.
/// </para>
/// <para>
/// Two sources, and the second is the expensive one. The session list is a file stat
/// per session; the activity read parses transcript bodies. Both are pulled under the
/// one cache entry and both are read at the widest window this surface can be asked for
/// — see <see cref="Horizon"/> — so that moving the period control stays a derivation
/// rather than a read.
/// </para>
/// <para>
/// Every figure understates rather than invents. A stretch of activity is clipped to
/// the window rather than counted whole, a session the activity source could not
/// describe contributes no duration at all and is counted as such, and a capped or
/// partly unreadable source travels as such all the way to the screen. Where a number
/// could be guessed, it is not.
/// </para>
/// <para>
/// Availability is asked before data and a throw is turned into the same
/// unavailable-with-a-reason answer, exactly as the productivity derivation does. One
/// source having a bad minute must not take the surface down, and the reader needs a
/// sentence either way. It is cached with the report rather than beside it, so the claim
/// above holds for the whole call and not only for its second half: moving the machine
/// filter asks the sources nothing at all until the reader refreshes.
/// </para>
/// </remarks>
/// <param name="sessions">The cheap half: which sessions there were.</param>
/// <param name="activity">
/// The expensive half: when an agent was actually producing. Required rather than
/// optional, and that is a decision. A null activity source would render an empty grid
/// and a zero tile with no explanation, which is the one thing this surface must not do
/// — every other absence on it arrives with a sentence attached.
/// </param>
/// <param name="time">The clock the window is measured back from. Read here and
/// nowhere below it, so the whole derivation stays a pure function of one reading.</param>
public sealed class SessionInsights(
    IAssistantSessionSource sessions,
    IAssistantActivitySource activity,
    IWorkingHoursSettings workingHours,
    IRepositoryDirectory repositories,
    IUsageResetSettings usageReset,
    TimeProvider time) : ISessionInsights
{
    /// <summary>The only key there is. Named rather than empty so the entry reads as a
    /// deliberate single entry in a cache that holds several kinds.</summary>
    private const string CacheKey = "sessions|all";

    /// <summary>
    /// The widest window this surface can be asked for. The horizon is a constant rather
    /// than the scope's own window for the property the whole class turns on: reading
    /// twelve weeks once means moving the period control derives again rather than reads
    /// again, exactly as moving the machine filter already does. It costs one longer read
    /// on a refresh and buys a scope change that never touches the disk.
    /// <para>
    /// Both sources are read back to it, the session list included. That list used to be
    /// read with no horizon at all and came back in the inventory's shape — the newest
    /// hundred per assistant — so every machine's count was the page size. The Sessions
    /// context promises to keep records this far back (<c>AgentSessionLimits.History</c>,
    /// which this module may not name); a horizon longer than that would come back
    /// capped, and a period control offering more than twelve weeks has to move both.
    /// </para>
    /// </summary>
    private static TimeSpan Horizon => DashboardScope.Horizon;

    private readonly InsightCache _cache = new();

    public async Task<InsightResult<AssistantSessionsInsight>> GetSessionsAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        try
        {
            var reading = await _cache.GetOrAddAsync(CacheKey, ReadAsync, cancellationToken).ConfigureAwait(false);

            return reading.Report is { } report
                ? InsightResult<AssistantSessionsInsight>.Ready(Derive(reading, report, scope))
                : InsightResult<AssistantSessionsInsight>.Unavailable(reading.Availability.Reason);
        }
        catch (OperationCanceledException)
        {
            // The reader closing the dashboard or moving a filter, not a source
            // failing. Let it travel.
            throw;
        }
        catch (Exception exception)
        {
            return InsightResult<AssistantSessionsInsight>.Unavailable(exception.Message);
        }
    }

    public void Invalidate() => _cache.Clear();

    /// <summary>
    /// One trip to the sources: whether the session list can answer, and — when it can —
    /// everything both of them have. All under one cache entry, so a refusal is
    /// remembered for as long as the reports would have been and a scope change costs
    /// none of the calls.
    /// </summary>
    /// <remarks>
    /// The activity source is asked only once the session list has said it can answer.
    /// The two read the same folders, so a refusal from the first is a refusal about the
    /// second as well, and parsing hundreds of megabytes to confirm it would be work
    /// done to produce a sentence already written.
    /// </remarks>
    private async Task<Reading> ReadAsync(CancellationToken cancellationToken)
    {
        var availability = await sessions.GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);

        if (!availability.IsAvailable) return new Reading(availability, null, AssistantActivityReport.Empty);

        // One instant for both reads, so the session list and the activity log are the
        // same twelve weeks rather than two windows a tick apart.
        var since = time.GetUtcNow() - Horizon;

        var report = await sessions.GetSessionsAsync(since, cancellationToken).ConfigureAwait(false);

        var log = await activity.GetActivityAsync(since, cancellationToken).ConfigureAwait(false);

        return new Reading(availability, report, log);
    }

    /// <summary>What one read produced. The report is null exactly when the source said
    /// it could not answer, which is the only combination any of the three is ever
    /// in.</summary>
    private sealed record Reading(
        InsightAvailability Availability,
        AssistantSessionReport? Report,
        AssistantActivityReport Activity);

    /// <summary>
    /// The whole derivation: filter to the scope, then measure. Synchronous and pure,
    /// which is what lets every arithmetic decision below be asserted without a source
    /// behind it — and what makes moving the period control free.
    /// </summary>
    private AssistantSessionsInsight Derive(Reading reading, AssistantSessionReport report, DashboardScope scope)
    {
        var zone = time.LocalTimeZone;
        var (from, to) = scope.Window(time.GetUtcNow());

        var scoped = report.Sessions
            .Where(session => InWindow(session, from, to))
            .Where(session => scope.IsAllMachines || Matches(session.MachineId, scope.MachineId))
            .ToList();

        var scopedActivity = reading.Activity.Sessions
            .Where(session => scope.IsAllMachines || Matches(session.MachineId, scope.MachineId))
            .ToList();

        // One sweep per measure over the whole window, and the grid is read out of the
        // same dictionaries rather than swept a second time. Cells are disjoint hours,
        // so the last seven days of a twelve-week sweep are exactly what a seven-day
        // sweep would have produced — and taking them from here is what makes it
        // impossible for the tile and the grid to have measured differently.
        var active = LocalHourBuckets.Sweep(Intervals(scopedActivity, session => session.Active), from, to, zone);
        var waiting = LocalHourBuckets.Sweep(Intervals(scopedActivity, session => session.Waiting), from, to, zone);

        // A third sweep, over each session's producing and waiting stretches together.
        // Concatenated rather than merged because within one session the two never
        // overlap — a wait is the gap between two runs — so the sweep sees at most one of
        // them from a given session at any instant, and a run ending exactly as a wait
        // begins closes before it opens. What it does not include is silence that never
        // resumed: nothing records an end, so counting to the last event would hold an
        // abandoned window open for days.
        var open = LocalHourBuckets.Sweep(
            Intervals(scopedActivity, session => [.. session.Active, .. session.Waiting]),
            from,
            to,
            zone);

        var scopedAgents = reading.Activity.Subagents
            .Where(agent => scope.IsAllMachines || Matches(agent.MachineId, scope.MachineId))
            .ToList();

        // The fourth and last sweep, and the only new one. Over the subagents' stretches
        // alone: they are never merged with the sessions' and never compared to them. A
        // subagent always overlaps the session that spawned it — that is what spawning
        // means — so one sweep over both would report two of something that never existed.
        var agents = LocalHourBuckets.Sweep(
            scopedAgents.SelectMany(agent => agent.Active).Select(interval => (interval.From, interval.To)),
            from,
            to,
            zone);

        var week = workingHours.Current;

        var recorded = scopedActivity.Select(session => session.Id).ToHashSet(StringComparer.Ordinal);

        // The sessions the mean is over. Null is "nothing to count from", never zero, so
        // it leaves the denominator as well as the numerator — see the contract's
        // Prompts. Filtered once here so the tile and the columns cannot be over two
        // different populations.
        var counted = scoped.Where(session => session.Prompts is not null).ToList();

        // The week everything below is cut into. The person's own reset first, because
        // the records may be from another plan or another month; the assistant's last
        // reported reset second; Monday midnight on the local clock when neither is
        // known — and the insight says which, so the columns can.
        var weeks = Weeks(reading.Activity.Limits, to, zone);
        var buckets = weeks.Buckets(from, to);

        var scopedLimits = reading.Activity.Limits
            .Where(hit => scope.IsAllMachines || Matches(hit.MachineId, scope.MachineId))
            .ToList();

        var grids = Grids(buckets, scopedActivity, scopedLimits, active, waiting, open, agents, week, zone);

        var bandOf = Bands();
        var hits = scopedLimits.Where(hit => hit.At >= from && hit.At < to).ToList();

        // The two figures read off the session records rather than the activity: each
        // over the sessions that could say, and null never counted as zero.
        var used = scoped.Where(session => session.ModelUsage is not null).ToList();
        var linking = scoped.Where(session => session.PullRequests is not null).ToList();
        var linked = Linked(linking);

        return new AssistantSessionsInsight(
            scoped.Count,
            Sum(active),
            scoped.Count == 0 ? null : scoped.Max(session => session.LastActivityAt),
            scoped.Count(session => !recorded.Contains(session.Id)),
            report.Capped,
            report.Unreadable,
            Breakdown(scoped, scopedActivity, scope, from, to, zone))
        {
            // Bucketed on the last activity, which is the only instant every session
            // has: the start is optional and there is no end at all. A session that
            // ran across a week boundary is therefore one mark in the week it last
            // moved rather than a mark in each, and the part says that beside the
            // columns.
            SessionsPerWeek = WeekBuckets.Count(
                buckets,
                scoped,
                session => session.LastActivityAt,
                weeks.KeyOf),
            PromptsPerSession = MeanPrompts(counted),
            SessionsWithPrompts = counted.Count,
            // Same buckets and the same instant as the series above, so a column here is
            // the same sessions as the column beside it there. A week with nothing
            // counted is a zero point rather than a gap, on the rework rate's precedent.
            PromptsPerSessionPerWeek = WeekBuckets.Reduce(
                buckets,
                counted,
                session => session.LastActivityAt,
                inWeek => MeanPrompts(inWeek) ?? 0m,
                weeks.KeyOf),
            Waiting = Sum(waiting),
            MostSessionsAtOnce = Busiest(active),
            MostAgentsAtOnce = Busiest(agents),
            // The tiles above, cut by week. Read out of the same sweeps, cell by cell, so
            // the duration columns add up to their tile and the peak columns top out at
            // theirs — never a second pass over the intervals. A cell goes in the week
            // its start instant falls in, on the same cut as the sessions series.
            ActiveTimePerWeek = PerWeek(buckets, weeks, active, zone, Hours),
            WaitingPerWeek = PerWeek(buckets, weeks, waiting, zone, Hours),
            MostSessionsAtOncePerWeek = PerWeek(buckets, weeks, active, zone, Peak),
            MostAgentsAtOncePerWeek = PerWeek(buckets, weeks, agents, zone, Peak),
            ByRepository = ByRepository(buckets, weeks, scope, bandOf, scoped, scopedActivity, scopedAgents, from, to, zone),
            Week = new UsageWeekInfo(weeks.Source, weeks.ResetDescription),
            Grids = grids,
            LimitHits = LimitHits(hits),
            Tokens = used.Count == 0
                ? null
                : new TokenTotals(
                    used.Sum(session => session.ModelUsage!.Sum(usage => usage.OutputTokens)),
                    used.Sum(session => session.ModelUsage!.Sum(usage => usage.InputTokens)),
                    used.Sum(session => session.ModelUsage!.Sum(usage => usage.CacheReadInputTokens)),
                    used.Sum(session => session.ModelUsage!.Sum(usage => usage.CacheCreationInputTokens))),
            SessionsWithUsage = used.Count,
            TokensByModel = Ranked(
                used.SelectMany(session => session.ModelUsage!.Select(usage => (Session: session, Usage: usage)))
                    .GroupBy(pair => pair.Usage.Model, StringComparer.Ordinal)
                    .Select(model => Band(
                        model.Key,
                        null,
                        WeekBuckets.Reduce(
                            buckets,
                            model,
                            pair => pair.Session.LastActivityAt,
                            inWeek => inWeek.Sum(pair => (decimal)pair.Usage.OutputTokens),
                            weeks.KeyOf)))),
            TokensByRepository = Ranked(
                used.GroupBy(session => bandOf(session.Repository))
                    .Where(band => InRepositoryScope(scope, band.Key))
                    .Select(band => Band(
                        band.Key.Name,
                        band.Key.Kind,
                        WeekBuckets.Reduce(
                            buckets,
                            band,
                            session => session.LastActivityAt,
                            inWeek => inWeek.Sum(session => (decimal)session.ModelUsage!.Sum(usage => usage.OutputTokens)),
                            weeks.KeyOf)))),
            PullRequests = (int)WeekBuckets.Count(buckets, linked, pr => pr.At, weeks.KeyOf).Sum(point => point.Value),
            SessionsWithPullRequestRecord = linking.Count,
            PullRequestsByRepository = Ranked(
                linked.GroupBy(pr => bandOf(pr.Repository))
                    .Where(band => InRepositoryScope(scope, band.Key))
                    .Select(band => Band(
                        band.Key.Name,
                        band.Key.Kind,
                        WeekBuckets.Count(buckets, band, pr => pr.At, weeks.KeyOf)))),
            ActivityByHour = grids.Count == 0 ? [] : grids[^1].Hours,
            ActivityByDay = grids.Count == 0 ? [] : grids[^1].Days,
            IdleAfter = reading.Activity.IdleAfter
        };
    }

    /// <summary>
    /// The cut in force: the configured reset, else the last weekly refusal's reset the
    /// assistant reported, else the local calendar. A configured reset outranks a
    /// detected one on purpose — the records on a machine may come from an older plan
    /// — and the surface names which won.
    /// </summary>
    private UsageWeeks Weeks(IReadOnlyList<AssistantLimitHit> limits, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (usageReset.Current is { } configured)
        {
            return UsageWeeks.AnchoredOn(configured.MostRecentBefore(now, zone), zone, WeekSource.Configured);
        }

        var detected = limits
            .Where(hit => hit.Kind == AssistantLimitKind.SevenDay)
            .OrderByDescending(hit => hit.At)
            .Select(hit => (DateTimeOffset?)hit.ResetsAt)
            .FirstOrDefault();

        return detected is { } reset
            ? UsageWeeks.AnchoredOn(reset, zone, WeekSource.Detected)
            : UsageWeeks.Calendar(now, zone);
    }

    /// <summary>
    /// The mean prompt count over sessions that all carry one, or null when there are
    /// none. Callers hand this the already-filtered list, so a null count reaching it
    /// is a programming error rather than a session to skip — and it is written that
    /// way so the two places it is used cannot filter differently.
    /// </summary>
    private static decimal? MeanPrompts(IReadOnlyList<AssistantSession> counted) =>
        counted.Count == 0
            ? null
            : (decimal)counted.Sum(session => session.Prompts!.Value) / counted.Count;

    /// <summary>
    /// A sweep cut into the given week buckets: every cell into the week its start
    /// instant falls in, and the cells that landed in a week reduced to one figure by
    /// <paramref name="aggregate"/> — a sum of hours or a peak, which are the two
    /// things a cell holds. A week no cell landed in is handed an empty list and is
    /// whatever the aggregate makes of nothing, which for both is zero.
    /// <para>
    /// The cell's instant is its local start put back into UTC through the zone the
    /// sweep was cut on — the offset the zone reports for that local hour, which is
    /// defined on both sides of a daylight-saving transition rather than throwing on
    /// the hour that repeats. The hour that does not exist cannot be a cell, because
    /// every cell came out of a UTC instant. What the round trip buys is the same week
    /// axis as the sessions series, drawn in UTC like every other week column here, and
    /// a cell that cannot fall outside the buckets covering its own window.
    /// </para>
    /// </summary>
    private static IReadOnlyList<InsightPoint> PerWeek(
        IReadOnlyList<WeekBucket> buckets,
        UsageWeeks weeks,
        IReadOnlyDictionary<HourCell, HourReading> cells,
        TimeZoneInfo zone,
        Func<IReadOnlyList<KeyValuePair<HourCell, HourReading>>, decimal> aggregate) =>
        WeekBuckets.Reduce(
            buckets,
            cells,
            cell => StartOf(cell.Key, zone),
            aggregate,
            weeks.KeyOf);

    /// <summary>
    /// The weekly series again, one row per repository band, each row swept on its own.
    /// <para>
    /// The band is the session's, joined on the id: the activity record carries no
    /// repository, and the session record carries what the assistant wrote or, failing
    /// that, the registered clone its folder lies in. A recorded repository the workspace has configured is its
    /// alias, so the row wears the name the header's chips do; one it has not is folded
    /// into the other row, because nothing here has a name or a colour for it. A
    /// subagent takes its parent session's band, and an activity record whose session
    /// the report did not list — the cap, or a source that read one folder and not the
    /// other — is unrecorded too, because nothing says where it was.
    /// </para>
    /// <para>
    /// The scope is applied here and nowhere else on this insight: with repositories in
    /// focus only their rows are built, and the two folded rows are out — the Sessions
    /// list's treatment of a session it cannot place. The totals above are left whole
    /// on purpose; the contract says why.
    /// </para>
    /// <para>
    /// One sweep per row per measure rather than a partition of the total sweep, since
    /// a cell's peak is a property of which intervals overlapped and cannot be split
    /// after the fact. The hours can, and do add up across rows to the totals above;
    /// the peaks cannot, and the contract says so.
    /// </para>
    /// </summary>
    /// <summary>
    /// Which band a repository falls in: a configured one by its alias, an unconfigured
    /// one folded into the other row, none into the unrecorded row. One resolver for
    /// every chart cut by repository, so the activity rows, the tokens and the pull
    /// requests cannot band the same repository two ways.
    /// </summary>
    private Func<string?, (string Name, RepositoryBandKind Kind)> Bands()
    {
        // owner/name to alias, for the repositories the workspace knows. Case-insensitive
        // on the full name, as GitHub itself is.
        var aliasOf = repositories.Repositories
            .GroupBy(repository => repository.FullName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Alias, StringComparer.OrdinalIgnoreCase);

        return repository =>
            string.IsNullOrWhiteSpace(repository) ? (RepositoryWeekly.UnrecordedName, RepositoryBandKind.Unrecorded)
            : aliasOf.TryGetValue(repository.Trim(), out var alias) ? (alias, RepositoryBandKind.Configured)
            : (RepositoryWeekly.OtherName, RepositoryBandKind.Other);
    }

    /// <summary>With repositories in focus only their configured bands are in, and the
    /// two folded bands are out — the Sessions list's treatment of a session it cannot
    /// place.</summary>
    private static bool InRepositoryScope(DashboardScope scope, (string Name, RepositoryBandKind Kind) band) =>
        scope.IsAllRepositories || (band.Kind == RepositoryBandKind.Configured && scope.Repositories.Contains(band.Name));

    private static WeeklyBand Band(string name, RepositoryBandKind? kind, IReadOnlyList<InsightPoint> perWeek) =>
        new(name, kind, perWeek, perWeek.Sum(point => point.Value));

    /// <summary>Biggest first, ties by name so two refreshes of one profile list them
    /// in one order; a row of nothing in the window would be a legend entry for
    /// nothing, so it is left out.</summary>
    private static IReadOnlyList<WeeklyBand> Ranked(IEnumerable<WeeklyBand> bands) =>
        [.. bands
            .Where(band => band.Total > 0)
            .OrderByDescending(band => band.Total)
            .ThenBy(band => band.Name, StringComparer.Ordinal)];

    /// <summary>One pull request as the part counts it: where it lives and the instant
    /// that places it in a week.</summary>
    private sealed record LinkedPullRequest(string Repository, DateTimeOffset At);

    /// <summary>
    /// The distinct pull requests the sessions linked, each once. Matched on the URL,
    /// case-insensitively, because two sessions linking one pull request is the normal
    /// case — the one that opened it and the one that finished it. Placed at the
    /// earliest link, and at the linking session's last activity where the link was not
    /// dated, which is the one instant every session has.
    /// </summary>
    private static IReadOnlyList<LinkedPullRequest> Linked(IEnumerable<AssistantSession> sessions) =>
        [.. sessions
            .SelectMany(session => session.PullRequests!.Select(pr => (Pr: pr, At: pr.LinkedAt ?? session.LastActivityAt)))
            .GroupBy(
                pair => string.IsNullOrWhiteSpace(pair.Pr.Url) ? $"{pair.Pr.Repository}#{pair.Pr.Number}" : pair.Pr.Url.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select(same => same.OrderBy(pair => pair.At).First())
            .Select(pair => new LinkedPullRequest(pair.Pr.Repository, pair.At))];

    /// <summary>
    /// The refusals in the window, by allowance and by what overage did about each: an
    /// account already on overage, a wall for a reason the assistant named, or nothing
    /// said. A <c>rejected</c> status with no reason is a wall too, under that word.
    /// </summary>
    private static LimitHitCounts LimitHits(IReadOnlyList<AssistantLimitHit> hits)
    {
        static string? WallReason(AssistantLimitHit hit) =>
            hit.IsUsingOverage == true ? null
            : !string.IsNullOrWhiteSpace(hit.OverageDisabledReason) ? hit.OverageDisabledReason.Trim()
            : string.Equals(hit.OverageStatus, "rejected", StringComparison.OrdinalIgnoreCase) ? "rejected"
            : null;

        var walled = hits
            .Select(WallReason)
            .OfType<string>()
            .GroupBy(reason => reason, StringComparer.Ordinal)
            .Select(reason => new LimitWall(reason.Key, reason.Count()))
            .OrderByDescending(wall => wall.Count)
            .ThenBy(wall => wall.Reason, StringComparer.Ordinal)
            .ToList();

        var onOverage = hits.Count(hit => hit.IsUsingOverage == true);

        return new LimitHitCounts(
            hits.Count(hit => hit.Kind == AssistantLimitKind.FiveHour),
            hits.Count(hit => hit.Kind == AssistantLimitKind.SevenDay))
        {
            OnOverage = onOverage,
            Walled = walled,
            OverageUnrecorded = hits.Count - onOverage - walled.Sum(wall => wall.Count)
        };
    }

    private IReadOnlyList<RepositoryWeekly> ByRepository(
        IReadOnlyList<WeekBucket> buckets,
        UsageWeeks weeks,
        DashboardScope scope,
        Func<string?, (string Name, RepositoryBandKind Kind)> bandOf,
        IReadOnlyList<AssistantSession> sessions,
        IReadOnlyList<AssistantActivitySession> activity,
        IReadOnlyList<AssistantActivitySubagent> agents,
        DateTimeOffset from,
        DateTimeOffset to,
        TimeZoneInfo zone)
    {
        if (buckets.Count == 0) return [];

        // Last one wins on a duplicate id, which the sessions list does not produce;
        // the dictionary is only refusing to throw on a fixture that does.
        var bandOfSession = new Dictionary<string, (string Name, RepositoryBandKind Kind)>(StringComparer.Ordinal);

        foreach (var session in sessions)
        {
            bandOfSession[session.Id] = bandOf(session.Repository);
        }

        (string Name, RepositoryBandKind Kind) BandOfId(string sessionId) =>
            bandOfSession.TryGetValue(sessionId, out var band) ? band : (RepositoryWeekly.UnrecordedName, RepositoryBandKind.Unrecorded);

        var rows = sessions
            .Select(session => bandOf(session.Repository))
            .Concat(activity.Select(session => BandOfId(session.Id)))
            .Distinct()
            .Where(band => InRepositoryScope(scope, band))
            .Select(band =>
            {
                var theirSessions = sessions.Where(session => bandOf(session.Repository) == band).ToList();
                var theirs = activity.Where(session => BandOfId(session.Id) == band).ToList();
                var theirAgents = agents.Where(agent => BandOfId(agent.SessionId) == band).ToList();
                var counted = theirSessions.Where(session => session.Prompts is not null).ToList();

                var active = LocalHourBuckets.Sweep(Intervals(theirs, session => session.Active), from, to, zone);
                var waiting = LocalHourBuckets.Sweep(Intervals(theirs, session => session.Waiting), from, to, zone);
                var spawned = LocalHourBuckets.Sweep(
                    theirAgents.SelectMany(agent => agent.Active).Select(interval => (interval.From, interval.To)),
                    from,
                    to,
                    zone);

                return (
                    Cells: active.Count + waiting.Count + spawned.Count + theirSessions.Count,
                    Total: Sum(active),
                    Row: new RepositoryWeekly(
                        band.Name,
                        band.Kind,
                        WeekBuckets.Count(buckets, theirSessions, session => session.LastActivityAt, weeks.KeyOf),
                        WeekBuckets.Reduce(buckets, counted, session => session.LastActivityAt, inWeek => MeanPrompts(inWeek) ?? 0m, weeks.KeyOf),
                        PerWeek(buckets, weeks, active, zone, Hours),
                        PerWeek(buckets, weeks, waiting, zone, Hours),
                        PerWeek(buckets, weeks, active, zone, Peak),
                        PerWeek(buckets, weeks, spawned, zone, Peak)));
            })
            // A band with nothing at all in the window — no session and no cell on any
            // sweep — would be a legend entry for a band of nothing.
            .Where(entry => entry.Cells > 0)
            .OrderByDescending(entry => entry.Total)
            .ThenBy(entry => entry.Row.Name, StringComparer.Ordinal)
            .Select(entry => entry.Row);

        return [.. rows];
    }

    /// <summary>The UTC instant a local hour cell begins at.</summary>
    private static DateTimeOffset StartOf(HourCell cell, TimeZoneInfo zone)
    {
        var local = cell.Day.ToDateTime(new TimeOnly(cell.Hour, 0));

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    /// <summary>What a week's cells add up to, in hours. Decimal from the total rather
    /// than from a rounded figure, so the columns sum to the tile to the tick.</summary>
    private static decimal Hours(IReadOnlyList<KeyValuePair<HourCell, HourReading>> inWeek) =>
        (decimal)inWeek.Aggregate(TimeSpan.Zero, (running, cell) => running + cell.Value.Total).TotalHours;

    /// <summary>The most at once in any of a week's cells, or zero for a week with none —
    /// a maximum, which is the one arithmetic a peak survives.</summary>
    private static decimal Peak(IReadOnlyList<KeyValuePair<HourCell, HourReading>> inWeek) =>
        inWeek.Count == 0 ? 0m : inWeek.Max(cell => cell.Value.Peak);

    /// <summary>
    /// The busiest cell of a sweep, or null when the sweep found nothing.
    /// <para>
    /// Ordered before it is maximised, and that is not decoration. A dictionary
    /// enumerates in whatever order it enumerates in, so two hours that both reached the
    /// peak would be separated by nothing at all and the tile would name a different hour
    /// between two refreshes of an unchanged profile — the defect
    /// <c>ClaudeTranscripts.Newest</c> carries a path tie-break against. The earliest
    /// wins, because "when it first got that busy" is a question with one answer.
    /// </para>
    /// <para>
    /// A read-out of the same sweep the grid is drawn from, never a second pass. The
    /// window's true maximum falls inside some hour and that hour's cell recorded it, so
    /// the tile and the grid cannot have measured differently — the rule the active time
    /// is already summed under.
    /// </para>
    /// </summary>
    private static ConcurrencyPeak? Busiest(IReadOnlyDictionary<HourCell, HourReading> cells) =>
        cells
            .Where(cell => cell.Value.Peak > 0)
            .OrderByDescending(cell => cell.Value.Peak)
            .ThenBy(cell => cell.Key.Day)
            .ThenBy(cell => cell.Key.Hour)
            .Select(cell => new ConcurrencyPeak(cell.Value.Peak, cell.Key.Day, cell.Key.Hour))
            .FirstOrDefault();

    /// <summary>
    /// One grid per week: the week's hours on calendar-day rows, read out of the same
    /// sweeps the tiles are summed from, and the refusals that fell inside the week.
    /// <para>
    /// Rows are local calendar days, hours 0–23 in clock order, so a row reads the way a
    /// day does. The week does not start at midnight, so the first row holds only the
    /// hours from the reset hour on and the last — an eighth calendar day — only the
    /// hours before it; the hours outside the week are simply not there, which the grid
    /// draws as not reported rather than as zero. Under the calendar fallback the reset
    /// hour is midnight and there are seven full rows.
    /// </para>
    /// <para>
    /// Empty when the sweeps found nothing at all, so the part can decline to draw an
    /// axis it has nothing to put on — the rule the weekly series are already under.
    /// </para>
    /// <para>
    /// <b>DST.</b> The week is 168 hours of UTC cut into local hours, so on the two days a
    /// year the clock skips or repeats an hour a row is an hour short or long. The cells
    /// are the local hours as the sweep cut them, which is what makes this a stated limit
    /// rather than a mis-attribution — the choice <see cref="LocalHourBuckets"/> already
    /// made.
    /// </para>
    /// </summary>
    private static IReadOnlyList<WeekGrid> Grids(
        IReadOnlyList<WeekBucket> buckets,
        IReadOnlyList<AssistantActivitySession> sessions,
        IReadOnlyList<AssistantLimitHit> limits,
        IReadOnlyDictionary<HourCell, HourReading> active,
        IReadOnlyDictionary<HourCell, HourReading> waiting,
        IReadOnlyDictionary<HourCell, HourReading> open,
        IReadOnlyDictionary<HourCell, HourReading> agents,
        WorkingHours week,
        TimeZoneInfo zone)
    {
        if (active.Count == 0 && waiting.Count == 0) return [];

        // Which distinct sessions touched which cell, once for every week, so a row's
        // count is a set size rather than a sum of peaks.
        var ran = new Dictionary<HourCell, HashSet<string>>();

        foreach (var session in sessions)
        {
            foreach (var interval in session.Active)
            {
                foreach (var (cell, length) in LocalHourBuckets.Pieces(interval.From, interval.To, zone))
                {
                    if (length <= TimeSpan.Zero) continue;

                    if (!ran.TryGetValue(cell, out var seen))
                    {
                        seen = new HashSet<string>(StringComparer.Ordinal);
                        ran[cell] = seen;
                    }

                    seen.Add(session.Id);
                }
            }
        }

        return
        [
            .. buckets.Select(bucket =>
            {
                var start = TimeZoneInfo.ConvertTime(bucket.Start, zone).DateTime;
                var end = bucket.Start.AddDays(LocalHourBuckets.Days);

                // Every local hour of the week, in order: 168 of them, from the reset
                // hour of the first day to the hour before it on the last.
                var cells = Enumerable.Range(0, LocalHourBuckets.Days * 24)
                    .Select(offset => start.AddHours(offset))
                    .Select(at => new HourCell(DateOnly.FromDateTime(at), at.Hour))
                    .ToList();

                var hours = cells.Select(cell =>
                {
                    var worked = At(active, cell);
                    var waited = At(waiting, cell);

                    return new ActivityHour(
                        cell.Day,
                        cell.Hour,
                        worked.Peak,
                        worked.Total,
                        waited.Total,
                        At(open, cell).Peak,
                        week.Covers(cell.Day.DayOfWeek, cell.Hour))
                    {
                        PeakAgents = At(agents, cell).Peak
                    };
                }).ToList();

                var days = cells
                    .GroupBy(cell => cell.Day)
                    .OrderBy(group => group.Key)
                    .Select(group =>
                    {
                        var all = new HashSet<string>(StringComparer.Ordinal);
                        var inHours = new HashSet<string>(StringComparer.Ordinal);
                        var outside = new HashSet<string>(StringComparer.Ordinal);

                        foreach (var cell in group)
                        {
                            if (!ran.TryGetValue(cell, out var seen)) continue;

                            all.UnionWith(seen);
                            (week.Covers(cell.Day.DayOfWeek, cell.Hour) ? inHours : outside).UnionWith(seen);
                        }

                        return new ActivityDay(group.Key, all.Count, inHours.Count, outside.Count);
                    })
                    .ToList();

                var marks = limits
                    .Where(hit => hit.At >= bucket.Start && hit.At < end)
                    .OrderBy(hit => hit.At)
                    .Select(hit =>
                    {
                        var local = TimeZoneInfo.ConvertTime(hit.At, zone).DateTime;

                        // How far the wall reached: to the hour the reset fell in, and
                        // no further than this week's last hour. A weekly refusal's reset
                        // is the next week, so it gets no reach at all.
                        DateTime? until = hit.Kind == AssistantLimitKind.FiveHour
                            ? TimeZoneInfo.ConvertTime(hit.ResetsAt < end ? hit.ResetsAt : end.AddSeconds(-1), zone).DateTime
                            : null;

                        return new LimitMark(DateOnly.FromDateTime(local), local.Hour, hit.Kind, hit.At, hit.ResetsAt)
                        {
                            UntilDay = until is { } reach ? DateOnly.FromDateTime(reach) : null,
                            UntilHour = until?.Hour
                        };
                    })
                    .ToList();

                return new WeekGrid(bucket.Key, bucket.Label, bucket.Start, hours, days, marks);
            })
        ];
    }

    private static HourReading At(IReadOnlyDictionary<HourCell, HourReading> cells, HourCell cell) =>
        cells.TryGetValue(cell, out var found) ? found : HourReading.Nothing;

    private static IEnumerable<(DateTimeOffset From, DateTimeOffset To)> Intervals(
        IEnumerable<AssistantActivitySession> sessions,
        Func<AssistantActivitySession, IReadOnlyList<AssistantActivityInterval>> of) =>
        sessions.SelectMany(of).Select(interval => (interval.From, interval.To));

    /// <summary>
    /// Whether any part of this session falls inside the window. Both halves are
    /// needed: the last activity has to have happened after the window opened, and the
    /// session has to have begun before it closed. A session with no recorded start is
    /// admitted on its last activity alone — it happened, and the only thing not known
    /// about it is how long it took.
    /// </summary>
    private static bool InWindow(AssistantSession session, DateTimeOffset from, DateTimeOffset to) =>
        session.LastActivityAt >= from && (session.StartedAt ?? session.LastActivityAt) < to;

    /// <summary>
    /// What a set of cells adds up to. The tile is the sum of the sweep rather than a
    /// second pass over the intervals, so the number above the grid and the numbers in
    /// it cannot have been measured two different ways.
    /// </summary>
    private static TimeSpan Sum(IReadOnlyDictionary<HourCell, HourReading> cells) =>
        cells.Values.Aggregate(TimeSpan.Zero, (running, cell) => running + cell.Total);

    /// <summary>
    /// The breakdown answers whichever question the filter has not already answered:
    /// by machine while every machine is in view, by assistant once one is focused. A
    /// machine column under a machine filter would be a restatement of the control the
    /// reader just used.
    /// <para>
    /// Grouped by the machine's id and labelled with its name, never grouped by the
    /// name — the rule the session list already follows. Two machines that happen to
    /// share a name are two rows, because they are two machines, and the group's own key
    /// travels onto the row so the table can tell those two rows apart as well.
    /// </para>
    /// <para>
    /// The rows are the <em>sessions</em>' groups, not the activity's. A row has to be
    /// able to say "eight sessions, no recorded activity"; if the activity list decided
    /// which rows existed, a machine whose transcripts could not be parsed would vanish
    /// from a table that is counting its sessions in the tile above.
    /// </para>
    /// </summary>
    private static IReadOnlyList<AssistantSessionRow> Breakdown(
        IReadOnlyList<AssistantSession> scoped,
        IReadOnlyList<AssistantActivitySession> scopedActivity,
        DashboardScope scope,
        DateTimeOffset from,
        DateTimeOffset to,
        TimeZoneInfo zone)
    {
        var byKey = scopedActivity
            .GroupBy(session => Key(session, scope), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<AssistantActivitySession>)[.. group],
                StringComparer.Ordinal);

        return
        [
            .. scoped
                .GroupBy(session => scope.IsAllMachines ? session.MachineId : session.Assistant, StringComparer.Ordinal)
                .Select(group =>
                {
                    var theirs = byKey.TryGetValue(group.Key, out var found) ? found : [];

                    return new AssistantSessionRow(
                        group.Key,
                        scope.IsAllMachines ? group.First().MachineName : group.Key,
                        group.Count(),
                        Sum(LocalHourBuckets.Sweep(
                            Intervals(theirs, session => session.Active), from, to, zone)),
                        group.Max(session => session.LastActivityAt))
                    {
                        Waiting = Sum(LocalHourBuckets.Sweep(
                            Intervals(theirs, session => session.Waiting), from, to, zone))
                    };
                })
                // Busiest first, because that is the row the reader is looking for; ties
                // by name so the order does not move between two refreshes of the same
                // figures.
                .OrderByDescending(row => row.Sessions)
                .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>The same grouping key the session rows use, read off an activity record
    /// instead — so the two lists group on the same thing and a row finds its own
    /// activity rather than somebody else's.</summary>
    private static string Key(AssistantActivitySession session, DashboardScope scope) =>
        scope.IsAllMachines ? session.MachineId : session.Assistant;

    private static bool Matches(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);
}
