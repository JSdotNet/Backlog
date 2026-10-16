namespace Backlog.Modules.Progress.Abstractions;

/// <summary>
/// The feature keys Progress owns
/// (<c>.devbook/domain/progress/context.md#progress-area</c>).
/// <para>
/// One key switches the whole context. It ships off and marked <c>DEV</c>; the
/// settings screen lists it from the shell's feature catalog. With it off the
/// module's ports answer <see cref="ProgressErrors.Off"/> and the screens draw
/// nothing, while the series and sprints already recorded are kept for when it is
/// turned on again.
/// </para>
/// </summary>
public static class ProgressFeatures
{
    /// <summary>Sprint series, the sprint view and the sprint lines in the briefs.</summary>
    public const string Progress = "progress";
}
