namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The toggles a popped-out window's title bar carries, just left of the system's
/// minimise, maximise and close (local ADR 0043 §4): Keep on top, then Remember this
/// window. A pure answer over <see cref="IShellWindows"/>, so the head's native title
/// bar only draws it: which toggles show, in what order, under which name and tooltip,
/// and pressed or not, is decided where a test can reach it.
/// </summary>
/// <param name="Toggles">The toggles to draw, in caption order.</param>
public sealed record SurfaceWindowCaption(IReadOnlyList<SurfaceWindowCaptionToggle> Toggles)
{
    /// <summary>The caption of the window <paramref name="windowKey"/> names.
    /// <list type="bullet">
    /// <item>Keep on top wherever the head can keep a window on top.</item>
    /// <item>Remember this window only over a surface the window shows — never over an
    /// id the catalog does not offer — in a host that remembers windows.</item>
    /// <item>Neither for a window under no key: a web harness tab or a loopback page has
    /// no title bar of the head's.</item>
    /// </list></summary>
    /// <param name="surfaceAvailable">Whether the window shows a surface the catalog
    /// offers, rather than "not available".</param>
    public static SurfaceWindowCaption For(IShellWindows windows, string? windowKey, bool surfaceAvailable)
    {
        ArgumentNullException.ThrowIfNull(windows);

        if (string.IsNullOrWhiteSpace(windowKey)) return new SurfaceWindowCaption([]);

        var toggles = new List<SurfaceWindowCaptionToggle>(2);
        if (windows.CanKeepOnTop)
        {
            // A window docked beside other work is the one that gets covered by it;
            // pinned, it stays in front of every window that is not pinned itself.
            toggles.Add(new SurfaceWindowCaptionToggle(
                SurfaceWindowCaptionToggleKind.KeepOnTop,
                "surface-window-on-top",
                "Keep on top",
                "Keep this window on top of other windows",
                windows.IsOnTop(windowKey)));
        }

        if (surfaceAvailable && windows.CanRemember)
        {
            // The name stays fixed and the pressed state carries the choice; the
            // tooltip says what the next press does.
            var remembered = windows.IsRemembered(windowKey);
            toggles.Add(new SurfaceWindowCaptionToggle(
                SurfaceWindowCaptionToggleKind.Remember,
                "surface-window-remember",
                "Remember this window",
                remembered ? "Forget this window" : "Remember this window",
                remembered));
        }

        return new SurfaceWindowCaption(toggles);
    }

    /// <summary>A press on <paramref name="kind"/> in the window
    /// <paramref name="windowKey"/> names: it flips what the toggle stands for, read
    /// back from <paramref name="windows"/> rather than from the control, so the next
    /// <see cref="For"/> shows what the head really holds.</summary>
    public static void Press(IShellWindows windows, string windowKey, SurfaceWindowCaptionToggleKind kind)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentException.ThrowIfNullOrWhiteSpace(windowKey);

        switch (kind)
        {
            case SurfaceWindowCaptionToggleKind.KeepOnTop:
                windows.SetOnTop(windowKey, !windows.IsOnTop(windowKey));
                break;
            case SurfaceWindowCaptionToggleKind.Remember:
                windows.SetRemembered(windowKey, !windows.IsRemembered(windowKey));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a caption toggle.");
        }
    }
}

/// <summary>Which caption toggle a <see cref="SurfaceWindowCaptionToggle"/> is: what a
/// press on it does, and which glyph the head draws for it.</summary>
public enum SurfaceWindowCaptionToggleKind
{
    /// <summary>Keep this window on top of other windows; a pin.</summary>
    KeepOnTop,

    /// <summary>Remember this window to reopen on the next start; a bookmark.</summary>
    Remember,
}

/// <summary>One toggle of a popped-out window's title bar.</summary>
/// <param name="Kind">What it stands for.</param>
/// <param name="AutomationId">Its UI Automation id, which a test of the running head
/// finds it by — the test id the header's toggle carried before it moved.</param>
/// <param name="Name">Its accessible name. It never changes with the state.</param>
/// <param name="ToolTip">What the pointer is told.</param>
/// <param name="Pressed">Whether it is pressed now.</param>
public sealed record SurfaceWindowCaptionToggle(
    SurfaceWindowCaptionToggleKind Kind,
    string AutomationId,
    string Name,
    string ToolTip,
    bool Pressed);
