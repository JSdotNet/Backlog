namespace Backlog.Modules.Roadmap.Abstractions.Services;

/// <summary>
/// How many story points the reader gets through in a day.
/// <para>
/// A port on Roadmap Planning's own surface, answered by an infrastructure adapter
/// over the per-device settings file. The module asks this instead of reading the
/// settings store, because a module may not reference infrastructure
/// (<c>ModuleBoundaryTests.A_module_never_references_infrastructure</c>) — and
/// because where the figure is kept is not something the roadmap should have an
/// opinion about.
/// </para>
/// <para>
/// It is a <em>reading preference the person owns, not an estimate the plan
/// registers</em> (ADR 0013, ruling 4). Placing an imported plan that states no due
/// date divides the effort its tasks registered by this and rounds up, so the only
/// thing added to the gathered total is the reader's own statement of their pace.
/// The plan still invents no estimate for work that carries none.
/// </para>
/// </summary>
public interface IPlanningVelocity
{
    /// <summary>Always positive, so a caller may divide by it without guarding.
    /// The reader having chosen nothing reads as 1.</summary>
    decimal StoryPointsPerDay { get; }
}
