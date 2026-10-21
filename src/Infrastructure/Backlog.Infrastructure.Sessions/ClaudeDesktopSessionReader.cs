using System.Collections.Concurrent;
using System.Text.Json;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// What the Claude desktop app's session store says about one of its sessions: the three
/// facts the session list reads from it, and what the record is keyed and ranked on.
/// </summary>
/// <param name="DesktopId">The desktop app's own id for the session —
/// <c>local_&lt;guid&gt;</c>, the record's file name — which a live marker names as its
/// <c>hostSessionId</c>.</param>
/// <param name="CliSessionId">The Claude Code session the record drives, which is the
/// id every other Claude reader knows the session by; null where the record names
/// none.</param>
/// <param name="Archived">The record's <c>isArchived</c>, or null where it states none —
/// an absent property in an undocumented format states nothing, and is not
/// <c>false</c>.</param>
/// <param name="ScheduledTask">The record's <c>scheduledTaskId</c>, or null.</param>
/// <param name="LastActivityAt">The record's <c>lastActivityAt</c>, or null. Only
/// ranks two records that name one CLI session.</param>
/// <param name="Title">The record's <c>title</c> — the name the sidebar shows, whatever
/// its <c>titleSource</c> — or null where it is absent or blank.</param>
internal sealed record ClaudeDesktopSession(
    string? DesktopId,
    string? CliSessionId,
    bool? Archived,
    string? ScheduledTask,
    DateTimeOffset? LastActivityAt,
    string? Title);

/// <summary>
/// One reading of the desktop app's session store, looked up by the CLI session id the
/// rest of the Claude readers key on.
/// </summary>
internal sealed class ClaudeDesktopSessions
{
    private readonly Dictionary<string, ClaudeDesktopSession> _byCli;
    private readonly Dictionary<string, ClaudeDesktopSession> _byDesktop;

