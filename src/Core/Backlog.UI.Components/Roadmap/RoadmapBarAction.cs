namespace Backlog.UI.Components.Roadmap;

/// <summary>Which glyph a bar's action is drawn with. Named for the shape of the act, not
/// for what a host does with it: the host says that in the action's label.</summary>
public enum RoadmapBarActionKind
{
    /// <summary>Go and look at what carries the bar out, elsewhere. A list glyph.</summary>
    Open,

    /// <summary>Mark the bar's work as done. A tick.</summary>
    Complete
}

/// <summary>
/// The one thing a bar offers to be done to it in place, without opening it: drawn as a small
/// icon button at the bar's end, shown while the bar is hovered or holds the focus.
/// </summary>
/// <param name="Kind">The glyph it is drawn with.</param>
/// <param name="Label">Its accessible name and tooltip, in full — "Open Launch in Tasks",
/// "Complete Launch". The icon is the whole button, so this is the only thing a reader hears.</param>
public sealed record RoadmapBarAction(RoadmapBarActionKind Kind, string Label);
