namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A roving group of buttons — an element marked <c>data-roving-buttons</c>, whose
/// direct-child buttons are one tab stop — is roved in the browser: components.js
/// moves the stop and the focus on the arrows, Home and End, synchronously, so quick
/// presses are never computed from a stale stop, refuses those keys their scroll, and
/// gives a group left with no stop its first button back. bUnit runs no script, so
/// the script is read where it lives.
/// </summary>
public sealed class RovingButtonsScriptTests
{
    /// <summary>The keydown listener, to its own closing line.</summary>
    private static string Listener() => ComponentsScript.Region("if (!ROVING_BUTTONS_KEYS.has(event.key))", "\n    });");

    /// <summary>The group's buttons, the stop's repair, and what triggers it.</summary>
    private static string Restore() => ComponentsScript.Region("const rovingButtons", "if (!ROVING_BUTTONS_KEYS.has(event.key))");

    [Fact]
    public void A_roving_group_answers_the_arrows_home_and_end_unmodified_on_its_own_buttons()
    {
        var keys = ComponentsScript.Region("const ROVING_BUTTONS_KEYS", ";");
        Assert.Contains("'ArrowDown', 'ArrowUp', 'ArrowLeft', 'ArrowRight', 'Home', 'End'", keys, StringComparison.Ordinal);

        var listener = Listener();
        Assert.Contains("event.altKey || event.ctrlKey || event.metaKey || event.shiftKey", listener, StringComparison.Ordinal);
        Assert.Contains("${ROVING_BUTTONS_GROUP} > button", listener, StringComparison.Ordinal);
        Assert.Contains("const ROVING_BUTTONS_GROUP = '[data-roving-buttons]'", ComponentsScript.Source, StringComparison.Ordinal);
        Assert.Contains("event.preventDefault()", listener, StringComparison.Ordinal);
        Assert.DoesNotContain("stopPropagation", listener, StringComparison.Ordinal);
    }

    [Fact]
    public void A_roving_group_moves_its_one_stop_and_the_focus_wrapping_and_skipping_disabled()
    {
        var listener = Listener();

        // Its sibling buttons, the disabled left out.
        Assert.Contains("rovingButtons(target.parentElement)", listener, StringComparison.Ordinal);
        Assert.Contains("child.matches('button') && !child.disabled", Restore(), StringComparison.Ordinal);

        // Home and End to the ends; forward and back wrapping.
        Assert.Contains("event.key === 'Home' ? 0", listener, StringComparison.Ordinal);
        Assert.Contains("event.key === 'End' ? count - 1", listener, StringComparison.Ordinal);
        Assert.Contains("(from + 1) % count", listener, StringComparison.Ordinal);
        Assert.Contains("(from - 1 + count) % count", listener, StringComparison.Ordinal);

        // One tabindex="0", on the button reached, which takes the focus.
        Assert.Contains("button.tabIndex = index === to ? 0 : -1", listener, StringComparison.Ordinal);
        Assert.Contains("buttons[to].focus()", listener, StringComparison.Ordinal);
    }

    /// <summary>A re-render can remove or disable the button holding the stop; the
    /// group then has none and Tab would pass it by. The script gives it back.</summary>
    [Fact]
    public void A_roving_group_left_without_one_stop_gets_one_back()
    {
        var restore = Restore();

        // Nothing to do with exactly one stop; none or several is repaired.
        Assert.Contains("const stops = buttons.filter(button => button.tabIndex === 0)", restore, StringComparison.Ordinal);
        Assert.Contains("if (buttons.length === 0 || stops.length === 1) return;", restore, StringComparison.Ordinal);

        // The focused stop is kept, else the first stop, else the first enabled button.
        Assert.Contains("stops.find(button => button === document.activeElement) ?? stops[0] ?? buttons[0]", restore, StringComparison.Ordinal);
        Assert.Contains("button.tabIndex = button === keep ? 0 : -1", restore, StringComparison.Ordinal);

        // Run on every group, at most once a frame, after any change to the page —
        // an element removed or added, or a button disabled.
        Assert.Contains("document.querySelectorAll(ROVING_BUTTONS_GROUP).forEach(restoreRovingStop)", restore, StringComparison.Ordinal);
        Assert.Contains("new MutationObserver(", restore, StringComparison.Ordinal);
        Assert.Contains("requestAnimationFrame(restoreRovingStops)", restore, StringComparison.Ordinal);
        Assert.Contains("childList: true", restore, StringComparison.Ordinal);
        Assert.Contains("subtree: true", restore, StringComparison.Ordinal);
        Assert.Contains("attributeFilter: ['disabled']", restore, StringComparison.Ordinal);
    }
}
