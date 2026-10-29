namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// Which window a scope belongs to (local ADR 0043 §4, §5): the key the head named a
/// popped-out window by, or null for the main window. One per window, with the window's
/// view state; <see cref="Routes"/> writes it as the window's root renders.
/// <para>
/// For a head service that has to stand over the window a request came from rather
/// than over whichever window it finds first — the desktop head's folder picker is
/// parented to the window whose Settings asked for it.
/// </para>
/// </summary>
public sealed class CurrentShellWindow
{
    /// <summary>The popped-out window's key, or null in the main window.</summary>
    public string? Key { get; set; }
}
