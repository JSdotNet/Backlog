using System.Text.Json;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// The Claude sessions a machine can account for, read from what Claude Code
/// leaves in the user profile.
/// <para>
/// Two folders, because Claude keeps liveness and history in different places and
/// only the pair of them answers "what has been going on here":
/// </para>
/// <list type="bullet">
/// <item><description><c>sessions/&lt;pid&gt;.json</c> — one file per running
/// session, holding the session id, the folder, when it started and what the
/// session is called. A file here is the only evidence that a session is live,
/// which is why this reader can tell Running from Stalled and the Copilot one
/// cannot.</description></item>
/// <item><description><c>projects/&lt;slug&gt;/&lt;id&gt;.jsonl</c> — the
/// transcript of a session that has been. There is one per session ever run, so
/// this is the history.</description></item>
/// </list>
/// <para>
/// The folder is read out of the transcript rather than decoded from the slug. The
/// slug is the path with every separator, colon and dot flattened to a hyphen, so
/// <c>D--Repos-Backlog--claude-worktrees-x</c> could be reassembled several ways
/// and only one of them is right — while the transcript states the <c>cwd</c>
/// outright a few lines in.
/// </para>
/// <para>
/// A third record is handed in rather than read here: the Claude desktop app's session
/// store (<see cref="ClaudeDesktopSessions"/>), which says which sessions the desktop app
/// still holds open. Every session this reader builds is stamped from it, live and past,
/// at both reading depths, because the depth must never change which sessions are
/// answered — and the open ones are exempt from the cap.
/// </para>
/// </summary>
internal sealed class ClaudeSessionReader
{
    /// <summary>What a transcript that was not opened is taken to state: nothing.</summary>
    private static readonly TranscriptFacts Unread = new(string.Empty, null, null);

    private readonly string _home;
    private readonly string _environmentId;
    private readonly string _environment;
    private readonly TimeProvider _clock;

    /// <summary>What every transcript this reader has read was found to say, by path,
    /// and — for the few most recently written — where the read stopped and the fold
    /// it left. In front of the disk cache, which stays the layer that survives a
    /// restart. The rules it remembers on are the activity reader's too, so they have
    /// one home: <see cref="ResumableFileMemory{TFold, TValue}"/>.</summary>
    private readonly ResumableFileMemory<ClaudeTranscriptFactsParser, TranscriptFacts> _remembered;

    /// <summary>The environment arrives as an id and a name, not as a name alone: a
    /// session found here ran here, and "here" is a device with an identity that
    /// outlives whatever the machine is currently called.
    /// <para>
    /// The cache is optional for the reason every cache in this product is: without
    /// one the reader opens every transcript on every read and is correct, only
    /// slower. See <see cref="ReadTranscriptAsync"/> for what it saves.
    /// </para>
    /// <para>
    /// The opener is a test's seam, so a test can count what a read opens and how much
    /// of it; a host leaves it to <see cref="TranscriptTail.OpenShared"/>.
    /// </para></summary>
    internal ClaudeSessionReader(
        string home,
        string environmentId,
        string environment,
        TimeProvider clock,
        ITranscriptFactsCache? facts = null,
        Func<string, Stream>? open = null)
    {
        _home = home;
        _environmentId = environmentId;
        _environment = environment;
        _clock = clock;
        _remembered = new ResumableFileMemory<ClaudeTranscriptFactsParser, TranscriptFacts>(
            open ?? TranscriptTail.OpenShared,
            parser => parser.Facts(),

            // What a cut-short read may still say is the parser's to answer.
            parser => (parser ?? new ClaudeTranscriptFactsParser()).Interrupted(),
            (path, length, writtenAt) => facts?.TryRead(path, length, writtenAt),
            (path, length, writtenAt, found) => facts?.Write(path, length, writtenAt, found));
    }

    /// <summary>What this reader is called when it cannot be read.</summary>
    internal static string Name => "Claude";

