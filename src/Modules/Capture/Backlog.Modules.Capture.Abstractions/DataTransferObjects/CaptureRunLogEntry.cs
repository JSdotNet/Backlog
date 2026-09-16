namespace Backlog.Modules.Capture.Abstractions.DataTransferObjects;

/// <summary>
/// One source's line from one past run, as the log keeps it: when the run
/// happened, how many captures the source produced, and what its line said.
/// <para>
/// The same three facts as <see cref="CaptureRunSourceResult"/> with the time
/// in place of the kind, because the log is read per source — the kind is the
/// question, and this is one answer to it. The message is kept whole, notes
/// and all, so a reader opening the log sees the same sentence the pane
/// showed on the day.
/// </para>
/// </summary>
/// <param name="RanAt">When the run happened.</param>
/// <param name="NewItems">How many captures the source produced in that run.</param>
/// <param name="Message">The source's line from that run, per-target remarks
/// included.</param>
public sealed record CaptureRunLogEntry(DateTimeOffset RanAt, int NewItems, string Message);
