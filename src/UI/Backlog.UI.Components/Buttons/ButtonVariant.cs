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

public enum ButtonSize
{
    /// <summary>The default: `.btn` already carries the medium metrics, so the
    /// modifier is only worth emitting when it actually changes something.</summary>
    None,
    Small,
    Medium
}
