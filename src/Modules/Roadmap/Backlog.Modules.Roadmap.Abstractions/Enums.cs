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
