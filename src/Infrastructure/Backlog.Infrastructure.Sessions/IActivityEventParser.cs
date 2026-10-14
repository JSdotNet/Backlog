namespace Backlog.Infrastructure.Sessions;

/// <summary>
/// One agent's event file, folded a line at a time into the <see cref="ActivityEvent"/>s
/// it records.
/// <para>
/// A fold rather than a pass for the reason <see cref="ClaudeTranscriptFactsParser"/> is
/// one: the activity reader resumes a file that is being written to from where its last
/// read stopped, so everything a later line's meaning depends on has to be state held
/// here, and <see cref="ILineFold{TSelf}.Clone"/> is what makes that safe. The
/// resuming itself is <see cref="ResumableFileMemory{TFold, TValue}"/>'s.
/// </para>
/// <para>
/// The events themselves are what is kept, not runs folded from them.
/// <see cref="AgentActivityRuns.Fold"/> sorts before it folds, and a line appended later
/// can carry an earlier timestamp than one already read, so the runs of a grown file are
/// folded again over the whole list rather than continued from where the last fold
/// stopped. That costs no I/O; continuing would cost a wrong answer.
/// </para>
/// </summary>
internal interface IActivityEventParser : ILineFold<IActivityEventParser>
{
    /// <summary>Every event the lines folded so far record, in the order the file holds
    /// them.</summary>
    IReadOnlyList<ActivityEvent> Events { get; }
}
