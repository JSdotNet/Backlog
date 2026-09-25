namespace Backlog.Modules.Roadmap.Abstractions;

/// <summary>
/// How much the plan wants an item relative to the others.
/// <para>
/// The same four words as the Tasks module's <c>Priority</c>, chosen
/// deliberately rather than by accident: two vocabularies for the same idea would
/// make every conversation about priority start with "which kind". They stay
/// different types owned by different contexts — Backlog ranks a work item for
/// execution, this ranks intent across projects — and neither is converted into
/// the other. See <c>.domain/roadmap/naming.md#term-planning-priority</c>.
/// </para>
/// </summary>
public enum PlanningPriority
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// What kind of fixed point a milestone is. Business kinds, not shapes — how each
/// one is drawn is the view's decision.
/// </summary>
public enum MilestoneKind
{
    /// <summary>Something ships.</summary>
    Release,

    /// <summary>Change stops being accepted.</summary>
    Freeze,

    /// <summary>A date the plan is read out loud on.</summary>
    Review,

    /// <summary>A date promised to somebody else.</summary>
    Commitment
}

/// <summary>
/// Which rule an import used to place an item's window — the <c>placed_by_import</c>
/// provenance of ADR 0013, ruling 5.
/// <para>
/// Held as a nullable value rather than a flag. Absent means a person placed the
/// window, by hand at creation or by moving it since, and the importer never places
/// it again. Either member means the window is still the importer's, and a later
/// re-import has to know which rule placed it: a task-level re-import may re-length
/// an <see cref="Effort"/> window, but must keep the end of a <see cref="DueDate"/>
/// one, because that end is a date the person wrote.
/// </para>
/// </summary>
public enum ImportPlacement
{
    /// <summary>Start from the predecessors, length from gathered effort over the
    /// reader's velocity — or the default span when nothing estimated was
    /// gathered.</summary>
    Effort,

    /// <summary>Start from the predecessors, end from the entry's <c>due:</c>.</summary>
    DueDate
}

/// <summary>
/// Which of the reader's paces places an imported plan: the one they typed, or one
/// measured from the effort they finished over a recent stretch.
/// <para>
/// Persisted by name in the per-device pace file, so a member is never renamed — a
/// renamed one reads back as <see cref="Manual"/> and the reader's choice is lost
/// without a word.
/// </para>
/// </summary>
public enum PaceSource
{
    /// <summary>The figure the reader typed.</summary>
    Manual,

    /// <summary>Effort finished over the last two weeks, per day.</summary>
    LastTwoWeeks,

    /// <summary>Effort finished over the last four weeks, per day.</summary>
    LastFourWeeks,

    /// <summary>Effort finished over the last eight weeks, per day.</summary>
    LastEightWeeks
}
