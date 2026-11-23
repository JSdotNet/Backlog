namespace Backlog.Modules.Calendar.Abstractions.Services;

/// <summary>
/// The window's repository scope and repository colours as the Calendar applies them to
/// its layers (local ADR 0056 §2): the scope chips narrow the tasks and the hours, and the
/// colours switch colours the chips, the hours blocks and the plan bars. Answered per window
/// by the shell, which keeps both choices; the Calendar keeps neither.
/// </summary>
public interface ICalendarWindowScope
{
    /// <summary>Raised after the scope chips or the colours switch moved. It may arrive on
    /// any thread.</summary>
    event Action? Changed;

    /// <summary>Whether a task filed under <paramref name="repositoryIds"/> is in the
    /// window's scope. An empty scope keeps everything; the Brain scope keeps only what
    /// resolves to no repository.</summary>
    bool InScope(IReadOnlyList<string> repositoryIds);

    /// <summary>The colour band for something filed under <paramref name="repositories"/> —
    /// ids or aliases, the first that has one deciding — or null while the colours are
    /// hidden or none has one.</summary>
    int? BandFor(IReadOnlyList<string> repositories);
}
