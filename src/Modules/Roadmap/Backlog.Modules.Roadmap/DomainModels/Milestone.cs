using Backlog.Modules.Roadmap.Abstractions;

namespace Backlog.Modules.Roadmap.DomainModels;

/// <summary>
/// A single day the plan is read against — a release, a freeze, a review, a
/// commitment made to somebody else.
/// <para>
/// Deliberately not a one-day <see cref="RoadmapItem"/>. A milestone is a
/// different thing, not a smaller one: it has no duration to lengthen or shorten,
/// rescheduling it moves one date rather than two, and "is this late" is answered
/// against a day rather than a span.
/// </para>
/// <para>
/// It is a first-class dependency endpoint — work waits on a release, and a
/// release waits on work — which is why it lives in the same aggregate as the
/// items. A dependency crossing between the two has to be validated on one side of
/// a boundary, not two.
/// </para>
/// </summary>
public sealed class Milestone
{
    public Milestone(
        Guid id,
        string title,
        DateOnly on,
        MilestoneKind kind,
        RepositoryScope scope,
        PlanningLane lane,
        Dependencies dependencies,
        bool isPlanWide = false)
    {
        Id = id;
        Title = title;
        On = on;
        Kind = kind;
        Scope = scope;
        Lane = lane;
        Dependencies = dependencies;
        IsPlanWide = isPlanWide;
    }

    /// <summary>Shares an id space with <see cref="RoadmapItem"/>, because a
    /// dependency does not care which of the two it points at.</summary>
    public Guid Id { get; }

    public string Title { get; private set; }

    /// <summary>The one day it falls on.</summary>
    public DateOnly On { get; private set; }

    public MilestoneKind Kind { get; private set; }

    public RepositoryScope Scope { get; private set; }

    public PlanningLane Lane { get; private set; }

    public Dependencies Dependencies { get; }

    /// <summary>
    /// Whether the whole plan is read against this date rather than one band.
    /// <para>
    /// A release or a freeze is not a fact about one repository: everything on the plan
    /// is either before it or after it. Saying so is what lets a view draw the date
    /// through every band instead of leaving a reader to hold a vertical position in
    /// their head. It is a property of the milestone rather than of the drawing,
    /// because which dates are that kind of date is a planning judgement.
    /// </para>
    /// </summary>
    public bool IsPlanWide { get; private set; }

    internal void Rename(string title) => Title = title;

    internal void MoveTo(DateOnly on) => On = on;

    internal void Reclassify(MilestoneKind kind) => Kind = kind;

    internal void FileUnder(RepositoryScope scope) => Scope = scope;

    internal void ReadAgainstThePlan(bool isPlanWide) => IsPlanWide = isPlanWide;

    internal void MoveToLane(PlanningLane lane) => Lane = lane;
}
