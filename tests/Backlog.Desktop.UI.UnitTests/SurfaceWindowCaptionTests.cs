using Backlog.Desktop.UI.Shell;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The toggles a popped-out window's title bar carries beside the system caption
/// buttons (local ADR 0043 §4): Keep on top, then Remember this window. The head's
/// native title bar draws whatever <see cref="SurfaceWindowCaption"/> answers, so
/// which toggles show, their order, names, tooltips and pressed state are proved here.
/// </summary>
public sealed class SurfaceWindowCaptionTests
{
    private const string WindowKey = "window-1";

    [Fact]
    public void A_head_that_can_do_both_puts_keep_on_top_then_remember_this_window_in_the_caption()
    {
        var windows = new FakeShellWindows { CanKeepOnTop = true, CanRemember = true };

        var caption = SurfaceWindowCaption.For(windows, WindowKey, surfaceAvailable: true);

        Assert.Equal(
            [SurfaceWindowCaptionToggleKind.KeepOnTop, SurfaceWindowCaptionToggleKind.Remember],
            caption.Toggles.Select(toggle => toggle.Kind));
    }

    [Fact]
    public void Keep_on_top_is_named_for_what_it_does_and_unpressed_until_pressed()
    {
        var windows = new FakeShellWindows { CanKeepOnTop = true };

        var pin = Assert.Single(SurfaceWindowCaption.For(windows, WindowKey, surfaceAvailable: true).Toggles);

        Assert.Equal("Keep on top", pin.Name);
        Assert.Equal("Keep this window on top of other windows", pin.ToolTip);
        Assert.Equal("surface-window-on-top", pin.AutomationId);
        Assert.False(pin.Pressed);
    }

    [Fact]
    public void Pressing_keep_on_top_keeps_the_window_on_top_and_pressing_it_again_lets_it_go()
    {
        var windows = new FakeShellWindows { CanKeepOnTop = true };

        SurfaceWindowCaption.Press(windows, WindowKey, SurfaceWindowCaptionToggleKind.KeepOnTop);

        Assert.Contains(WindowKey, windows.OnTop);
        var pin = Assert.Single(SurfaceWindowCaption.For(windows, WindowKey, surfaceAvailable: true).Toggles);
        Assert.True(pin.Pressed);
        Assert.Equal("Keep this window on top of other windows", pin.ToolTip);

        SurfaceWindowCaption.Press(windows, WindowKey, SurfaceWindowCaptionToggleKind.KeepOnTop);

        Assert.DoesNotContain(WindowKey, windows.OnTop);
        Assert.False(Assert.Single(SurfaceWindowCaption.For(windows, WindowKey, surfaceAvailable: true).Toggles).Pressed);
    }

    /// <summary>A window that opens kept on top — its surface's last window was —
    /// shows the pin pressed.</summary>
    [Fact]
    public void A_window_opened_on_top_shows_keep_on_top_pressed()
    {
        var windows = new FakeShellWindows { CanKeepOnTop = true };
        windows.OnTop.Add(WindowKey);

        Assert.True(Assert.Single(SurfaceWindowCaption.For(windows, WindowKey, surfaceAvailable: true).Toggles).Pressed);
    }

    /// <summary>The pin shows whenever the head can keep a window on top, even over a
    /// surface the window cannot show.</summary>
    [Fact]
    public void Keep_on_top_shows_over_a_surface_the_window_cannot_show()
    {
        var windows = new FakeShellWindows { CanKeepOnTop = true, CanRemember = true };

        var caption = SurfaceWindowCaption.For(windows, WindowKey, surfaceAvailable: false);

        Assert.Equal([SurfaceWindowCaptionToggleKind.KeepOnTop], caption.Toggles.Select(toggle => toggle.Kind));
    }

    [Fact]
    public void A_host_that_cannot_keep_a_window_on_top_puts_no_pin_in_the_caption()
    {
        var windows = new FakeShellWindows { CanRemember = true };

        var caption = SurfaceWindowCaption.For(windows, WindowKey, surfaceAvailable: true);

        Assert.DoesNotContain(caption.Toggles, toggle => toggle.Kind == SurfaceWindowCaptionToggleKind.KeepOnTop);
    }

