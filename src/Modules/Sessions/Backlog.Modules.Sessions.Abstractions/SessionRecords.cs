namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// What this machine knows about one of its own sessions, kept after the agent's
/// files are gone: the session as last read, and what its transcript was last folded
/// into.
/// <para>
/// The record is this machine's, and independent of sync. The assistants clean their
/// transcripts away — Claude after a month by default — and a session read from
/// nothing is a session the log no longer holds, so every reading of a local session
/// amends its record here (<see cref="AgentSessionRecords.Amend"/>). Sync, when it is
/// on, carries the same session to the other machines; it is not where this machine
/// keeps its own.
/// </para>
/// </summary>
/// <param name="Session">The session as last read, with its working folder and title —
/// the record stays on this machine, so nothing here is subject to the sync
/// whitelist.</param>
/// <param name="Activity">What the transcript was folded into — runs, waits and limit
/// hits — or null where no reading ever folded one.</param>
/// <param name="RecordedAt">When a reading last amended the record.</param>
public sealed record AgentSessionRecord(AgentSession Session, AgentSessionActivity? Activity, DateTimeOffset RecordedAt);

/// <summary>
/// PORT — this machine's session records. One record per session, keyed on the
/// <c>Session Identity</c>: the agent and the session id together.
/// </summary>
public interface IAgentSessionRecordStore
{
    /// <summary>Every record held, in no promised order. An unreadable record is left
    /// out rather than failing the rest.</summary>
    IReadOnlyList<AgentSessionRecord> All();

    /// <summary>Amends each reading into the record it belongs to, or starts one.
    /// Never removes a record and never replaces one whole: see
    /// <see cref="AgentSessionRecords.Amend"/>. Answers how many records were started
    /// and how many amended.</summary>
    SessionRecordUpdate Save(IReadOnlyList<AgentSessionRecord> readings);
}

/// <summary>What one update did to the store.</summary>
/// <param name="Started">Sessions that had no record before.</param>
/// <param name="Amended">Sessions whose record a reading amended.</param>
/// <param name="Published">Whether sync was asked to send every record again.</param>
public sealed record SessionRecordUpdate(int Started, int Amended, bool Published = false)
{
    public static SessionRecordUpdate None { get; } = new(0, 0);
}

/// <summary>
/// PORT — keeps this machine's session records level with what its agents' files
/// still say.
/// </summary>
public interface ISessionRecordKeeper
{
    /// <summary>
    /// Reads this machine's sessions and amends their records.
    /// </summary>
    /// <param name="everything">Every session the files still hold, rather than those
    /// that moved since the newest record — and, where sync is on, every record sent
    /// again. What the Sessions pane's update button asks for.</param>
    Task<SessionRecordUpdate> UpdateAsync(bool everything, CancellationToken cancellationToken = default);
}

/// <summary>
/// PORT — asks sync to send every one of this machine's records again. Absent on a
/// host without sync, which is what keeps the records themselves independent of it.
/// </summary>
public interface ISessionRecordPublisher
{
    /// <summary>Requests a republish and answers whether sync is on to carry it.</summary>
    bool RepublishAll();
}

/// <summary>
/// How a reading amends a record: <b>added to, never taken from.</b>
/// <para>
/// A reading is what the agent's files say now, and they can say less than they once
/// did — a transcript cut short, a live file gone, a field an older agent did not
/// write. So a value the reading has wins, a value it lacks keeps what the record
/// held, and the lists keep what came before the reading's own start. The one thing
/// a reading replaces is the stretch it covers: its runs and waits from its first
/// instant on are the fold of the same lines the record's were, so keeping both would
/// count that time twice.
/// </para>
/// </summary>
public static class AgentSessionRecords
{
    public static AgentSessionRecord Amend(AgentSessionRecord? stored, AgentSessionRecord reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        if (stored is null) return reading;

        var held = stored.Session;
        var now = reading.Session;

        var session = now with
        {
            Title = string.IsNullOrWhiteSpace(now.Title) ? held.Title : now.Title,
            WorkingFolder = string.IsNullOrWhiteSpace(now.WorkingFolder) ? held.WorkingFolder : now.WorkingFolder,
            Repository = now.Repository ?? held.Repository,
            Branch = now.Branch ?? held.Branch,
            StartedAt = Earliest(now.StartedAt, held.StartedAt),
            LastActivityAt = now.LastActivityAt >= held.LastActivityAt ? now.LastActivityAt : held.LastActivityAt,
            TurnCount = Largest(now.TurnCount, held.TurnCount),
            ResolvedRepository = now.ResolvedRepository ?? held.ResolvedRepository,
            WorktreeKey = now.WorktreeKey ?? held.WorktreeKey,
            Entrypoint = now.Entrypoint ?? held.Entrypoint,
            // Every pull request either reading linked: a link is a fact that stays true.
            PullRequests = now.PullRequests is null ? held.PullRequests
                : held.PullRequests is null ? now.PullRequests
                : [.. now.PullRequests.Concat(held.PullRequests).DistinctBy(pr => pr.Url, StringComparer.OrdinalIgnoreCase).OrderBy(pr => pr.LinkedAt ?? DateTimeOffset.MaxValue)],
            // A sum over the transcript: a shortened one sums less, so each figure keeps
            // the larger of the two readings, per model.
            ModelUsage = now.ModelUsage is null ? held.ModelUsage
                : held.ModelUsage is null ? now.ModelUsage
                : [.. now.ModelUsage.Concat(held.ModelUsage)
                    .GroupBy(usage => usage.Model, StringComparer.Ordinal)
                    .Select(model => new AgentModelUsage(
                        model.Key,
                        model.Max(usage => usage.InputTokens),
                        model.Max(usage => usage.OutputTokens),
                        model.Max(usage => usage.CacheCreationInputTokens),
                        model.Max(usage => usage.CacheReadInputTokens)))
                    .OrderBy(usage => usage.Model, StringComparer.Ordinal)]
        };

        return new AgentSessionRecord(session, Amend(stored.Activity, reading.Activity), reading.RecordedAt);
    }

    private static AgentSessionActivity? Amend(AgentSessionActivity? held, AgentSessionActivity? now)
    {
        if (now is null) return held;
        if (held is null) return now;

        var from = Earliest(now.Runs.FirstOrDefault()?.StartedAt, now.Waits.FirstOrDefault()?.StartedAt);

        return now with
        {
            Runs = [.. Before(held.Runs, run => run.EndedAt, from), .. now.Runs],
            Waits = [.. Before(held.Waits, wait => wait.EndedAt, from), .. now.Waits],
            // An instant, so there is no stretch to double: every hit either reading
            // held, the newer reading's fields winning for a hit both hold.
            LimitHits =
            [
                .. now.LimitHits
                    .Concat(held.LimitHits)
                    .DistinctBy(hit => (hit.At, hit.Kind))
                    .OrderBy(hit => hit.At)
            ]
        };
    }

    /// <summary>The held intervals that ended before the reading's first began — all of
    /// them where the reading has none.</summary>
    private static IEnumerable<T> Before<T>(IReadOnlyList<T> held, Func<T, DateTimeOffset> end, DateTimeOffset? from) =>
        from is { } start ? held.Where(interval => end(interval) <= start) : held;

    private static DateTimeOffset? Earliest(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : a < b ? a : b;

    private static int? Largest(int? a, int? b) =>
        a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
}
