namespace Backlog.UI.Components.Layout;

/// <summary>
/// Which pane of a <c>SplitPane</c> keeps the width it was given, and so which one
/// takes whatever is left of the row.
/// <para>
/// An enum rather than a bool called <c>AnchorEnd</c>, because both values are a
/// layout somebody chose: a fixed list beside a flexing detail and a flexing list
/// beside a fixed panel are two shapes, not a shape and its negation.
/// </para>
/// </summary>
public enum SplitPaneAnchor
{
    /// <summary>The leading pane is fixed; the trailing one flexes.</summary>
    Start,

    /// <summary>The trailing pane is fixed; the leading one flexes. The arrangement
    /// the shared JS resizer assumes by default, because the app's own right-hand
    /// knowledge panel has it.</summary>
    End
}
