namespace Backlog.Modules.Progress.Abstractions;

/// <summary>
/// The feature keys Progress owns
/// (<c>.devbook/domain/progress/context.md#goals-area</c>).
/// </summary>
public static class ProgressFeatures
{
    /// <summary>
    /// Goals, impediments and the stand-up
    /// (<c>.devbook/domain/progress/context.md#goals-area</c>, local ADR 0035 §10). One key
    /// switches the whole context. It ships off and marked <c>DEV</c>; the settings screen
    /// lists it from the shell's feature catalog. With it off the goal port answers
    /// <see cref="ProgressErrors.GoalsOff"/>; the goals already recorded are kept, and still
    /// travel between devices.
    /// </summary>
    public const string Goals = "goals";
}
