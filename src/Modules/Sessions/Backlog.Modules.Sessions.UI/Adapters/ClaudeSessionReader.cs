using System.Text.Json;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UI.Adapters;

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
/// </summary>
internal sealed class ClaudeSessionReader
{
    /// <summary>How far into a transcript to look for the folder it ran in. The
    /// first few lines are queued prompts and hooks, which carry no <c>cwd</c>; the
    /// first real turn does, and that is within a handful of lines. A cap so a
    /// transcript that never states one costs a few reads rather than its whole
    /// length.</summary>
    private const int HeaderLines = 40;

    private readonly string _home;
    private readonly string _environmentId;
    private readonly string _environment;
    private readonly TimeProvider _clock;

    /// <summary>The environment arrives as an id and a name, not as a name alone: a
    /// session found here ran here, and "here" is a device with an identity that
    /// outlives whatever the machine is currently called.</summary>
    internal ClaudeSessionReader(string home, string environmentId, string environment, TimeProvider clock)
    {
        _home = home;
        _environmentId = environmentId;
        _environment = environment;
        _clock = clock;
    }

    /// <summary>What this reader is called when it cannot be read.</summary>
    internal static string Name => "Claude";

    internal async Task<SessionReading> ReadAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var live = await ReadLiveAsync(now, cancellationToken).ConfigureAwait(false);
        var transcripts = Transcripts();

        // Keyed by session id so a live session is not also listed as its own
        // finished transcript. The live file is the better record of the two: it
        // knows the session's name and the folder without being parsed for it — but
        // it holds nothing to count turns from, so the transcript it displaces is
        // read for that one fact before it is dropped from the list.
        var counted = await WithTurnCountsAsync(live, transcripts, cancellationToken).ConfigureAwait(false);

        var seen = live.Select(session => session.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var history = transcripts
            .Where(transcript => !seen.Contains(transcript.Key))
            .Select(transcript => transcript.Value)
            .ToList();

        // A live session is never dropped by the cap. It is the row a reader opened
        // this surface for, and there are only ever as many as the machine is running.
        var room = Math.Max(0, AgentSessionLimits.PerAgent - live.Count);
        var past = await ReadHistoryAsync(history, room, cancellationToken).ConfigureAwait(false);

        // Sessions found, not files found. A session filed under two folders was never
        // two sessions, and counting it twice would have the subtitle overstate what
        // this machine has been doing.
        return new SessionReading([.. counted, .. past], live.Count + history.Count);
    }

    /// <summary>
    /// The live sessions, each carrying the turn count only its transcript knows.
    /// <para>
    /// A live file states the session's id, folder, name and start, and nothing about
    /// how much has been said in it. The transcript beside it does, and the dedupe has
    /// already worked out which one that is — so the count is taken from it here
    /// rather than the row being left with a null a transcript on this very disk could
    /// answer. A session too new to have written a transcript keeps its null, which is
    /// the honest reading of a file that does not exist yet.
    /// </para>
    /// <para>
    /// The extra reads are bounded by how many sessions the machine is running, not by
    /// how many it has ever run — six on the profile this was measured against.
    /// </para>
    /// </summary>
    private static async Task<List<AgentSession>> WithTurnCountsAsync(
        IReadOnlyList<AgentSession> live,
        IReadOnlyDictionary<string, FileInfo> transcripts,
        CancellationToken cancellationToken)
    {
        var counted = new List<AgentSession>(live.Count);

        foreach (var session in live)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!transcripts.TryGetValue(session.Id, out var transcript))
            {
                counted.Add(session);

                continue;
            }

            var (_, _, turns) = await ReadTranscriptAsync(transcript, cancellationToken).ConfigureAwait(false);

