namespace Backlog.Modules.Roadmap.Abstractions.Services;

/// <summary>
/// Says that the backlog work the roadmap draws may have changed underneath it: a
/// backlog entry an item gathers was written.
/// <para>
/// The band reads an item's steps, its progress and the shelf of unplanned tags from
/// Tasks on every load and keeps none of it (ADR 0013, ruling 6).
/// <see cref="IRoadmapPlanning.Changed"/> tells it when the plan was written; a task
/// write is not a plan write, so without this a step marked done while the band was
/// on screen stayed drawn the way it was until the page was reloaded.
/// </para>
/// <para>
/// A signal, like the task signal it is fed from: no payload, because the listener
/// reads what changed through its own ports as it always has.
/// </para>
/// </summary>
public interface IRoadmapWorkChanges
{
    /// <summary>Raised after a change, on whatever thread made it.</summary>
    event Action? Changed;
}
