namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>
/// The names of the owner's Brain areas, which the open task's Area picker suggests
/// before the areas already used on a task (<c>.devbook/domain/tasks/domain.md#area</c>,
/// <c>.devbook/domain/brain/domain.md#para-category</c>).
/// <para>
/// A port on Tasks' own surface rather than a reference to the Brain, the shape
/// <see cref="ICalendarPlans"/> takes: the shell answers it through the Brain's own
/// abstractions. A host that composes no Brain composes none of this either, and the
/// picker then suggests the areas in use alone.
/// </para>
/// </summary>
public interface IBrainAreaNames
{
    /// <summary>The Brain's area names, in its tab order; empty while no Brain is
    /// set or its folder is missing. Reads the folder's names, so a screen asks it off
    /// its render thread.</summary>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);
}
