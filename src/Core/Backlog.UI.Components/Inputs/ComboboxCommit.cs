namespace Backlog.UI.Components.Inputs;

/// <summary>How a <see cref="Combobox"/> value was committed.</summary>
public enum ComboboxCommitReason
{
    /// <summary>An option was taken from the list, by Enter or a click.</summary>
    Picked,

    /// <summary>Enter was pressed with no option active, so what was typed stands.</summary>
    Entered,

    /// <summary>The field changed and was left, as a native <c>change</c> event reports
    /// it. Never raised for a value already committed by a pick or by Enter.</summary>
    Changed
}

/// <summary>
/// A value a <see cref="Combobox"/> committed, and why. A host that will not keep the
/// value calls <see cref="Refuse"/> from its handler, and the field goes back to the
/// last value that stood — the last one committed and kept, or the last figure the host
/// set — rather than leaving the refused text standing as if it had been taken. A host
/// bound two-way hears the revert through <c>ValueChanged</c>.
/// </summary>
public sealed class ComboboxCommit(string value, ComboboxCommitReason reason)
{
    /// <summary>The text committed: an option's value or what was typed.</summary>
    public string Value { get; } = value;

    public ComboboxCommitReason Reason { get; } = reason;

    /// <summary>Whether the host refused the value.</summary>
    public bool Refused { get; private set; }

    /// <summary>The host will not keep this value: put the field back to its own.</summary>
    public void Refuse() => Refused = true;
}
