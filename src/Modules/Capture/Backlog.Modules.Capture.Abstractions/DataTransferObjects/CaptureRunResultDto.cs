namespace Backlog.Modules.Capture.Abstractions.DataTransferObjects;

/// <summary>What one source had to say for itself after a run.</summary>
/// <param name="Kind">Which source.</param>
/// <param name="NewItems">How many captures it produced. Zero is an ordinary
/// answer, not a failure.</param>
/// <param name="Message">One line for the reader — what happened, or why
/// nothing did. Always written, because a source that says nothing is one the
/// reader cannot tell from one that was skipped.</param>
public sealed record CaptureRunSourceResult(CaptureSourceKind Kind, int NewItems, string Message);

/// <summary>
/// What a run produced, per enabled source and in total.
/// </summary>
/// <param name="Sources">One result per source the run looked at, in the order
/// it looked. Empty when nothing was enabled.</param>
/// <param name="RanAt">When the run happened.</param>
public sealed record CaptureRunResultDto(IReadOnlyList<CaptureRunSourceResult> Sources, DateTimeOffset RanAt)
{
    /// <summary>No source was switched on, so there was nothing to run. The
    /// shell reads this to point the reader at the settings rather than report
    /// an empty result as if it were one.</summary>
    public bool NothingEnabled => Sources.Count == 0;

    public int TotalNewItems => Sources.Sum(source => source.NewItems);
}
