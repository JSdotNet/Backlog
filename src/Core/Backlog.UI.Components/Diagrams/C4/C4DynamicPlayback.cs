namespace Backlog.UI.Components.Diagrams.C4;

/// <summary>
/// One step of a dynamic view as the zoom tab's steps panel lists it: who calls whom,
/// what the call is, and what it travels over.
/// </summary>
/// <param name="Order">The step's own number, as the DSL numbered it and as its arrow
/// carries it.</param>
/// <param name="From">The calling element's name, or its identifier when the model
/// declares no such element.</param>
/// <param name="To">The called element's name, likewise.</param>
/// <param name="Description">What the step says, from the dynamic view itself.</param>
/// <param name="Technology">What the call travels over. A dynamic step carries no
/// technology of its own, so this is read from the model relationship between the same
/// two elements.</param>
public sealed record C4PlaybackStep(
    int Order,
    string From,
    string To,
    string? Description,
    string? Technology);

/// <summary>
/// The steps of a dynamic view, in the order the zoom tab plays them.
/// <para>
/// Only the steps the picture draws are listed: a step whose endpoint the view cannot
/// draw has no arrow to highlight, so listing it would offer a current step nothing on
/// the picture answers to. That is the same selection
/// <see cref="C4MermaidWriter.VisibleRelationships"/> makes for the arrows.
/// </para>
/// </summary>
public static class C4DynamicPlayback
{
    /// <summary>The playable steps of <paramref name="view"/>, by their number; empty for
    /// any view that is not dynamic.</summary>
    public static IReadOnlyList<C4PlaybackStep> Steps(C4Workspace workspace, C4View view)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(view);

        if (view.Kind is not C4ViewKind.Dynamic || view.Steps.Count == 0) return [];

        var drawn = C4MermaidWriter.VisibleRelationships(workspace, view)
            .Where(edge => edge.Order is not null)
            .Select(edge => edge.Order!.Value)
            .ToHashSet();

        return [.. view.Steps
            .Where(step => drawn.Contains(step.Order))
            .OrderBy(step => step.Order)
            .Select(step => new C4PlaybackStep(
                step.Order,
                workspace.Element(step.SourceId)?.Name ?? step.SourceId,
                workspace.Element(step.DestinationId)?.Name ?? step.DestinationId,
                step.Description,
                Technology(workspace, step)))];
    }

    /// <summary>
    /// The technology of the model relationship this step plays.
    /// <para>
    /// The relationship between the same two elements whose description matches the
    /// step's is preferred, since two elements may talk over more than one channel; then
    /// any relationship between them that names a technology. None is null, and the
    /// panel then shows the description alone.
    /// </para>
    /// </summary>
    private static string? Technology(C4Workspace workspace, C4DynamicStep step)
    {
        var between = workspace.Relationships
            .Where(relationship =>
                string.Equals(relationship.SourceId, step.SourceId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(relationship.DestinationId, step.DestinationId, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(relationship.Technology))
            .ToList();

        return (between.FirstOrDefault(relationship =>
                    string.Equals(relationship.Description, step.Description, StringComparison.OrdinalIgnoreCase))
                ?? between.FirstOrDefault())?.Technology;
    }

    /// <summary>The step after <paramref name="current"/>, held at the last.</summary>
    public static int Next(int current, int count) => count == 0 ? 0 : Math.Clamp(current + 1, 0, count - 1);

    /// <summary>The step before <paramref name="current"/>, held at the first.</summary>
    public static int Previous(int current, int count) => count == 0 ? 0 : Math.Clamp(current - 1, 0, count - 1);
}