    internal async Task<SessionReading> ReadAsync(
        AgentSessionQuery query,
        ClaudeDesktopSessions desktop,
        CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var live = await ReadLiveAsync(now, desktop, cancellationToken).ConfigureAwait(false);
        var transcripts = Transcripts();

        // A timestamps reading opens no transcript, live or past: the walk's metadata
        // and the live files already answer every field it promises.
        var opens = query.Detail is AgentSessionDetail.Full;

        // Keyed by session id so a live session is not also listed as its own
        // finished transcript. The live file is the better record of the two: it
        // knows the session's name and the folder without being parsed for it — but
        // it holds nothing to count turns from and does not say which branch the
        // session is on, so the transcript it displaces is read for those two facts
        // before it is dropped from the list.
        IReadOnlyList<AgentSession> counted = opens
            ? await WithTranscriptFactsAsync(live, transcripts, now, cancellationToken).ConfigureAwait(false)
            : live;

        var seen = live.Select(session => session.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A horizon reading is the whole of the history inside the horizon, decided on
        // the file's own timestamp before anything is opened — the same instant the
        // row's LastActivityAt is read from, so nothing inside the window is filtered
        // out by a cheaper clock than the one that would have put it in. A live session
        // is inside every horizon: it is being written to now.
        var history = transcripts
            .Where(transcript => !seen.Contains(transcript.Key))
            .Select(transcript => transcript.Value)
            .Where(transcript => query.Horizon is not { } horizon || transcript.LastWriteTimeUtc >= horizon.UtcDateTime)
            .ToList();

        // A live session is never dropped by the cap. It is the row a reader opened
        // this surface for, and there are only ever as many as the machine is running.
        // A horizon reading has no cap: the count it feeds is only right if it is over
        // everything, and the facts cache is what makes that affordable on a refresh.
        //
        // An open session is never dropped by it either, for the same reason: it is in
        // the desktop app's sidebar, which is where a reader of the Open view is looking,
        // and the desktop app keeps sessions there for weeks after they stop being among
        // the newest hundred. They are read beside the cap rather than inside it, so the
        // room is computed over the rest and the newest hundred stay the newest hundred.
        var exempt = query.IsNewest
            ? history.Where(transcript => desktop.IsOpen(Path.GetFileNameWithoutExtension(transcript.Name))).ToList()
            : [];
        var capped = history.Except(exempt).OrderByDescending(file => file.LastWriteTimeUtc).ToList();
        var room = query.IsNewest ? Math.Max(0, AgentSessionLimits.PerAgent - live.Count) : capped.Count;

        // The open sessions older than the newest one the cap dropped: the ones listed
        // only because they are open. Any open session newer than that is among the
        // most recent anyway, since nothing newer than it was left out.
        var beyondCap = capped.Count > room
            ? exempt.Count(transcript => transcript.LastWriteTimeUtc < capped[room].LastWriteTimeUtc)
            : 0;

        var chosen = capped
            .Take(room)
            .Concat(exempt)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();
        var past = await ReadHistoryAsync(chosen, desktop, opens, now, cancellationToken).ConfigureAwait(false);

        _remembered.Prune(transcripts.Values.Select(transcript => transcript.FullName));

        // Sessions found, not files found. A session filed under two folders was never
        // two sessions, and counting it twice would have the subtitle overstate what
        // this machine has been doing.
        return new SessionReading([.. counted, .. past], live.Count + history.Count) { OpenBeyondCap = beyondCap };
    }

    /// <summary>
    /// The live sessions, each carrying the two facts only its transcript knows: the
    /// turn count, and the branch.
    /// <para>
    /// A live file states the session's id, folder, name and start, and nothing about
    /// how much has been said in it or which branch it is on. The transcript beside it
    /// states both, and the dedupe has already worked out which one that is — so they
    /// are taken from it here rather than the row being left with two nulls a
    /// transcript on this very disk could answer. A session too new to have written a
    /// transcript keeps them, which is the honest reading of a file that does not
    /// exist yet.
    /// </para>
    /// <para>
    /// <strong>The branch matters beyond the row it renders.</strong>
    /// <c>SessionRecordMapping.ToRecord</c> puts it on the wire, so a null here is a
    /// null in every other machine's session log — and not one that reliably repairs
    /// itself when the session ends, because the push selects on
    /// <c>LastActivityAt</c> and the finished reading carries the transcript's
    /// timestamp where the live readings that already moved the watermark carried the
    /// live file's.
    /// </para>
    /// <para>
    /// The folder is deliberately <em>not</em> taken, and that is the one element of
    /// the three left on the floor. The live file's <c>cwd</c> is where the session is
    /// now; the transcript's header is where it started, and a session resumed in
    /// another worktree keeps the old one there. Taking it would move a running row
    /// back to a folder it has left.
    /// </para>
    /// <para>
    /// The extra reads are bounded by how many sessions the machine is running, not by
    /// how many it has ever run — six on the profile this was measured against.
    /// </para>
    /// </summary>
    private async Task<List<AgentSession>> WithTranscriptFactsAsync(
        IReadOnlyList<AgentSession> live,
        IReadOnlyDictionary<string, FileInfo> transcripts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var read = new List<AgentSession>(live.Count);

        foreach (var session in live)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!transcripts.TryGetValue(session.Id, out var transcript))
            {
                read.Add(session);

                continue;
            }

            var facts = await ReadTranscriptAsync(transcript, now, cancellationToken).ConfigureAwait(false);

            read.Add(WithWork(session with { Branch = facts.Branch, TurnCount = facts.Turns }, facts));
        }

