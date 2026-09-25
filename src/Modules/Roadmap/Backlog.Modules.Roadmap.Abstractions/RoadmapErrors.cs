using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Roadmap.Abstractions;

/// <summary>
/// Every way planning a roadmap can be refused, named once.
/// <para>
/// Published rather than internal because the codes are part of the contract: a
/// screen that wants to say "that would make the plan circular" in its own words,
/// or to keep a dialog open only for a validation failure, has to be able to
/// match on something more stable than a message string.
/// </para>
/// </summary>
public static class RoadmapErrors
{
    public static Error TitleRequired() =>
        Error.Validation("roadmap.title_required", "Give the planned work a title.");

    public static Error InvalidWindow(DateOnly start, DateOnly end) =>
        Error.Validation(
            "roadmap.invalid_window",
            $"Planned work cannot end before it starts — {end:d MMM yyyy} is before {start:d MMM yyyy}.");

    public static Error ItemNotFound(Guid itemId) =>
        Error.NotFound("roadmap.item_not_found", $"There is no planned item {itemId} in this plan.");

    public static Error NodeNotFound(Guid nodeId) =>
        Error.NotFound(
            "roadmap.node_not_found",
            $"There is nothing in this plan with id {nodeId}, so nothing can depend on it.");

    public static Error SelfDependency() =>
        Error.Validation("roadmap.self_dependency", "Something cannot wait for itself.");

    public static Error PlacedByHand(string title) =>
        Error.Conflict(
            "roadmap.placed_by_hand",
            $"'{title}' has been moved by hand since it was imported, so an import keeps its dates.");

    /// <summary>Re-lengthening from the tasks is the importer's effort rule, so it
    /// only applies while that rule still sizes the window. A null
    /// <paramref name="placement"/> means a person placed it.</summary>
    public static Error NotPlacedByEffort(string title, ImportPlacement? placement) =>
        Error.Conflict(
            "roadmap.not_placed_by_effort",
            placement is ImportPlacement.DueDate
                ? $"'{title}' ends on the due date its plan wrote, so its tasks do not decide how long it runs."
                : $"'{title}' has been moved by hand, so its tasks no longer decide how long it runs.");

    /// <summary>A cycle is a conflict with the plan's current state rather than
    /// bad input: the same edge would have been fine before the others were
    /// added.</summary>
    public static Error CyclicDependency(string waitingTitle, string dependsOnTitle) =>
        Error.Conflict(
            "roadmap.cyclic_dependency",
            $"'{waitingTitle}' already has to land before '{dependsOnTitle}', so this would make each wait for the other. "
            + "The plan has been left as it was.");
}
