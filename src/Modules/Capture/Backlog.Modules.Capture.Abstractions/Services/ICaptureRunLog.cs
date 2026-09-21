using Backlog.Modules.Capture.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Capture.Abstractions.Services;

/// <summary>
/// What past runs said, per source: when each source was last looked at, what
/// that run found, and the runs before it.
/// <para>
/// A port on Capture's own surface, the same shape as
/// <see cref="ICaptureSourceSettings"/>: the run writes it, the Inbox's
/// sources panel reads it, and where the answer is kept is an adapter's
/// business. The run does not read it back — what is new is decided by id at
/// delivery, never by remembering the last run — so the log is for the reader
/// and nothing the run does depends on it.
/// </para>
/// </summary>
public interface ICaptureRunLog
{
    /// <summary>Raised after a run is written down, so a panel showing the
    /// sources can redraw its last-run lines.</summary>
    event Action? Changed;

    /// <summary>The most recent run that looked at this source, or null when
    /// none has. A source switched off for a run is not in that run, so its
    /// last line stays the one from when it was on.</summary>
    CaptureRunLogEntry? LastRunFor(CaptureSourceKind kind);

    /// <summary>Every kept run that looked at this source, newest first. Empty
    /// when none has. How many are kept is the adapter's bound.</summary>
    IReadOnlyList<CaptureRunLogEntry> EntriesFor(CaptureSourceKind kind);

    /// <summary>Writes a run down, one entry per source it looked at. Never
    /// throws: a run that happened is a run that happened, and a log that
    /// could not be saved for next time is not a reason to report the run as
    /// failed.</summary>
    void Record(CaptureRunResultDto run);
}
