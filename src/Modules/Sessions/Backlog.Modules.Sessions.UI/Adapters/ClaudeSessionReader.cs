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
    private readonly string _environment;
    private readonly TimeProvider _clock;

    internal ClaudeSessionReader(string home, string environment, TimeProvider clock)
    {
        _home = home;
        _environment = environment;
        _clock = clock;
    }

    /// <summary>What this reader is called when it cannot be read.</summary>
    internal static string Name => "Claude";

    internal async Task<SessionReading> ReadAsync(CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var live = await ReadLiveAsync(now, cancellationToken).ConfigureAwait(false);

        // Keyed by session id so a live session is not also listed as its own
        // finished transcript. The live file is the better record of the two: it
        // knows the session's name and the folder without being parsed for it.
        var seen = live.Select(session => session.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A live session is never dropped by the cap. It is the row a reader opened
        // this surface for, and there are only ever as many as the machine is running.
        var room = Math.Max(0, AgentSessionLimits.PerAgent - live.Count);
        var (past, transcripts) = await ReadHistoryAsync(seen, room, cancellationToken).ConfigureAwait(false);

        return new SessionReading([.. live, .. past], live.Count + transcripts);
    }

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
                Environment: _environment,
                Title: string.IsNullOrWhiteSpace(name) ? TitleOf(folder, id) : name,
                WorkingFolder: folder,
                Repository: null,
                Branch: null,
                StartedAt: Started(root),
                LastActivityAt: lastActivity,
                State: AgentSessionStates.Of(lastActivity, now));
        }
    }

    /// <summary>
    /// The most recent <paramref name="room"/> transcripts as sessions, and how many
    /// there were altogether. Both numbers, because they differ and the surface has
    /// to be able to say so.
    /// </summary>
    private async Task<(List<AgentSession> Sessions, int Discovered)> ReadHistoryAsync(
        HashSet<string> alreadySeen,
        int room,
        CancellationToken cancellationToken)
    {
        var folder = new DirectoryInfo(Path.Combine(_home, "projects"));
        var sessions = new List<AgentSession>();

        if (!folder.Exists) return (sessions, 0);

        // Enumerated and counted before anything is opened, and only the most recent
        // are opened: a transcript costs a file handle and a few reads to find its
        // cwd, and a developer's profile holds hundreds.
        var all = folder
            .EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
            .Where(file => !alreadySeen.Contains(Path.GetFileNameWithoutExtension(file.Name)))
            .ToList();

        var transcripts = all
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(room);

        foreach (var transcript in transcripts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (folderPath, branch) = await ReadHeaderAsync(transcript, cancellationToken).ConfigureAwait(false);
            var id = Path.GetFileNameWithoutExtension(transcript.Name);

            sessions.Add(new AgentSession(
                Id: id,
                Kind: AgentSessionKind.Claude,
                Environment: _environment,
                Title: TitleOf(folderPath, id),
                WorkingFolder: folderPath,
                Repository: null,
                Branch: branch,
                StartedAt: new DateTimeOffset(transcript.CreationTimeUtc, TimeSpan.Zero),
                LastActivityAt: new DateTimeOffset(transcript.LastWriteTimeUtc, TimeSpan.Zero),

                // A transcript with no live file beside it is over. Not Stalled:
                // stalled means still registered as running, and nothing here is.
                State: AgentSessionState.Finished));
        }

        return (sessions, all.Count);
    }

    /// <summary>
    /// The folder and branch a transcript states, from the first lines that state
    /// them. Empty strings when it never does — a transcript that only holds queued
    /// prompts is a real thing and not an error.
    /// </summary>
    private static async Task<(string Folder, string? Branch)> ReadHeaderAsync(
        FileInfo transcript,
        CancellationToken cancellationToken)
    {
        var folder = string.Empty;
        string? branch = null;

        try
        {
            using var reader = new StreamReader(transcript.FullName);

            for (var line = 0; line < HeaderLines; line++)
            {
                var text = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

                if (text is null) break;

                var (lineFolder, lineBranch) = Header(text);

                folder = string.IsNullOrEmpty(folder) ? lineFolder ?? string.Empty : folder;
                branch ??= lineBranch;

                if (!string.IsNullOrEmpty(folder)) break;
            }
        }
        catch (IOException)
        {
            // The agent is appending to this file as it is read. A locked
            // transcript costs its folder, not the whole list.
        }

        return (folder, branch);
    }

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