            counted.Add(session with { TurnCount = turns });
        }

        return counted;
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

            var session = FromLiveFile(json, file, now);

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
    private AgentSession? FromLiveFile(string json, FileInfo file, DateTimeOffset now)
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

            var id = Text(root, "sessionId");

            if (string.IsNullOrWhiteSpace(id)) return null;

            var folder = Text(root, "cwd") ?? string.Empty;
            var name = Text(root, "name");

            // The file's own timestamp, not the session's start: a running session
            // rewrites this file as it goes, which is exactly what "last activity"
            // means here.
            var lastActivity = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);

            return new AgentSession(
                Id: id,
                Kind: AgentSessionKind.Claude,
                EnvironmentId: _environmentId,
                Environment: _environment,
                Title: string.IsNullOrWhiteSpace(name) ? TitleOf(folder, id) : name,
                WorkingFolder: folder,
                Repository: null,
                Branch: null,
                StartedAt: Started(root),
                LastActivityAt: lastActivity,
                State: AgentSessionStates.Of(lastActivity, now),

                // Nothing in a live file is countable. The caller fills this in from
                // the session's transcript where there is one; see WithTurnCountsAsync.
                TurnCount: null,

                // Read off this machine's own disk, which is the only kind of record
                // this reader can produce.
                Origin: AgentSessionOrigin.Local);
        }
    }

    /// <summary>
    /// The most recent <paramref name="room"/> sessions the history holds. It is handed
    /// the deduped transcripts rather than finding them, because the caller needs the
    /// same set to answer a live session's turn count from — deduped before the cap and
    /// not after, so a session filed twice costs one place in the list rather than two.
    /// </summary>
    private async Task<List<AgentSession>> ReadHistoryAsync(
        IReadOnlyCollection<FileInfo> transcripts,
        int room,
        CancellationToken cancellationToken)
    {
        var sessions = new List<AgentSession>();

        // Counted before anything is opened, and only the most recent are opened: a
        // transcript costs a file handle and a pass over the file, and a developer's
        // profile holds hundreds.
        var newest = transcripts
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(room);

        foreach (var transcript in newest)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (folderPath, branch, turns) =
                await ReadTranscriptAsync(transcript, cancellationToken).ConfigureAwait(false);

            var id = Path.GetFileNameWithoutExtension(transcript.Name);

            sessions.Add(new AgentSession(
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
                Origin: AgentSessionOrigin.Local));
        }

        return sessions;
    }

    /// <summary>
    /// What a transcript says about itself: the folder and branch from the first lines
    /// that state them, and how many turns the person took in it. Empty strings and a
    /// null count where it states none — a transcript holding only queued prompts is a
    /// real thing and not an error.
    /// <para>
    /// One pass for all three, which is the reason they are one method. The folder is
    /// in the first handful of lines and a count is in no bounded prefix of the file,
    /// so answering them separately would open every transcript twice to learn what
    /// one walk already has in hand.
    /// </para>
    /// <para>
    /// <b>Reading the whole file is a real cost change, and it was measured rather than
    /// assumed.</b> The header scrape stopped at <see cref="HeaderLines"/>; a count
    /// cannot. On the profile this was validated against — 137 Claude sessions and 705
    /// Copilot ones, both readers capped at
    /// <see cref="AgentSessionLimits.PerAgent"/>, so 100 transcripts and about 64 MB
    /// of them — a whole pane read went from 44-52 ms warm and 170 ms cold to 215-379
    /// ms across a dozen runs. Roughly 200 ms added, on pane open and on every
    /// Refresh, for a column that could not otherwise exist. It stays that size
    /// because of the pre-filter in <see cref="IsTurn"/> and because the file is
    /// streamed a line at a time; parsing every line, or reading 64 MB into strings to
    /// count part of it, is the version of this that is too slow to ship.
    /// </para>
    /// <para>
    /// If that ever stops being affordable, the cheap move is to remember a count
    /// against a transcript's path, length and write time — a session that has not
    /// been written to since the last read cannot have taken another turn — rather
    /// than to make the count less true.
    /// </para>
    /// </summary>
    private static async Task<(string Folder, string? Branch, int? Turns)> ReadTranscriptAsync(
        FileInfo transcript,
        CancellationToken cancellationToken)
    {
        var folder = string.Empty;
        string? branch = null;
        var turns = 0;
        var line = 0;

        try
        {
            using var reader = new StreamReader(transcript.FullName);

            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } text)
            {
                // The header is still only looked for in the first lines, and still
                // only until it is found. Widening the read to the whole file is about
                // the count; a cwd stated three thousand lines in would be a different
                // session's, from a resume this record has no way to attribute.
                if (folder.Length == 0 && line++ < HeaderLines)
                {
                    var (lineFolder, lineBranch) = Header(text);

                    folder = lineFolder ?? string.Empty;
                    branch ??= lineBranch;
                }

                if (IsTurn(text)) turns++;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The agent is appending to this file as it is read, or it is not ours to
            // open. Either way it costs this transcript's own facts and not the list —
            // letting it out of here would put Claude on the unreadable list over one
            // file.
            //
            // Null rather than the turns counted so far, and that is the point of
            // returning here rather than falling through. A count interrupted at line
            // nine of ninety is not a nearly-right number, it is a wrong one with
            // nothing on the row to say so, and a reader comparing two sessions would
            // be comparing one real count against one lost race. The folder and branch
            // are kept because they are complete or absent, never half-read.
            return (folder, branch, null);
        }

        // Zero turns is reported as absent, never as 0. A count of 0 claims a person
        // opened this session and never spoke in it; what a transcript with nothing
        // countable in it actually supports is that there is nothing here to count.
        // See AgentSession.TurnCount, and the Session Log's invariant behind it.
        return (folder, branch, turns == 0 ? null : turns);
    }

    /// <summary>
    /// Whether this line is a turn the person took — one prompt they sent, which is
    /// what <c>AgentSession.TurnCount</c> defines a turn to be.
    /// <para>
    /// Three things have to be true, and each of them rules out a cheaper answer that
    /// is wrong on this machine's real transcripts.
    /// </para>
    /// <list type="number">
    /// <item><description>The line's <em>root</em> <c>type</c> is <c>user</c>. The
    /// substring occurs nested as well — an assistant quoting a transcript back, an
    /// attachment carrying a user object — so counting occurrences over-reads, by
    /// about a hundred lines in the hundred most recent transcripts here.</description></item>
    /// <item><description>It was not injected. A skill body arrives as a user-role
    /// message marked <c>isMeta</c>, and a subagent's task prompt as one marked
    /// <c>isSidechain</c>; neither was typed by anybody, and the second belongs to a
    /// transcript this reader lists as its own session anyway.</description></item>
    /// <item><description>It carries something the person said rather than something a
    /// tool returned — see <see cref="Prompted"/>, which is where the real difference
    /// is. Counting every root-level user line instead would have reported about 6,100
    /// turns across the hundred transcripts where a person sent about 180.</description></item>
    /// </list>
    /// </summary>
    private static bool IsTurn(string line)
    {
        // A necessary condition that costs a substring scan, taken before the line is
        // parsed at all: on this machine's transcripts about three lines in four fail
        // it, and parsing every line of 64 MB to count part of it is the version of
        // this that is too slow to ship. Claude writes compact JSON, so this is the
        // exact byte sequence a user line carries. A future format that spaced its
        // separators would fail this filter on every line and the count would go
        // absent rather than wrong — which is the direction this product's counts are
        // meant to fail in, and the reason a substring is allowed to be load-bearing
        // here at all.
        if (!line.Contains("\"type\":\"user\"", StringComparison.Ordinal)) return false;

        if (line.Length == 0 || line[0] is not '{') return false;

        try
        {
            using var document = JsonDocument.Parse(line);

            var root = document.RootElement;

            if (root.ValueKind is not JsonValueKind.Object) return false;
            if (Text(root, "type") is not "user") return false;
            if (Flag(root, "isMeta") || Flag(root, "isSidechain")) return false;

            return Prompted(root);
        }
        catch (JsonException)
        {
            // A half-written last line, or a line this reader does not understand. It
            // is not counted, which loses at most one turn off a running session's
            // count and never invents one.
            return false;
        }
    }

    /// <summary>
    /// Whether a user-role line holds something the person sent.
    /// <para>
    /// This is the check that decides the number. Claude files a tool's output as a
    /// user-role message — the result is addressed to the model in the person's role —
    /// so on the hundred most recent transcripts on this machine about 6,100 lines are
    /// user-role and about 180 of them are prompts. A count that did not draw this line
    /// would be a tool-call count with a turn count's name on it, off by a factor of
    /// more than thirty.
    /// </para>
    /// <para>
    /// A string content is a typed prompt. An array is judged by what is in it: a text
    /// or image block is the person's, a <c>tool_result</c> block is not, and a line
    /// holding only tool results is not a turn. A user-role line with no message at all
    /// is not counted either — the reader cannot tell what it was, and the whole point
    /// of the null is that it does not have to pretend.
    /// </para>
    /// </summary>
    private static bool Prompted(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message)) return false;
        if (message.ValueKind is not JsonValueKind.Object) return false;
        if (!message.TryGetProperty("content", out var content)) return false;

        return content.ValueKind switch
        {
            JsonValueKind.String => !string.IsNullOrWhiteSpace(content.GetString()),
            JsonValueKind.Array => content.EnumerateArray().Any(block =>
                block.ValueKind is JsonValueKind.Object && Text(block, "type") is not "tool_result"),
            _ => false
        };
    }

    /// <summary>Present and true. A flag Claude did not write is not a flag set to
    /// false, but nothing here needs to tell those apart.</summary>
    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;

    private static (string? Folder, string? Branch) Header(string line)
    {
        if (line.Length == 0 || line[0] is not '{') return (null, null);

        try
        {
            using var document = JsonDocument.Parse(line);

            return document.RootElement.ValueKind is JsonValueKind.Object
                ? (Text(document.RootElement, "cwd"), Text(document.RootElement, "gitBranch"))
                : (null, null);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    /// <summary>Epoch milliseconds, which is how the live file dates itself.</summary>
    private static DateTimeOffset? Started(JsonElement root) =>
        root.TryGetProperty("startedAt", out var value) && value.TryGetInt64(out var milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : null;

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
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
