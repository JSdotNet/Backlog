namespace Backlog.UI.Components.Diagrams.C4;

/// <summary>
/// Where a <see cref="C4ZoomExplorer"/> is: the view open, and the component whose Code
/// level is open over it, if one is. Reported to a host that draws things about the
/// open view around the explorer — the architecture surface's left navigation and its
/// breadcrumb.
/// </summary>
/// <param name="ViewKey">The view open, by its key in the workspace.</param>
/// <param name="Title">What the view is called on screen.</param>
/// <param name="CodeComponentId">The component whose Code level is open, or null.</param>
/// <param name="CodeComponent">That component's name, or null.</param>
public sealed record C4ZoomLocation(string ViewKey, string Title, string? CodeComponentId = null, string? CodeComponent = null);
