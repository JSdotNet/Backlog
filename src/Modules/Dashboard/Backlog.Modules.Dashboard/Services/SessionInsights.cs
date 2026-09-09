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
    /// </summary>
    private static readonly TimeSpan Horizon = TimeSpan.FromDays(7 * 12);

    private readonly InsightCache _cache = new();

    public async Task<InsightResult<AssistantSessionsInsight>> GetSessionsAsync(
        DashboardScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);

        try
        {
            var reading = await _cache.GetOrAddAsync(CacheKey, () => ReadAsync(cancellationToken)).ConfigureAwait(false);

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

        var report = await sessions.GetSessionsAsync(cancellationToken).ConfigureAwait(false);

        var log = await activity
            .GetActivityAsync(time.GetUtcNow() - Horizon, cancellationToken)
            .ConfigureAwait(false);

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

        return new AssistantSessionsInsight(
            scoped.Count,
            Sum(active),
            scoped.Count == 0 ? null : scoped.Max(session => session.LastActivityAt),
            scoped.Count(session => !recorded.Contains(session.Id)),
            report.Capped,
            report.CapPerAssistant,
            report.Unreadable,
            Breakdown(scoped, scopedActivity, scope, from, to, zone))
        {
            // Bucketed on the last activity, which is the only instant every session
            // has: the start is optional and there is no end at all. A session that
            // ran across a week boundary is therefore one mark in the week it last
            // moved rather than a mark in each, and the part says that beside the
            // columns.
            SessionsPerWeek = WeekBuckets.Count(
                WeekBuckets.Buckets(from, to),
                scoped,
                session => session.LastActivityAt),
            Waiting = Sum(waiting),
            MostSessionsAtOnce = Busiest(active),
            MostAgentsAtOnce = Busiest(agents),
            ActivityByHour = Grid(active, waiting, open, agents, week, to, zone),
            ActivityByDay = Days(scopedActivity, active, waiting, week, to, zone),
            IdleAfter = reading.Activity.IdleAfter
        };
    }

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
    /// The grid: the last seven dated local days, every hour of them, read out of the
    /// sweeps.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Seven days, not the window.</b> Every other figure this class produces widens
    /// with the period control and this one does not — see
    /// <see cref="AssistantSessionsInsight.ActivityByHour"/> and
    /// <see cref="LocalHourBuckets.Cells"/> for why the fixed grid was chosen over a
    /// fold across weeks. The part is required to say so on screen; here it is enough
    /// that the asymmetry is deliberate and in one place.
    /// </para>
    /// <para>
    /// Empty when neither session sweep found anything, so the part can decline to draw an
    /// axis with nothing on it. Otherwise every cell is present, including the ones nobody
    /// worked: a heatmap draws a zero and an absence differently, and an hour that
    /// happened and was quiet is a zero.
    /// </para>
    /// <para>
    /// <b>The agents deliberately do not open this gate.</b> A subagent exists because a
    /// session was producing, so a profile with agents and no session activity is not a
    /// thing that happens — and widening the test to include them would let a third grid
    /// appear beside two the part had declined to draw.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<ActivityHour> Grid(
        IReadOnlyDictionary<HourCell, HourReading> active,
        IReadOnlyDictionary<HourCell, HourReading> waiting,
        IReadOnlyDictionary<HourCell, HourReading> open,
        IReadOnlyDictionary<HourCell, HourReading> agents,
        WorkingHours week,
        DateTimeOffset to,
        TimeZoneInfo zone)
    {
        if (active.Count == 0 && waiting.Count == 0) return [];

        return
        [
            .. LocalHourBuckets.Cells(to, zone).Select(cell =>
            {
                var worked = At(active, cell);
                var waited = At(waiting, cell);

                // The totals of the open sweep are discarded, and the peak of the waiting
                // one with them. Open agent-hours would be a duration nobody asked for and
                // would read as a bigger version of the active time it is not comparable
                // to; a second peak travelling unused is a second peak somebody eventually
                // shades by mistake.
                return new ActivityHour(
                    cell.Day,
                    cell.Hour,
                    worked.Peak,
                    worked.Total,
                    waited.Total,
                    At(open, cell).Peak,
                    week.Covers(cell.Day.DayOfWeek, cell.Hour))
                {
                    // The agents' peak and not their total, for the reason the open sweep's
                    // total is dropped: agent-hours spawned is a duration nobody asked for
                    // and it would read as a bigger version of the active time it is not
                    // comparable to.
                    PeakAgents = At(agents, cell).Peak
                };
            })
        ];
    }

    /// <summary>
    /// How many distinct sessions ran on each of the grid's days.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Counted from the sessions themselves rather than from the sweeps, and that is the
    /// whole point of the method. The sweeps hold peaks and durations, neither of which
    /// can produce this: adding a day's twenty-four peaks counts a session once per hour
    /// it spanned, and a duration says nothing about how many things produced it. Only
    /// the interval's owner can answer "how many distinct", so the identity has to come
    /// from the session and not from the grid.
    /// </para>
    /// <para>
    /// Active intervals only. A session that spent a day waiting and never produced did
    /// not run that day, and counting it here would put a figure in the column that the
    /// shaded row beside it flatly contradicts.
    /// </para>
    /// <para>
    /// Drawn or not drawn with the grid, never on its own: the same emptiness test, so
    /// the column cannot appear beside an axis the part has declined to draw.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<ActivityDay> Days(
        IReadOnlyList<AssistantActivitySession> sessions,
        IReadOnlyDictionary<HourCell, HourReading> active,
        IReadOnlyDictionary<HourCell, HourReading> waiting,
        WorkingHours week,
        DateTimeOffset to,
        TimeZoneInfo zone)
    {
        if (active.Count == 0 && waiting.Count == 0) return [];

        var ran = new Dictionary<DateOnly, HashSet<string>>();
        var inHours = new Dictionary<DateOnly, HashSet<string>>();
        var outsideHours = new Dictionary<DateOnly, HashSet<string>>();

        foreach (var session in sessions)
        {
            foreach (var interval in session.Active)
            {
                // By the hour rather than by the day, because the split is an hour-level
                // question: a session that began at four and ran until eight was inside
                // the working day for one of those hours and outside it for the rest, and
                // a day-level test could only have said one of those.
                foreach (var cell in LocalHourBuckets.HoursTouched(interval.From, interval.To, to, zone))
                {
                    Count(ran, cell.Day, session.Id);

                    Count(
                        week.Covers(cell.Day.DayOfWeek, cell.Hour) ? inHours : outsideHours,
                        cell.Day,
                        session.Id);
                }
            }
        }

        return
        [
            .. LocalHourBuckets.Cells(to, zone)
                .Select(cell => cell.Day)
                .Distinct()
                .Select(day => new ActivityDay(day, Size(ran, day), Size(inHours, day), Size(outsideHours, day)))
        ];
    }

    private static void Count(Dictionary<DateOnly, HashSet<string>> into, DateOnly day, string session)
    {
        if (!into.TryGetValue(day, out var seen))
        {
            seen = new HashSet<string>(StringComparer.Ordinal);
            into[day] = seen;
        }

        seen.Add(session);
    }

    private static int Size(Dictionary<DateOnly, HashSet<string>> counted, DateOnly day) =>
        counted.TryGetValue(day, out var seen) ? seen.Count : 0;

    /// <summary>What a sweep found in one cell, or a zero where it found nothing. The
    /// substitution is the grid's rather than the sweep's, so "nothing landed here" stays
    /// one fact with one owner.</summary>
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