        return read;
    }

    /// <summary>
    /// One transcript per session, keyed by the session's id. Nothing is opened here:
    /// this is the directory walk and the dedupe, and which of them are worth reading
    /// is decided afterwards.
    /// <para>
    /// One session, one row — even when two project folders hold a transcript for it.
    /// A transcript is filed under the folder the session ran in, so a session whose
    /// cwd changed — resumed in a worktree it did not start in — is written under a
    /// second slug while keeping its id. That is one session filed twice rather than
    /// two sessions, so two rows would be wrong on its own terms; it was worse than
    /// wrong on screen, for the reason the live dedupe gives. Found on a real profile,
    /// where one of 377 transcripts was filed under two worktrees.
    /// </para>
    /// <para>
    /// The more recently written file wins, because it is the more current record of
    /// the same session: it names the folder that session ended up in, and it is the
    /// copy the agent went on appending to. Ties go to the path that sorts first, and
    /// that tie-break is not decoration: two copies written in the same instant would
    /// otherwise be separated by whichever the directory walk happened to reach first,
    /// which nothing guarantees, and the row's folder and branch would change between
    /// two refreshes of an unchanged profile.
    /// </para>
    /// </summary>
    /// <summary>
    /// Keyed by session id, because this reader needs to look a session's transcript up
    /// by the id its live file states rather than walk for it.
    /// <para>
    /// The dedupe rule itself is <see cref="ClaudeTranscripts"/>' rather than this
    /// method's. The activity reader needs the identical rule — which file speaks for a
    /// session that was filed under two project folders — and one copy of a rule found
    /// on a real profile is the whole reason it was extracted. All this adds is the
    /// shape: an id-keyed lookup instead of a list.
    /// </para>
    /// </summary>
    private Dictionary<string, FileInfo> Transcripts() =>
        ClaudeTranscripts.Newest(_home).ToDictionary(
            transcript => transcript.SessionId,
            transcript => transcript.File,
            StringComparer.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<AgentSession>> ReadLiveAsync(
        DateTimeOffset now,
        ClaudeDesktopSessions desktop,
        CancellationToken cancellationToken)
    {
        var folder = new DirectoryInfo(Path.Combine(_home, "sessions"));
        var sessions = new List<AgentSession>();

        if (!folder.Exists) return sessions;

        foreach (var file in folder.EnumerateFiles("*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The read is inside the guard, not just the parse. A live-session file
            // is being written by the process it describes, so opening one can lose
            // the race and throw a sharing violation — and letting that out of here
            // costs every Claude session rather than the one row, because the caller
            // reads an IOException as "this agent's folder cannot be read". A
            // half-written file and a momentarily locked one are the same ordinary
            // event and get the same proportionate answer.
            string json;

            try
            {
                json = await File.ReadAllTextAsync(file.FullName, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var session = FromLiveFile(json, file, now, desktop);

            if (session is not null) sessions.Add(session);
        }

        // One session, one row — even when two files claim it.
        //
        // The folder holds a file per process, not per session, and a session that
        // was resumed comes back under a new process id while the old file is still
        // sitting there. Two rows for one session is wrong on its own terms, and it
        // was worse than wrong on screen: a list keyed by session id had two
        // siblings with the same key, which corrupts Blazor's keyed diff and takes
        // the circuit down with it. Found on a real profile, where one of seventeen
        // files was a leftover.
        //
        // The most recently written file wins, because that is the process that is
        // actually running.
        return [.. sessions
            .GroupBy(session => session.Id, StringComparer.OrdinalIgnoreCase)
            .Select(duplicates => duplicates.MaxBy(session => session.LastActivityAt)!)];
    }

    /// <summary>
    /// One live-session file, or null when it is not one. Null rather than a throw:
    /// these files are written by another process while this one reads them, so a
    /// half-written or unrecognised file is an ordinary event and losing one row is
    /// the proportionate response to it.
    /// </summary>
    private AgentSession? FromLiveFile(string json, FileInfo file, DateTimeOffset now, ClaudeDesktopSessions desktop)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind is not JsonValueKind.Object) return null;

            var id = ClaudeJson.Text(root, "sessionId");

            if (string.IsNullOrWhiteSpace(id)) return null;

            var folder = ClaudeJson.Text(root, "cwd") ?? string.Empty;
            var name = ClaudeJson.Text(root, "name");

            // The file's own timestamp, not the session's start: a running session
            // rewrites this file as it goes, which is exactly what "last activity"
            // means here.
            var lastActivity = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);

            // The live file names its desktop record by the desktop app's own id, which
            // is the fallback where no record names this session's CLI id. The stamp
            // survives the transcript facts taken onto this session later: those are
            // record expressions over it.
            var record = desktop.For(id, ClaudeJson.NonBlankText(root, "hostSessionId"));

            return Stamped(new AgentSession(
                Id: id,
                Kind: AgentSessionKind.Claude,
                EnvironmentId: _environmentId,
                Environment: _environment,
                Title: string.IsNullOrWhiteSpace(name) ? TitleOf(folder, id) : name,
                WorkingFolder: folder,
                Repository: null,

                // A live file names no branch, and the folder sitting right here is
                // not one: a path leaf that looks like a worktree name is a guess, and
                // a guessed branch arrives on another machine indistinguishable from a
                // recorded one. The caller fills this in from the session's transcript
                // where there is one; see WithTranscriptFactsAsync.
                Branch: null,
                StartedAt: Started(root),
                LastActivityAt: lastActivity,
                State: AgentSessionStates.Of(lastActivity, now),

                // Nothing in a live file is countable. Filled in from the same
                // transcript, in the same place.
                TurnCount: null,

                // Read off this machine's own disk, which is the only kind of record
                // this reader can produce.
                Origin: AgentSessionOrigin.Local), record);
        }
    }

    /// <summary>
    /// The past sessions the caller chose: the most recent ones the cap leaves room for,
    /// and the open ones beside them. It is handed the deduped transcripts rather than
    /// finding them, because the caller needs the same set to answer a live session's
    /// turn count from — deduped before the cap and not after, so a session filed twice
    /// costs one place in the list rather than two. The choice is made before anything
    /// is opened: a transcript costs a file handle and a pass over the file, and a
    /// developer's profile holds hundreds.
    /// </summary>
    private async Task<List<AgentSession>> ReadHistoryAsync(
        IReadOnlyCollection<FileInfo> transcripts,
        ClaudeDesktopSessions desktop,
        bool opens,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var sessions = new List<AgentSession>();

        foreach (var transcript in transcripts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Unopened, a transcript states nothing: an empty folder, so the title falls
            // back to the short id, and nulls for the rest — absent, not guessed.
            var facts = opens
                ? await ReadTranscriptAsync(transcript, now, cancellationToken).ConfigureAwait(false)
                : Unread;
            var (folderPath, branch, turns) = (facts.Folder, facts.Branch, facts.Turns);

            var id = Path.GetFileNameWithoutExtension(transcript.Name);

            sessions.Add(Stamped(WithWork(new AgentSession(
                Id: id,
                Kind: AgentSessionKind.Claude,
                EnvironmentId: _environmentId,
                Environment: _environment,
                Title: TitleOf(folderPath, id),
                WorkingFolder: folderPath,
                Repository: null,
                Branch: branch,
                StartedAt: new DateTimeOffset(transcript.CreationTimeUtc, TimeSpan.Zero),
                LastActivityAt: new DateTimeOffset(transcript.LastWriteTimeUtc, TimeSpan.Zero),

                // A transcript with no live file beside it is over. Not Stalled:
                // stalled means still registered as running, and nothing here is.
                State: AgentSessionState.Finished,
                TurnCount: turns,
                Origin: AgentSessionOrigin.Local), facts), desktop.For(id)));
        }

        return sessions;
    }

    /// <summary>The session with what its desktop record says — whether the desktop app
    /// archived it, the scheduled task it is a run of, the name its sidebar shows — or as
    /// read where there is no record, all left null: unknown, never "not archived".</summary>
    private static AgentSession Stamped(AgentSession session, ClaudeDesktopSession? record) =>
        record is null
            ? session
            : session with { Archived = record.Archived, ScheduledTask = record.ScheduledTask, DesktopTitle = record.Title, DesktopSessionId = record.DesktopId };

    /// <summary>The session with what its transcript says about its work. A pass that
    /// did not finish leaves the lists null — not read — rather than empty.</summary>
    private static AgentSession WithWork(AgentSession session, TranscriptFacts facts) =>
        session with
        {
            Entrypoint = facts.Entrypoint,
            PullRequests = facts.Turns is null && facts.PullRequests.Count == 0 && facts.ModelUsage.Count == 0 ? null : facts.PullRequests,
            ModelUsage = facts.Turns is null && facts.PullRequests.Count == 0 && facts.ModelUsage.Count == 0 ? null : facts.ModelUsage
        };

    /// <summary>
    /// What a transcript says about itself: the folder and branch from the first lines
    /// that state them, and how many turns the person took in it. Empty strings and a
    /// null count where it states none — a transcript holding only queued prompts is a
    /// real thing and not an error.
    /// <para>
    /// One pass for all three, which is the reason they are one fold
    /// (<see cref="ClaudeTranscriptFactsParser"/>). The folder is in the first handful
    /// of lines and a count is in no bounded prefix of the file, so answering them
    /// separately would open every transcript twice to learn what one walk already has
    /// in hand.
    /// </para>
    /// <para>
    /// <b>Reading the whole file is a real cost change, and it was measured rather than
    /// assumed.</b> The header scrape stopped at forty lines; a count cannot. On the
    /// profile this was validated against — 137 Claude sessions and 705 Copilot ones,
    /// both readers capped at <see cref="AgentSessionLimits.PerAgent"/>, so 100
    /// transcripts and about 64 MB of them — a whole pane read went from 44-52 ms warm
    /// and 170 ms cold to 215-379 ms across a dozen runs. Roughly 200 ms added, on pane
    /// open and on every Refresh, for a column that could not otherwise exist. It stays
    /// that size because of the pre-filter in the turn test and because the file is
    /// streamed a line at a time; parsing every line, or reading 64 MB into strings to
    /// count part of it, is the version of this that is too slow to ship.
    /// </para>
    /// <para>
    /// That cost is paid once per transcript rather than once per read, three ways, in
    /// the order and on the rules <see cref="ResumableFileMemory{TFold, TValue}"/> keeps
    /// for both readers of these files: in memory, at the same length and write time;
    /// from where the last read stopped, for the transcripts written in the last day, so
    /// the one being written to right now — the file whose count must not be stale, and
    /// which misses every other way — costs what was appended; and on disk, when a host
    /// composes an <see cref="ITranscriptFactsCache"/>, which is what survives a restart.
    /// </para>
    /// <para>
    /// The facts are remembered on disk against the file's path, length and write time,
    /// and only for a pass over a settled file that read every line of it at that
    /// length. An interrupted pass returns its honest null and is tried again next time
    /// — letting the fault out of here would put Claude on the unreadable list over one
    /// file — and one that stopped in front of half a line would remember the file one
    /// line short. A settled file's last line with no newline is read as a line when it
    /// is whole JSON, so a finished transcript whose writer never terminated it is still
    /// remembered.
    /// </para>
    /// </summary>
    private Task<TranscriptFacts> ReadTranscriptAsync(
        FileInfo transcript,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        _remembered.ReadAsync(transcript, () => new ClaudeTranscriptFactsParser(), now, cancellationToken);

    /// <summary>Epoch milliseconds, which is how the live file dates itself.</summary>
    private static DateTimeOffset? Started(JsonElement root) =>
        root.TryGetProperty("startedAt", out var value) && value.TryGetInt64(out var milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : null;

    /// <summary>
    /// The folder's leaf, which for this product's sessions is the worktree name and
    /// therefore the most recognisable thing about a session. The short id when
    /// there is no folder — never the whole id, which is 36 characters of nothing a
    /// reader can tell apart at a glance.
    /// </summary>
    private static string TitleOf(string folder, string id)
    {
        var leaf = string.IsNullOrWhiteSpace(folder)
            ? null
            : Path.GetFileName(folder.TrimEnd('\\', '/'));

        return string.IsNullOrWhiteSpace(leaf)
            ? id.Length > 8 ? id[..8] : id
            : leaf;
    }
}