    private ClaudeDesktopSessions(
        Dictionary<string, ClaudeDesktopSession> byCli,
        Dictionary<string, ClaudeDesktopSession> byDesktop)
    {
        _byCli = byCli;
        _byDesktop = byDesktop;
        OpenIds = _byCli
            .Where(entry => entry.Value.Archived is false)
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>No store, or no reader composed: every lookup answers null, so every
    /// session's <c>Archived</c> stays null.</summary>
    internal static ClaudeDesktopSessions None { get; } = Of([]);

    /// <summary>The CLI session ids whose record says they are not archived — the
    /// sessions the desktop app holds open.</summary>
    internal IReadOnlySet<string> OpenIds { get; }

    /// <summary>
    /// The record for a CLI session, or null where the store holds none.
    /// <para>
    /// The record's own <c>cliSessionId</c> first, because it is the record's statement
    /// of which session it drives. A live marker also names its desktop record, as
    /// <paramref name="hostSessionId"/>, and that is asked only where no record names
    /// the session — a session the desktop app resumed under a new CLI id still has its
    /// record's old one written down. Nothing is concluded from a null without both.
    /// </para>
    /// </summary>
    internal ClaudeDesktopSession? For(string cliSessionId, string? hostSessionId = null)
    {
        if (_byCli.TryGetValue(cliSessionId, out var record)) return record;

        return !string.IsNullOrWhiteSpace(hostSessionId) && _byDesktop.TryGetValue(hostSessionId, out var hosted)
            ? hosted
            : null;
    }

    /// <summary>Whether the desktop app holds this CLI session open. A session it has
    /// no record of is not.</summary>
    internal bool IsOpen(string cliSessionId) => OpenIds.Contains(cliSessionId);

    /// <summary>
    /// The lookups over every record read, given in path order. Two records that name
    /// one CLI session — a session forked or moved in the desktop app — are one
    /// session, and the more recently active record speaks for it; a record that dates
    /// no activity ranks below one that does, and a tie goes to the path that sorts
    /// first, so an unchanged store answers the same on every refresh.
    /// </summary>
    internal static ClaudeDesktopSessions Of(IReadOnlyList<ClaudeDesktopSession> records) =>
        new(
            records
                .Where(record => !string.IsNullOrWhiteSpace(record.CliSessionId))
                .GroupBy(record => record.CliSessionId!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.MaxBy(record => record.LastActivityAt ?? DateTimeOffset.MinValue)!,
                    StringComparer.OrdinalIgnoreCase),
            records
                .Where(record => !string.IsNullOrWhiteSpace(record.DesktopId))
                .DistinctBy(record => record.DesktopId!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(record => record.DesktopId!, StringComparer.OrdinalIgnoreCase));
}

/// <summary>
/// The Claude desktop app's session store:
/// <c>%APPDATA%\Claude\claude-code-sessions\&lt;account&gt;\&lt;org&gt;\local_&lt;guid&gt;.json</c>,
/// one record per session in the desktop app's sidebar, archived or not.
/// <para>
/// The one place this machine says which sessions the desktop app still holds open.
/// Nothing under <c>~/.claude</c> does: the desktop app ends an idle session's CLI
/// process, so a session open in the sidebar has no live marker and reads Finished, and
/// the transcript says nothing about the sidebar either. This store does, as
/// <c>isArchived</c>, along with the <c>scheduledTaskId</c> of a routine's run and the
/// <c>title</c> the sidebar shows.
/// </para>
/// <para>
/// <b>The format is the desktop app's private one</b>, read the way the
/// <c>~/.claude</c> readers read Claude Code's: only the properties used, an absent one
/// as null rather than a default, and every other value skipped unread. Skipped and not
/// bound, because a record carries blobs — tool surfaces, prompt snapshots — of up to a
/// hundred kilobytes: on the machine this was built against, 1,388 records came to 41 MB.
/// <c>scheduled-tasks.json</c> sits in the same folders and is never read; only
/// <c>local_*.json</c> is a session.
/// </para>
/// <para>
/// That cost is paid once per record rather than once per refresh: a record at the same
/// length and write time is answered from what the last read of it found, the way
/// <see cref="ResumableFileMemory{TFold, TValue}"/> answers a transcript, and only from
/// a read of the settled file. A record the walk no longer finds is forgotten.
/// </para>
/// <para>
/// A record that is half-written, held open by the desktop app or not JSON at all costs
/// that record and never the reading: the same per-file guard as the live-marker read in
/// <see cref="ClaudeSessionReader"/>. A locked record is not remembered, so the next read
/// has it; a malformed one is, at its length and write time, until the desktop app writes
/// it again. A store that is not there is a machine without the desktop app, which is
/// ordinary. Only the walk itself failing — the store unreadable — is let out, for the
/// source to name.
/// </para>
/// <para>
/// <b>A known limit.</b> A record whose CLI session has no transcript under
/// <c>~/.claude/projects</c> — Claude Code cleans old transcripts — gives the session
/// list nothing to build a row from, so an open session that old is not listed. None
/// was, on the machine this was built against.
/// </para>
/// </summary>
internal sealed class ClaudeDesktopSessionReader
{
    private readonly string _root;
    private readonly TimeProvider _clock;
    private readonly Func<string, Stream> _open;

    /// <summary>What every record read was found to say, by path, at the length and
    /// write time it was read at. Safe to share between concurrent reads: an entry is
    /// replaced whole, so a read that loses a race repeats work rather than spoiling
    /// it.</summary>
    private readonly ConcurrentDictionary<string, Remembered> _remembered = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="root">The store's folder, <c>claude-code-sessions</c>.</param>
    /// <param name="clock">What decides whether a record is settled enough to be
    /// remembered on its write time.</param>
    /// <param name="open">How a record is opened; a test's seam, so a test can count
    /// what a read opens. A host leaves it to <see cref="TranscriptTail.OpenShared"/>.</param>
    internal ClaudeDesktopSessionReader(string root, TimeProvider clock, Func<string, Stream>? open = null)
    {
        _root = root;
        _clock = clock;
        _open = open ?? TranscriptTail.OpenShared;
    }

    /// <summary>What this reader is called when its store cannot be read.</summary>
    internal static string Name => "Claude desktop app";

    /// <summary>Every record in the store, as lookups by CLI and desktop session id.</summary>
    /// <exception cref="IOException">The store's folders could not be walked.</exception>
    /// <exception cref="UnauthorizedAccessException">The store is not ours to walk.</exception>
    internal async Task<ClaudeDesktopSessions> ReadAsync(CancellationToken cancellationToken)
    {
        var root = new DirectoryInfo(_root);

        if (!root.Exists) return ClaudeDesktopSessions.None;

        var now = _clock.GetUtcNow().UtcDateTime;
        var records = new List<ClaudeDesktopSession>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Records(root).OrderBy(file => file.FullName, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            seen.Add(file.FullName);

            var record = await ReadRecordAsync(file, now, cancellationToken).ConfigureAwait(false);

            if (record is not null) records.Add(record);
        }

        foreach (var path in _remembered.Keys.Where(path => !seen.Contains(path)))
        {
            _remembered.TryRemove(path, out _);
        }

        return ClaudeDesktopSessions.Of(records);
    }

    /// <summary>The records, two folders down: an account, then an organisation. Only
    /// <c>local_*.json</c>, and the name is checked as well as globbed, so a pattern
    /// match the platform widens never lets <c>scheduled-tasks.json</c> in.</summary>
    private static IEnumerable<FileInfo> Records(DirectoryInfo root) =>
        root.EnumerateDirectories()
            .SelectMany(account => account.EnumerateDirectories())
            .SelectMany(org => org.EnumerateFiles("local_*.json"))
            .Where(file =>
                file.Name.StartsWith("local_", StringComparison.OrdinalIgnoreCase)
                && file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

    private async Task<ClaudeDesktopSession?> ReadRecordAsync(FileInfo file, DateTime now, CancellationToken cancellationToken)
    {
        var length = file.Length;
        var writtenAt = file.LastWriteTimeUtc;

        if (_remembered.TryGetValue(file.FullName, out var remembered)
            && remembered.Settled
            && remembered.Length == length
            && remembered.WrittenAt == writtenAt)
        {
            return remembered.Record;
        }

        byte[] bytes;

        // The read is inside the guard, not just the parse: the desktop app rewrites a
        // record while its session runs, so opening one can lose the race. Not
        // remembered, so the next read tries it again.
        try
        {
            var stream = _open(file.FullName);

            await using (stream.ConfigureAwait(false))
            {
                using var buffer = new MemoryStream(capacity: (int)Math.Min(length, int.MaxValue));

                await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                bytes = buffer.ToArray();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var record = Parse(bytes);

        // Remembered only from a read of a settled file: a write time is only as fine as
        // the file system keeps it, so a record written in the tick it was read can be
        // written again at the same length and match on both.
        _remembered[file.FullName] = new Remembered(
            length,
            writtenAt,
            now - writtenAt > ResumableFileMemory<ClaudeTranscriptFactsParser, TranscriptFacts>.Settling,
            record);

        return record;
    }

    /// <summary>
    /// The properties used, from the record's top level, every other value skipped
    /// unread — a nested object that happens to hold an <c>isArchived</c> of its own is
    /// skipped whole and never mistaken for the record's. Null for anything that is not
    /// one JSON object naming a session.
    /// </summary>
    private static ClaudeDesktopSession? Parse(ReadOnlySpan<byte> json)
    {
        if (json.StartsWith("﻿"u8)) json = json[3..];

        string? desktopId = null;
        string? cliSessionId = null;
        bool? archived = null;
        string? scheduledTask = null;
        DateTimeOffset? lastActivityAt = null;
        string? title = null;

        try
        {
            var reader = new Utf8JsonReader(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });

            if (!reader.Read() || reader.TokenType is not JsonTokenType.StartObject) return null;

            while (reader.Read() && reader.TokenType is JsonTokenType.PropertyName)
            {
                // Matched on the name's bytes before stepping onto its value, so no name
                // the record holds is ever turned into a string.
                var field = reader.ValueTextEquals("sessionId"u8) ? Field.DesktopId
                    : reader.ValueTextEquals("cliSessionId"u8) ? Field.CliSessionId
                    : reader.ValueTextEquals("isArchived"u8) ? Field.Archived
                    : reader.ValueTextEquals("scheduledTaskId"u8) ? Field.ScheduledTask
                    : reader.ValueTextEquals("lastActivityAt"u8) ? Field.LastActivityAt
                    : reader.ValueTextEquals("title"u8) ? Field.Title
                    : Field.None;

                if (!reader.Read()) return null;

                switch (field)
                {
                    case Field.DesktopId:
                        desktopId = Text(ref reader);
                        break;
                    case Field.CliSessionId:
                        cliSessionId = Text(ref reader);
                        break;
                    case Field.Archived:
                        archived = reader.TokenType switch
                        {
                            JsonTokenType.True => true,
                            JsonTokenType.False => false,
                            _ => null
                        };
                        break;
                    case Field.ScheduledTask:
                        scheduledTask = Text(ref reader);
                        break;
                    case Field.LastActivityAt:
                        lastActivityAt = reader.TokenType is JsonTokenType.Number && reader.TryGetInt64(out var milliseconds)
                            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
                            : null;
                        break;
                    case Field.Title:
                        title = Text(ref reader);
                        break;
                }

                // Leaves the reader on the value's last token, whatever it was: a no-op
                // for a scalar, the whole of an object or array for anything else.
                reader.Skip();
            }

            if (reader.TokenType is not JsonTokenType.EndObject) return null;
        }
        catch (JsonException)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(desktopId) && string.IsNullOrWhiteSpace(cliSessionId)
            ? null
            : new ClaudeDesktopSession(desktopId, cliSessionId, archived, scheduledTask, lastActivityAt, title);
    }

    /// <summary>A blank string states nothing, here as in <see cref="ClaudeJson.NonBlankText"/>.</summary>
    private static string? Text(ref Utf8JsonReader reader) =>
        reader.TokenType is JsonTokenType.String && reader.GetString() is { } text && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;

    private enum Field
    {
        None,
        DesktopId,
        CliSessionId,
        Archived,
        ScheduledTask,
        LastActivityAt,
        Title
    }

    private sealed record Remembered(long Length, DateTime WrittenAt, bool Settled, ClaudeDesktopSession? Record);
}
