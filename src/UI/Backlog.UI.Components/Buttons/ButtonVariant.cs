namespace Backlog.UI.Components.Buttons;

public enum ButtonVariant
{
    Primary,
    Secondary,
    Ghost,
    Danger,

    /// <summary>No variant modifier at all. For hosts that already dress plain
    /// `.btn` themselves and would be restyled by an unasked-for modifier.</summary>
    None
}

/// <summary>
/// There are two button sizes, because the product uses two.
/// </summary>
/// <remarks>
/// This used to offer None, Small and Medium. `.btn` already carries the medium
/// metrics, so None and Medium rendered identically except that Medium also
/// emitted a `.btn--medium` class that no stylesheet has ever defined — three
/// names for two sizes, one of them a dead class. No application ever set the
/// parameter at all: every button in src/App is the default.
/// </remarks>
public enum ButtonSize
{
    /// <summary>The metrics `.btn` already carries. Emits no modifier, so a host
    /// that dresses plain `.btn` itself is not restyled by one it never asked
    /// for.</summary>
    Default,

    /// <summary>Tighter padding and smaller text, for dense rows and toolbars.</summary>
    Small
}
