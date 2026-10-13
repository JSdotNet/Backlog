using System.Text.Json;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// When the agents on this machine were actually producing, read out of the bodies of
/// the transcripts <see cref="LocalAgentSessionSource"/> only ever stats.
/// <para>
/// The expensive half of the pair, and separate from it on purpose — see
/// <see cref="IAgentActivitySource"/> for why that is two ports rather than a flag.
/// Everything about the shape of this class is the same as its cheap sibling's: two
/// readers asked independently, their failures collected rather than thrown, every
/// record stamped with this machine's identity because a session found here ran here.
/// </para>
/// <para>
/// <b>The clock never decides a figure.</b> The horizon arrives as a parameter, which
/// keeps "how far back" a decision the caller states rather than one buried here, and
/// every instant in the answer is one a file recorded. The <see cref="TimeProvider"/> is
/// asked one thing only: whether a file's write time is far enough behind the read that
/// the file is no longer being written — which decides what may be remembered, never
/// what is answered.
/// </para>
/// <para>
/// <b>It remembers what it read.</b> One instance serves every consumer for the app's
/// lifetime, and it keeps what each file was found to hold, by path, so a refresh opens
/// only the files that moved and reads only what was appended to them; see
/// <see cref="EntryOf"/>. The memory is safe to share: a read only ever replaces an
/// entry with the same file read again, and one that loses a race repeats work rather
/// than spoiling it.
/// </para>
/// <para>
/// The cache is optional and used through a null-conditional, on
/// <c>GitHubActivityClient</c>'s precedent: it is an optimisation, and a host that
/// composed none still gets the same figures, slowly.
/// </para>
/// </summary>
internal sealed class LocalAgentActivitySource : IAgentActivitySource
{
    private readonly string _claudeHome;
    private readonly string _copilotHome;
    private readonly string _environmentId;
    private readonly string _environment;
    private readonly TimeProvider _clock;

    /// <summary>What every file this reader has read was found to hold, by path, and —
    /// for the few most recently written — where the read stopped and the events it
    /// left. In front of the disk cache, which stays the layer that survives a restart.
    /// The rules it remembers on are the session reader's too, so they have one home:
    /// <see cref="ResumableFileMemory{TFold, TValue}"/>. Null is what a file that could
    /// not be read answers, and is never remembered.</summary>
    private readonly ResumableFileMemory<IActivityEventParser, AgentActivityEntry?> _remembered;

