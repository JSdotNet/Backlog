using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sync;

/// <summary>Which way a document travelled: away from this machine to the
/// replica, or from the replica onto this machine.</summary>
public enum SyncDirection
{
    Sent,
    Received,
}

/// <summary>What kind of document it was. Several rather than one because they
/// travel by different rules and a person reading the log asks different
/// questions of each: a task is the backlog, a capture is the Inbox's, a
/// session is a record of what an agent did somewhere, a roadmap document is
/// the whole plan or the pace it is drawn at (local ADR 0018), and a GitHub
/// settings document is the repository registry or the account identities (local
/// ADR 0021). Appended, never reordered: nothing stores the ordinal, but a log line
/// read beside an older one should name the same kind.</summary>
public enum SyncItemKind
{
    Task,
    Capture,
    Session,
    Roadmap,
    GitHubSettings,

    /// <summary>A note: the one Inbox item that syncs both ways with the phone.</summary>
    Note,

    /// <summary>A sprint series: its cadence, its sprints and what each holds, as one
    /// whole document.</summary>
    SprintSeries,

    /// <summary>The My Day preferences: the Clear My Day overnight switch, as one whole
    /// document (local ADR 0032).</summary>
    MyDayPreferences,
}

/// <summary>
/// One document that moved, as a person would want to read it back.
/// <para>
/// <paramref name="Title"/> is whatever the document is called on the screen
/// it belongs to — a task's title, a capture's title, a session's title or the
/// machine it came from — and <paramref name="Note"/> is the one word that
/// qualifies it where the title alone would mislead: <c>deleted</c> for a
/// tombstone, <c>acknowledged</c> for an Inbox decision, <c>withdrawn</c> for a
/// capture the other end took back. Null when the plain reading is the right
/// one.
/// </para>
/// </summary>
public sealed record SyncActivityEntry(
    DateTimeOffset At,
    SyncDirection Direction,
    SyncItemKind Kind,
    string Id,
    string Title,
    string? Note = null);

/// <summary>
/// What replication has done in this window, one document at a time, for the
/// footer to show and the log to keep.
/// <para>
/// The summaries the workers already carry say how <em>many</em> moved; this
/// says <em>which</em>, and in which direction. It exists because the number
/// on its own is what a person cannot act on: "applied 3" leaves them opening
/// panes to find out what changed, and "sent 40" every five minutes on a
/// backlog nobody touched is the shape of a watermark bug that only the titles
/// would give away.
/// </para>
/// <para>
/// Two outlets, one call. Every entry is written through <see cref="ILogger"/>
/// as a structured line — direction, kind, id and title as named properties, so
/// the Aspire dashboard and any other sink can filter on them — and kept in a
/// bounded in-memory list for the window to read back. In memory and bounded
/// rather than a file of its own: the footer is the reader, the last few
/// hundred moves are what it has room to show, and a file that grew by every
/// task ever synced would be a second log beside the one the logger already
/// writes.
/// </para>
/// <para>
/// A singleton over a plain lock. The sessions that record into it are
/// transient and run on thread-pool threads; the footer reads it on the
/// renderer's. <see cref="Snapshot"/> hands out a copy so the reader never
/// walks a list a writer is moving.
/// </para>
/// </summary>
public sealed class SyncActivityLog
{
    /// <summary>How many entries the window keeps. Enough to read back the last
    /// several cycles on a busy day; small enough that a republish of a whole
    /// backlog does not turn the footer's dialog into the backlog.
    /// <para>
    /// Five hundred rather than a round two, because a first cycle is one big
    /// push: measured at 33 tasks and 200 session records on a development
    /// machine, and at two hundred the sessions had already pushed every task
    /// out before the window was opened. The first cycle is the one a person
    /// most wants to read back.
    /// </para></summary>
    public const int Capacity = 500;

    private readonly Lock _gate = new();
    private readonly LinkedList<SyncActivityEntry> _entries = new();
    private readonly ILogger _log;
    private readonly TimeProvider _time;

    public SyncActivityLog(ILogger<SyncActivityLog>? log = null, TimeProvider? time = null)
    {
        _log = log ?? NullLogger<SyncActivityLog>.Instance;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Raised after every <see cref="Record(SyncActivityEntry)"/>, and
    /// once after every <see cref="RecordRange"/> that held anything, on the
    /// recording thread. A renderer subscribing to it has to marshal, the way it
    /// does for the workers' <c>Changed</c>.</summary>
    public event Action? Changed;

    /// <summary>How many entries are held right now, capped at
    /// <see cref="Capacity"/>.</summary>
    public int Count
    {
        get
        {
            lock (_gate) return _entries.Count;
        }
    }

    /// <summary>Records one document's move, stamped now.</summary>
    public void Record(SyncDirection direction, SyncItemKind kind, string id, string title, string? note = null) =>
        Record(Entry(direction, kind, id, title, note));

    /// <summary>Records one document's move, stamped by the caller.</summary>
    public void Record(SyncActivityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_gate) Keep(entry);

        Write(entry);

        Changed?.Invoke();
    }

    /// <summary>
    /// Records a batch or a page of moves, in order, as if each had been passed
    /// to <see cref="Record(SyncActivityEntry)"/> in turn — one logger line each,
    /// the last read back first — but raises <see cref="Changed"/> once, and not
    /// at all for an empty batch.
    /// <para>
    /// Once because the footer is the subscriber, and every <c>Changed</c> has it
    /// copy the whole window and redraw: a first sync recording two hundred
    /// documents one at a time was two hundred redraws of a list nobody had time
    /// to read.
    /// </para>
    /// </summary>
    public void RecordRange(IEnumerable<SyncActivityEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        // Copied before the lock: the enumeration is the caller's and may be a
        // query, and a null found half way would leave half a batch kept.
        SyncActivityEntry[] batch = [.. entries];

        if (batch.Length == 0) return;
        foreach (var entry in batch) ArgumentNullException.ThrowIfNull(entry, nameof(entries));

        lock (_gate)
        {
            foreach (var entry in batch) Keep(entry);
        }

        foreach (var entry in batch) Write(entry);

        Changed?.Invoke();
    }

    /// <summary>An entry for one document's move, stamped now by this log's
    /// clock, for a caller collecting a batch to pass to
    /// <see cref="RecordRange"/>. Builds the entry; records nothing.</summary>
    public SyncActivityEntry Entry(SyncDirection direction, SyncItemKind kind, string id, string title, string? note = null) =>
        new(_time.GetUtcNow(), direction, kind, id, title, note);

    /// <summary>Puts one entry on top of the window and lets the oldest go.
    /// Called under the lock.</summary>
    private void Keep(SyncActivityEntry entry)
    {
        _entries.AddFirst(entry);
        if (_entries.Count > Capacity) _entries.RemoveLast();
    }

    private void Write(SyncActivityEntry entry) =>
        // Information rather than Debug: this is the line the feature exists to
        // write, and a level nobody sees by default would leave the dashboard
        // with the counts and nothing else. The title goes in quoted because a
        // task is allowed to be called "deleted".
        _log.LogInformation(
            "Sync {Direction} {Kind} {ItemId} \"{Title}\"{Note}",
            entry.Direction,
            entry.Kind,
            entry.Id,
            entry.Title,
            entry.Note is null ? string.Empty : $" ({entry.Note})");

    /// <summary>The entries held, newest first, as a copy the caller may keep
    /// while more arrive.</summary>
    public IReadOnlyList<SyncActivityEntry> Snapshot()
    {
        lock (_gate) return [.. _entries];
    }
}