    /// <summary>Remember this window keeps one name; its tooltip says what the next
    /// press does.</summary>
    [Fact]
    public void Remember_this_window_remembers_and_forgets_this_window_and_its_tooltip_says_what_the_next_press_does()
    {
        var windows = new FakeShellWindows { CanRemember = true };

        var remember = Assert.Single(SurfaceWindowCaption.For(windows, WindowKey, surfaceAvailable: true).Toggles);
        Assert.Equal("Remember this window", remember.Name);
        Assert.Equal("Remember this window", remember.ToolTip);
        Assert.Equal("surface-window-remember", remember.AutomationId);
        Assert.False(remember.Pressed);

        SurfaceWindowCaption.Press(windows, WindowKey, SurfaceWindowCaptionToggleKind.Remember);

        Assert.Equal([WindowKey], windows.Remembered);
        remember = Assert.Single(SurfaceWindowCaption.For(windows, WindowKey, surfaceAvailable: true).Toggles);
        Assert.True(remember.Pressed);
        Assert.Equal("Remember this window", remember.Name);
        Assert.Equal("Forget this window", remember.ToolTip);

        SurfaceWindowCaption.Press(windows, WindowKey, SurfaceWindowCaptionToggleKind.Remember);

        Assert.Empty(windows.Remembered);
        Assert.False(Assert.Single(SurfaceWindowCaption.For(windows, WindowKey, surfaceAvailable: true).Toggles).Pressed);
    }

    [Fact]
    public void A_window_already_remembered_shows_remember_this_window_pressed()
    {
        var windows = new FakeShellWindows { CanRemember = true };
        windows.Remembered.Add(WindowKey);

        var remember = Assert.Single(SurfaceWindowCaption.For(windows, WindowKey, surfaceAvailable: true).Toggles);

        Assert.True(remember.Pressed);
        Assert.Equal("Forget this window", remember.ToolTip);
    }

    /// <summary>No bookmark where the window cannot be remembered: over a surface it
    /// cannot show — an unknown id, the reserved <c>proposals</c> — and in a host that
    /// remembers no windows.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void No_remember_toggle_where_the_window_cannot_be_remembered(bool canRemember, bool surfaceAvailable)
    {
        var windows = new FakeShellWindows { CanRemember = canRemember, CanKeepOnTop = true };

        var caption = SurfaceWindowCaption.For(windows, WindowKey, surfaceAvailable);

        Assert.DoesNotContain(caption.Toggles, toggle => toggle.Kind == SurfaceWindowCaptionToggleKind.Remember);
    }

    /// <summary>A window under no key — a web harness tab, a loopback page — has no
    /// title bar of the head's to put a toggle in.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void A_window_under_no_key_has_no_caption_toggles(string? windowKey)
    {
        var windows = new FakeShellWindows { CanKeepOnTop = true, CanRemember = true };

        Assert.Empty(SurfaceWindowCaption.For(windows, windowKey, surfaceAvailable: true).Toggles);
    }

    /// <summary>The head's windows, as far as the caption reads and drives them.</summary>
    private sealed class FakeShellWindows : IShellWindows
    {
        public bool CanKeepOnTop { get; init; }

        public bool CanRemember { get; init; }

        public HashSet<string> OnTop { get; } = new(StringComparer.Ordinal);

        public List<string> Remembered { get; } = [];

        public bool IsOnTop(string windowKey) => OnTop.Contains(windowKey);

        public void SetOnTop(string windowKey, bool onTop)
        {
            if (!CanKeepOnTop) return;
            if (onTop) OnTop.Add(windowKey);
            else OnTop.Remove(windowKey);
        }

        public bool IsRemembered(string windowKey) => Remembered.Contains(windowKey);

        public void SetRemembered(string windowKey, bool remembered)
        {
            Remembered.Remove(windowKey);
            if (remembered) Remembered.Add(windowKey);
        }

        public void Open(string surfaceId, IReadOnlyDictionary<string, string>? query = null) =>
            throw new NotSupportedException("The caption opens no window.");

        public Task<bool> OpenAsync(string surfaceId, IReadOnlyDictionary<string, string>? query = null) =>
            throw new NotSupportedException("The caption opens no window.");

        public void Close(string windowKey) =>
            throw new NotSupportedException("The caption closes no window: the system's close button does.");

        public void SetTitle(string windowKey, string title) =>
            throw new NotSupportedException("The caption writes no title.");

        public void ActivateMainWindow() =>
            throw new NotSupportedException("The caption activates no window.");
    }
}
