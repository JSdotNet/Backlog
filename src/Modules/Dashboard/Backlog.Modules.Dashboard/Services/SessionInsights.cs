using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.Modules.Dashboard.Abstractions.Services;

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
/// its key carries the focus. This source is a local read that already returns
/// everything it will ever return, so narrowing is a pure function over the report and
/// the reader can move the machine filter or the window without touching the disk
/// again. It is also why <see cref="Invalidate"/> takes no scope: there is nothing
/// per-scope to drop.
/// </para>
/// <para>
/// Every figure understates rather than invents. A session is clipped to the window
/// rather than counted whole, a session with no recorded start contributes no duration
/// at all, and a capped or partly unreadable source travels as such all the way to the
/// screen. Where a number could be guessed, it is not.
/// </para>
/// <para>
/// Availability is asked before data and a throw is turned into the same
/// unavailable-with-a-reason answer, exactly as the productivity derivation does. One
/// source having a bad minute must not take the surface down, and the reader needs a
/// sentence either way. It is cached with the report rather than beside it, so the claim
/// above holds for the whole call and not only for its second half: moving the machine
/// filter asks the source nothing at all until the reader refreshes.
/// </para>
/// </remarks>
public sealed class SessionInsights(IAssistantSessionSource sessions, TimeProvider time) : ISessionInsights
{
    /// <summary>The only key there is. Named rather than empty so the entry reads as a
    /// deliberate single entry in a cache that holds several kinds.</summary>
    private const string CacheKey = "sessions|all";

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
                ? InsightResult<AssistantSessionsInsight>.Ready(Derive(report, scope))
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
    /// One trip to the source: whether it can answer, and — when it can — everything it
    /// has. Both under one cache entry, so a refusal is remembered for as long as the
    /// report would have been and a scope change costs neither call.
    /// </summary>
    private async Task<Reading> ReadAsync(CancellationToken cancellationToken)
    {
        var availability = await sessions.GetAvailabilityAsync(cancellationToken).ConfigureAwait(false);

        return availability.IsAvailable
            ? new Reading(availability, await sessions.GetSessionsAsync(cancellationToken).ConfigureAwait(false))
            : new Reading(availability, null);
    }

    /// <summary>What one read produced. The report is null exactly when the source said
    /// it could not answer, which is the only combination either half is ever in.</summary>
    private sealed record Reading(InsightAvailability Availability, AssistantSessionReport? Report);

    /// <summary>
    /// The whole derivation: filter to the scope, then measure. Synchronous and pure,
    /// which is what lets every arithmetic decision below be asserted without a source
    /// behind it.
    /// </summary>
    private AssistantSessionsInsight Derive(AssistantSessionReport report, DashboardScope scope)
    {
        var (from, to) = scope.Window(time.GetUtcNow());

        var scoped = report.Sessions
            .Where(session => InWindow(session, from, to))
            .Where(session => scope.IsAllMachines || Matches(session.MachineId, scope.MachineId))
            .ToList();

        return new AssistantSessionsInsight(
            scoped.Count,
            Total(scoped, from, to),
            scoped.Count == 0 ? null : scoped.Max(session => session.LastActivityAt),
            scoped.Count(session => session.StartedAt is null),
            report.Capped,
            report.CapPerAssistant,
            report.Unreadable,
            Breakdown(scoped, scope, from, to))
        {
            // Bucketed on the last activity, which is the only instant every session
            // has: the start is optional and there is no end at all. A session that
            // ran across a week boundary is therefore one mark in the week it last
            // moved rather than a mark in each, and the part says that beside the
            // columns.
            SessionsPerWeek = WeekBuckets.Count(
                WeekBuckets.Buckets(from, to),
                scoped,
                session => session.LastActivityAt)
        };
    }

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
    /// Time inside the window, summed over the sessions that can report one.
    /// <para>
    /// Clipped rather than counted whole, so a session that ran across the edge of the
    /// window contributes only the part of it the reader is looking at — otherwise two
    /// readings of the same quarter would disagree depending on what came before it.
    /// Overlapping sessions are summed rather than merged: this is time spent working
    /// with the assistants, not wall-clock time during which one was open, and two
    /// agents running at once is two sessions' worth of work.
    /// </para>
    /// </summary>
    private static TimeSpan Total(IEnumerable<AssistantSession> scoped, DateTimeOffset from, DateTimeOffset to) =>
        scoped.Aggregate(TimeSpan.Zero, (running, session) => running + Duration(session, from, to));

    private static TimeSpan Duration(AssistantSession session, DateTimeOffset from, DateTimeOffset to)
    {
        // No start, no duration. A zero-length session would be a fact nobody
        // recorded; the count says it happened and WithoutStart says why it is not in
        // this total.
        if (session.StartedAt is not { } started) return TimeSpan.Zero;

        var opened = started > from ? started : from;
        var closed = session.LastActivityAt < to ? session.LastActivityAt : to;

        return closed > opened ? closed - opened : TimeSpan.Zero;
    }

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
    /// </summary>
    private static IReadOnlyList<AssistantSessionRow> Breakdown(
        IReadOnlyList<AssistantSession> scoped,
        DashboardScope scope,
        DateTimeOffset from,
        DateTimeOffset to) =>
    [
        .. scoped
            .GroupBy(session => scope.IsAllMachines ? session.MachineId : session.Assistant, StringComparer.Ordinal)
            .Select(group => new AssistantSessionRow(
                group.Key,
                scope.IsAllMachines ? group.First().MachineName : group.Key,
                group.Count(),
                Total(group, from, to),
                group.Max(session => session.LastActivityAt)))
            // Busiest first, because that is the row the reader is looking for; ties
            // by name so the order does not move between two refreshes of the same
            // figures.
            .OrderByDescending(row => row.Sessions)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
    ];

    private static bool Matches(string? left, string? right) =>
        string.Equals(left, right, StringComparison.Ordinal);
}
