using System.Text.Json;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Modules.Sessions.UI.Adapters;

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
/// <b>No <see cref="TimeProvider"/>.</b> This source reads recorded instants and never
/// asks what time it is. The horizon arrives as a parameter, which is what makes it
/// testable without a clock at all — and what keeps "how far back" a decision the
/// caller states rather than one buried here.
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
    private readonly IAgentActivityCache? _cache;

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
    /// machine happens to have been doing.</summary>
    internal LocalAgentActivitySource(
        string claudeHome,
        string copilotHome,
        string environmentId,
        string environment,
        IAgentActivityCache? cache = null)
    {
        _claudeHome = claudeHome;
        _copilotHome = copilotHome;
        _environmentId = environmentId;
        _environment = environment;
        _cache = cache;
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
        var unreadable = new List<string>();

        foreach (var reader in Readers(since, cancellationToken))
        {
            try
            {
                sessions.AddRange(await reader.Read().ConfigureAwait(false));
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

        return new AgentActivityLog(sessions, unreadable, since, AgentActivityRuns.IdleAfter);
    }

    private (string Name, Func<Task<IReadOnlyList<AgentSessionActivity>>> Read)[] Readers(
        DateTimeOffset since,
        CancellationToken cancellationToken) =>
    [
        (ClaudeSessionReader.Name, () => ReadClaudeAsync(since, cancellationToken)),
        (CopilotSessionReader.Name, () => ReadCopilotAsync(since, cancellationToken))
    ];

    /// <summary>
    /// Claude's history, one transcript per session — the same files the session list
    /// walks, deduped by the same rule, through the same helper. Two copies of that rule
    /// is what <see cref="ClaudeTranscripts"/> exists to prevent.
    /// </summary>
    private async Task<IReadOnlyList<AgentSessionActivity>> ReadClaudeAsync(
        DateTimeOffset since,
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
                path => ClaudeTranscriptEvents.ReadAsync(path, cancellationToken),
                since).ConfigureAwait(false);

            if (activity is not null) sessions.Add(activity);
        }

        return sessions;
    }

    /// <summary>
    /// Copilot's history, one folder per session. No dedupe rule here and none needed:
    /// the folder <em>is</em> the session id, so a session cannot be filed twice the way
    /// a Claude transcript can.
    /// </summary>
    private async Task<IReadOnlyList<AgentSessionActivity>> ReadCopilotAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        var folder = new DirectoryInfo(Path.Combine(_copilotHome, "session-state"));

        if (!folder.Exists) return [];

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
                path => CopilotEventStream.ReadAsync(path, cancellationToken),
                since).ConfigureAwait(false);

            if (activity is not null) sessions.Add(activity);
        }

        return sessions;
    }

    /// <summary>
    /// One file's record, or null when it has nothing to say inside the horizon.
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
    /// </summary>
    private async Task<AgentSessionActivity?> ActivityOf(
        string id,
        AgentSessionKind kind,
        FileInfo file,
        Func<string, Task<IReadOnlyList<ActivityEvent>>> read,
        DateTimeOffset since)
    {
        var writtenAt = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);

        if (writtenAt < since) return null;

        var entry = _cache?.TryRead(file.FullName, writtenAt, AgentActivityRuns.IdleAfter);

        if (entry is null)
        {
            IReadOnlyList<ActivityEvent> events;

            try
            {
                events = await read(file.FullName).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            var folded = AgentActivityRuns.Fold(events, AgentActivityRuns.IdleAfter);

            // Stored whole-file, before any horizon is applied, because the horizon moves
            // and the file does not: the twelve-week read and the four-week one share
            // entries and only the clipping differs.
            entry = new AgentActivityEntry(folded.Runs, folded.Waits, AgentActivityRuns.IdleAfter);
            _cache?.Write(file.FullName, writtenAt, entry);
        }

        var runs = Clip(entry.Runs, since);
        var waits = Clip(entry.Waits, since);

        // Absent rather than present-and-empty. A session whose whole record fell outside
        // the horizon, or that left one lone event, has nothing to contribute to a
        // duration — and the session list is already the place that says it existed.
        return runs.Count == 0 && waits.Count == 0
            ? null
            : new AgentSessionActivity(id, kind, _environmentId, _environment, runs, waits);
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
}
