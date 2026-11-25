namespace Backlog.UI.Components.Layout;

/// <summary>
/// Which way a <c>SplitPane</c> lays its two panes out: side by side, or one above
/// the other.
/// <para>
/// Named for the layout, not for the separator. The separator of a side-by-side
/// split is a vertical line, which is why its <c>aria-orientation</c> reads
/// <c>vertical</c> while this reads <see cref="Horizontal"/>; the component turns the
/// one into the other so a host never has to.
/// </para>
/// </summary>
public enum SplitPaneOrientation
{
    /// <summary>The panes sit side by side and the separator resizes a width, with
    /// Left and Right. The shape every split had before this enum existed.</summary>
    Horizontal,

    /// <summary>The panes are stacked and the separator resizes a height, with Up
    /// and Down — a map above the list it pins.</summary>
    Vertical
}