    /// <summary>What a host composes: the two agents' own folders in the profile of
    /// whoever is signed in, this device's identity, and whatever cache the host
    /// registered.</summary>
    internal LocalAgentActivitySource(IDeviceIdentitySource identity, IAgentActivityCache? cache = null)
        : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot"),
            IdOf(identity),
            identity.Current.Name,
            cache)
    {
    }

    /// <summary>Every input named, so the fold can be tested against fixture folders
    /// written in the shape the agents really write rather than against whatever this
    /// machine happens to have been doing.
    /// <para>
    /// The opener and the clock are a test's seams, so a test can count what a read opens
    /// and how much of it, and say when "now" is; a host leaves them to
    /// <see cref="TranscriptTail.OpenShared"/> and the system clock.
    /// </para></summary>
    internal LocalAgentActivitySource(
        string claudeHome,
        string copilotHome,
        string environmentId,
        string environment,
        IAgentActivityCache? cache = null,
        Func<string, Stream>? openTranscript = null,
        TimeProvider? clock = null)
    {
        _claudeHome = claudeHome;
        _copilotHome = copilotHome;
        _environmentId = environmentId;
        _environment = environment;
        _clock = clock ?? TimeProvider.System;

        // The disk keys on the write time alone, so the length the memory passes it is
        // not part of what is stored.
        _remembered = new ResumableFileMemory<IActivityEventParser, AgentActivityEntry?>(
            openTranscript ?? TranscriptTail.OpenShared,
            events => Folded(events.Events),
            _ => null,
            (path, _, writtenAt) => cache?.TryRead(path, writtenAt, AgentActivityRuns.IdleAfter),
            (path, _, writtenAt, entry) =>
            {
                if (entry is not null) cache?.Write(path, writtenAt, entry);
            });
    }

    /// <summary>The device id as this contract carries identifiers: a string, in the
    /// Guid's plain lower-case "D" form. Formatted the way
    /// <see cref="LocalAgentSessionSource"/> formats it, because an activity record and
    /// a session row are meant to be the same machine by identity and two spellings of
    /// one id is exactly how that stops being true.</summary>
    private static string IdOf(IDeviceIdentitySource identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        return identity.Current.Id.ToString();
    }

    public async Task<AgentActivityLog> GetActivityAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        var sessions = new List<AgentSessionActivity>();
        var subagents = new List<SubagentActivity>();
        var unreadable = new List<string>();

        var walk = new Walk(since, _clock.GetUtcNow(), new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        foreach (var reader in Readers(walk, cancellationToken))
        {
            try
            {
                var read = await reader.Read().ConfigureAwait(false);

                sessions.AddRange(read.Sessions);
                subagents.AddRange(read.Subagents);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                // The three ways reading somebody else's folder goes wrong, and the same
                // answer LocalAgentSessionSource gives them: this agent's name on the
                // list, the other agent's answer intact. A read that threw here would
                // cost the whole grid over one folder's permissions.
                unreadable.Add(reader.Name);
            }
        }

        // After both agents, so a file either walk returned is kept. A walk that failed
        // part way forgets what it did not reach, which costs those files a read and
        // never a wrong answer.
        _remembered.Prune(walk.Seen);

        return new AgentActivityLog(sessions, unreadable, since, AgentActivityRuns.IdleAfter)
        {
            Subagents = subagents
        };
    }

    /// <summary>
    /// What one agent's reader produced: the sessions it found, and whatever those
    /// sessions spawned.
    /// <para>
    /// One return rather than a second pass, so both halves sit inside the same guard
    /// above and a catastrophic failure names that agent exactly once. Two calls would
    /// let one agent appear twice on a list the surface prints as a sentence.
    /// </para>
    /// </summary>
    private sealed record AgentRead(
        IReadOnlyList<AgentSessionActivity> Sessions,
        IReadOnlyList<SubagentActivity> Subagents);

    private (string Name, Func<Task<AgentRead>> Read)[] Readers(
        Walk walk,
        CancellationToken cancellationToken) =>
    [
        (ClaudeSessionReader.Name, () => ReadClaudeAsync(walk, cancellationToken)),
        (CopilotSessionReader.Name, () => ReadCopilotAsync(walk, cancellationToken))
    ];

    /// <summary>
    /// Claude's history, one transcript per session — the same files the session list
    /// walks, deduped by the same rule, through the same helper. Two copies of that rule
    /// is what <see cref="ClaudeTranscripts"/> exists to prevent.
    /// </summary>
    private async Task<AgentRead> ReadClaudeAsync(
        Walk walk,
        CancellationToken cancellationToken)
    {
        var sessions = new List<AgentSessionActivity>();

        foreach (var (id, transcript) in ClaudeTranscripts.Newest(_claudeHome))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var activity = await ActivityOf(
                id,
                AgentSessionKind.Claude,
                transcript,
                () => new ClaudeTranscriptEvents(),
                walk,
                cancellationToken).ConfigureAwait(false);

            if (activity is not null) sessions.Add(activity);
        }

        return new AgentRead(sessions, await ReadClaudeSubagentsAsync(walk, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// The agents those sessions spawned, out of the sidechain transcripts filed under
    /// each session's own folder — <see cref="ClaudeTranscripts.Spawned"/> decides which
    /// files those are, from the same place the session rule lives.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything expensive about this read is inherited rather than restated. The same
    /// mtime skip keeps 1,146 extra files bounded by not opening them, the same cache
    /// entry means a finished sidechain is parsed once ever, and the same whole-file fold
    /// before clipping is what keeps moving the period control a derivation. A separate
    /// path here would be a second copy of all three, free to lose any of them quietly.
    /// </para>
    /// <para>
    /// <see cref="ClaudeTranscriptEvents"/> is reused unchanged, and that is
    /// checked rather than assumed: the only line types a sidechain file holds are
    /// <c>user</c>, <c>assistant</c> and <c>attachment</c>, which is exactly the closed
    /// list that reader admits.
    /// </para>
    /// <para>
    /// The fold's waits are dropped here, the way the grid drops the open sweep's totals.
    /// The cache may hold them — they are an output of a shared fold — but the contract
    /// may not, because a list of waits on a subagent would be a published claim that one
    /// can wait, and no sidechain on any profile carries the field that would evidence it.
    /// </para>
    /// <para>
    /// The human turns are dropped here too. A subagent is the agent's work, never the
    /// person's turn, so it starts no working stretch (ADR 0019 §6).
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<SubagentActivity>> ReadClaudeSubagentsAsync(
        Walk walk,
        CancellationToken cancellationToken)
    {
        var agents = new List<SubagentActivity>();

        foreach (var (sessionId, agentId, transcript) in ClaudeTranscripts.Spawned(_claudeHome))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = await EntryOf(
                transcript,
                () => new ClaudeTranscriptEvents(),
                walk,
                cancellationToken).ConfigureAwait(false);

            if (entry is null) continue;

            var runs = Clip(entry.Runs, walk.Since);
            var hits = Clip(entry.LimitHits, walk.Since);

            // Absent rather than present-and-empty, the rule the sessions are already
            // read under: an agent whose whole record fell outside the horizon has
            // nothing to contribute to a concurrency figure, and an entry with no
            // intervals would be an agent claiming to have been measured. A refusal
            // inside the horizon is a record, though, and keeps the agent.
            if (runs.Count == 0 && hits.Count == 0) continue;

            agents.Add(new SubagentActivity(
                agentId,
                sessionId,
                AgentSessionKind.Claude,
                _environmentId,
                _environment,
                runs)
            {
                LimitHits = hits
            });
        }

        return agents;
    }

    /// <summary>
    /// Copilot's history, one folder per session. No dedupe rule here and none needed:
    /// the folder <em>is</em> the session id, so a session cannot be filed twice the way
    /// a Claude transcript can.
    /// <para>
    /// And no subagents, ever. Copilot spawns none, so an empty list here is a fact about
    /// Copilot rather than a path nobody got round to writing — inventing one would mean
    /// guessing at a folder shape that does not exist.
    /// </para>
    /// </summary>
    private async Task<AgentRead> ReadCopilotAsync(
        Walk walk,
        CancellationToken cancellationToken)
    {
        var folder = new DirectoryInfo(Path.Combine(_copilotHome, "session-state"));

        if (!folder.Exists) return new AgentRead([], []);

        var sessions = new List<AgentSessionActivity>();

        foreach (var session in folder.EnumerateDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var events = new FileInfo(Path.Combine(session.FullName, "events.jsonl"));

            // 468 of the 691 session folders on this machine hold no events file at all.
            // Nothing to report is not the same as a session that reported nothing, and
            // the surface counts the difference.
            if (!events.Exists) continue;

            var activity = await ActivityOf(
                session.Name,
                AgentSessionKind.Copilot,
                events,
                () => new CopilotEventStream(),
                walk,
                cancellationToken).ConfigureAwait(false);

            if (activity is not null) sessions.Add(activity);
        }

        return new AgentRead(sessions, []);
    }

    /// <summary>
    /// One session's record, or null when it has nothing to say inside the horizon.
    /// Clipping and the record itself; everything before that is
    /// <see cref="EntryOf"/>'s, which the subagents go through as well.
    /// </summary>
    private async Task<AgentSessionActivity?> ActivityOf(
        string id,
        AgentSessionKind kind,
        FileInfo file,
        Func<IActivityEventParser> parser,
        Walk walk,
        CancellationToken cancellationToken)
    {
        var entry = await EntryOf(file, parser, walk, cancellationToken).ConfigureAwait(false);

        if (entry is null) return null;

        var runs = Clip(entry.Runs, walk.Since);
        var waits = Clip(entry.Waits, walk.Since);
        var hits = Clip(entry.LimitHits, walk.Since);
        var turns = Clip(entry.HumanTurns, walk.Since);

        // Absent rather than present-and-empty. A session whose whole record fell outside
        // the horizon, or that left one lone event, has nothing to contribute to a
        // duration — and the session list is already the place that says it existed.
        // One lone refusal is the exception: it contributes no duration and is still a
        // thing that happened to the session, so a session that was refused and nothing
        // else is present with that one fact on it. A lone human turn is one too.
        return runs.Count == 0 && waits.Count == 0 && hits.Count == 0 && turns.Count == 0
            ? null
            : new AgentSessionActivity(id, kind, _environmentId, _environment, runs, waits)
            {
                LimitHits = hits,
                HumanTurns = turns
            };
    }

    /// <summary>
    /// One file's whole-file fold, or null when it was skipped or could not be read.
    /// <para>
    /// The mtime test is what makes a twelve-week read bounded rather than all-time, and
    /// it is safe because a file's last write is its last event's own upper bound: a
    /// transcript nobody has touched since before the horizon cannot hold an event
    /// inside it. Skipped without being opened, which is the whole saving.
    /// </para>
    /// <para>
    /// The read is guarded per file, for the reason the session readers state at their
    /// own: the agent is appending to these while this runs, and an IOException let out
    /// of here would cost every record of that agent and report the agent as unreadable
    /// into the bargain. One record is the proportionate answer to one locked file.
    /// </para>
    /// <para>
    /// Whole-file and unclipped, which is what lets both callers share it. A session and
    /// an agent it spawned differ in what they build out of the fold and in nothing before
    /// it, so the mtime skip, the cache and the guard have one home rather than two free
    /// to drift — and the second read inherits every one of them by construction.
    /// </para>
    /// <para>
    /// Past the mtime skip, a file costs what it costs once rather than on every read,
    /// three ways, in the order and on the rules
    /// <see cref="ResumableFileMemory{TFold, TValue}"/> keeps for both readers of these
    /// files: in memory, at the same length and write time; from where the last read
    /// stopped, for the files written in the last day, so the transcript being written to
    /// right now — the file whose figure must not be stale, and which misses every other
    /// way — costs what was appended, its runs folded again over every event it holds;
    /// and on disk, when a host composes an <see cref="IAgentActivityCache"/>, which is
    /// what survives a restart.
    /// </para>
    /// <para>
    /// The length is part of the memory's key though the disk's has none: two writes in
    /// one tick of the file system's clock are told apart by it. It is also why only a
    /// pass over a settled file that read every line of it is written to disk — keyed on
    /// the write time alone, a pass that stopped in front of half a line would be
    /// remembered as the whole file.
    /// </para>
    /// </summary>
    private async Task<AgentActivityEntry?> EntryOf(
        FileInfo file,
        Func<IActivityEventParser> parser,
        Walk walk,
        CancellationToken cancellationToken)
    {
        var writtenAt = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);

        // Seen before it is skipped: a file outside this horizon is still the walk's, and
        // a longer read asked for next keeps what this reader knows about it.
        walk.Seen.Add(file.FullName);

        if (writtenAt < walk.Since) return null;

        return await _remembered.ReadAsync(file, parser, walk.Now, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// What a file's events come to: the runs and waits the fold finds in them, and the
    /// refusals and human turns lifted off them.
    /// <para>
    /// Whole-file, before any horizon is applied, because the horizon moves and the file
    /// does not: the twelve-week read and the four-week one share entries and only the
    /// clipping differs. The hits are lifted off the events here rather than inside the
    /// fold, which never looks at them: sorted, so the list is ascending whatever order
    /// the file recorded them in.
    /// </para>
    /// </summary>
    private static AgentActivityEntry Folded(IReadOnlyList<ActivityEvent> events)
    {
        var folded = AgentActivityRuns.Fold(events, AgentActivityRuns.IdleAfter);

        return new AgentActivityEntry(folded.Runs, folded.Waits, AgentActivityRuns.IdleAfter)
        {
            LimitHits =
            [
                .. events
                    .Where(activity => activity.Limit is not null)
                    .Select(activity => activity.Limit!)
                    .OrderBy(hit => hit.At)
            ],
            // The person's turns, lifted off the same way and for the same reason.
            HumanTurns =
            [
                .. events
                    .Where(activity => activity.IsHumanTurn)
                    .Select(activity => activity.At)
                    .Distinct()
                    .Order()
            ]
        };
    }

    /// <summary>
    /// Clipped to the horizon rather than dropped at it, so a session that began before
    /// the window reports the part of itself inside it. An interval that ended exactly on
    /// the horizon has no part inside and goes, rather than becoming a zero-length one.
    /// </summary>
    private static IReadOnlyList<AgentActivityRun> Clip(
        IReadOnlyList<AgentActivityRun> runs,
        DateTimeOffset since) =>
    [
        .. runs
            .Where(run => run.EndedAt > since)
            .Select(run => run.StartedAt >= since ? run : new AgentActivityRun(since, run.EndedAt))
    ];

    /// <inheritdoc cref="Clip(IReadOnlyList{AgentActivityRun}, DateTimeOffset)"/>
    private static IReadOnlyList<AgentActivityWait> Clip(
        IReadOnlyList<AgentActivityWait> waits,
        DateTimeOffset since) =>
    [
        .. waits
            .Where(wait => wait.EndedAt > since)
            .Select(wait => wait.StartedAt >= since ? wait : new AgentActivityWait(since, wait.EndedAt))
    ];

    /// <summary>An instant has no part to clip: a hit is inside the horizon or it is
    /// not. On the horizon counts as inside, the way a run starting there does.</summary>
    private static IReadOnlyList<AgentLimitHit> Clip(
        IReadOnlyList<AgentLimitHit> hits,
        DateTimeOffset since) =>
    [
        .. hits.Where(hit => hit.At >= since)
    ];

    /// <summary>A human turn is an instant too, and is clipped on the hits' terms.</summary>
    private static IReadOnlyList<DateTimeOffset> Clip(
        IReadOnlyList<DateTimeOffset> turns,
        DateTimeOffset since) =>
    [
        .. turns.Where(turn => turn >= since)
    ];

    /// <summary>One call's reading: the horizon it was asked for, the instant it began,
    /// and every file its walks returned, which is what the memory is pruned to.</summary>
    private sealed record Walk(DateTimeOffset Since, DateTimeOffset Now, HashSet<string> Seen);
}
