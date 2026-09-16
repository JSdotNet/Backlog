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

/// <summary>What kind of document it was. Three rather than one because the
/// three travel by different rules and a person reading the log asks different
/// questions of each: a task is the backlog, a capture is the Inbox's, and a
/// session is a record of what an agent did somewhere.</summary>
public enum SyncItemKind
{
    Task,
    Capture,
    Session,
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

    /// <summary>Raised after every entry, on the recording thread. A renderer
    /// subscribing to it has to marshal, the way it does for the workers'
    /// <c>Changed</c>.</summary>
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
        Record(new SyncActivityEntry(_time.GetUtcNow(), direction, kind, id, title, note));

    /// <summary>Records one document's move, stamped by the caller.</summary>
    public void Record(SyncActivityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_gate)
        {
            _entries.AddFirst(entry);
            if (_entries.Count > Capacity) _entries.RemoveLast();
        }

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

        Changed?.Invoke();
    }

    /// <summary>The entries held, newest first, as a copy the caller may keep
    /// while more arrive.</summary>
    public IReadOnlyList<SyncActivityEntry> Snapshot()
    {
        lock (_gate) return [.. _entries];
    }
}
