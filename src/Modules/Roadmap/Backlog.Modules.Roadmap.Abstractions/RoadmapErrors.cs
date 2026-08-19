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

    public static Error BandNotNamed() =>
        Error.Validation("roadmap.band_not_named", "Say which repository's band the colour is for.");

    /// <summary>Refused rather than clamped: a plan is not the place to invent a
    /// colour, and giving somebody a hue they did not ask for would look like a choice
    /// they had made.</summary>
    public static Error UnknownBandColour(int colour) =>
        Error.Validation(
            "roadmap.unknown_band_colour",
            $"There is no band colour {colour}. Choose one of the five the design system defines.");

    /// <summary>A cycle is a conflict with the plan's current state rather than
    /// bad input: the same edge would have been fine before the others were
    /// added.</summary>
    public static Error CyclicDependency(string waitingTitle, string dependsOnTitle) =>
        Error.Conflict(
            "roadmap.cyclic_dependency",
            $"'{waitingTitle}' already has to land before '{dependsOnTitle}', so this would make each wait for the other. "
            + "The plan has been left as it was.");
}
