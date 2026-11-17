using Backlog.Desktop.UI.Shell;

namespace Backlog.Desktop.Services;

/// <summary>
/// A popped-out window's native title bar (local ADR 0043 §4): the window's title,
/// then — just left of the system's minimise, maximise and close, which the system
/// keeps drawing — the toggles <see cref="SurfaceWindowCaption"/> answers for it.
/// <para>
/// A MAUI <see cref="TitleBar"/>, which replaces the default title bar MAUI's Windows
/// window draws over its extended content and does the passthrough bookkeeping
/// itself, so dragging, double-click to maximise, the system menu and Snap layouts
/// stay the system's. 32px high, the system caption buttons' height: the toggles
/// beside them are a recorded exception to the 44px target. Dark, in the tokens of
/// <c>.devbook/design/color-scheme.md</c>, active and inactive.
/// </para>
/// Built and touched on the main thread only.
/// </summary>
public sealed class SurfaceWindowTitleBar
{
    // The toggles fill the bar's height: no strip above or below them drags the
    // window where a press was meant. Their margins keep them spacing-xs apart.
    private readonly HorizontalStackLayout _trailing = new() { Spacing = 0, VerticalOptions = LayoutOptions.Fill };
    private readonly Dictionary<SurfaceWindowCaptionToggleKind, CaptionToggle> _toggles = [];

    /// <summary>A title bar under <paramref name="title"/>, whose toggles call
    /// <paramref name="pressed"/> with what was pressed.</summary>
    public SurfaceWindowTitleBar(string title, Action<SurfaceWindowCaptionToggleKind> pressed)
    {
        ArgumentNullException.ThrowIfNull(pressed);

        foreach (var kind in Enum.GetValues<SurfaceWindowCaptionToggleKind>())
        {
            var toggle = new CaptionToggle(kind) { IsVisible = false };
            toggle.Pressed += (_, _) => pressed(kind);
            _toggles[kind] = toggle;
        }

        View = new TitleBar
        {
            Title = title,
            HeightRequest = CaptionToggle.TargetHeight,
            BackgroundColor = CaptionColors.Background,
            ForegroundColor = CaptionColors.TextPrimary,
            TrailingContent = _trailing,
        };

        // The title dims while another window has the focus, as the system's does,
        // and stays legible: color-text-secondary still reads 10:1 on the background.
        var active = new VisualState { Name = "TitleBarTitleActive" };
        active.Setters.Add(new Setter { Property = TitleBar.ForegroundColorProperty, Value = CaptionColors.TextPrimary });
        var inactive = new VisualState { Name = "TitleBarTitleInactive" };
        inactive.Setters.Add(new Setter { Property = TitleBar.ForegroundColorProperty, Value = CaptionColors.TextSecondary });
        var states = new VisualStateGroup { Name = "TitleActiveStates" };
        states.States.Add(active);
        states.States.Add(inactive);
        VisualStateManager.SetVisualStateGroups(View, [states]);
    }

    /// <summary>The view <see cref="Window.TitleBar"/> takes.</summary>
    public TitleBar View { get; }

    /// <summary>The title, after a jump switched the surface the window shows.</summary>
    public void SetTitle(string title) => View.Title = title;

    /// <summary>Draws <paramref name="caption"/>: its toggles, in its order, each named,
    /// tooltipped and pressed as it says, and none it leaves out.</summary>
    public void Show(SurfaceWindowCaption caption)
    {
        ArgumentNullException.ThrowIfNull(caption);

        var order = caption.Toggles.Select(toggle => _toggles[toggle.Kind]).ToList();
        if (!_trailing.Children.SequenceEqual(order))
        {
            _trailing.Children.Clear();
            foreach (var toggle in order) _trailing.Children.Add(toggle);
        }

        foreach (var (kind, toggle) in _toggles)
        {
            var shown = caption.Toggles.FirstOrDefault(candidate => candidate.Kind == kind);
            toggle.IsVisible = shown is not null;
            if (shown is null) continue;

            // MAUI takes an element's AutomationId once; a toggle's never changes.
            toggle.AutomationId ??= shown.AutomationId;
            SemanticProperties.SetDescription(toggle, shown.Name);
            ToolTipProperties.SetText(toggle, shown.ToolTip);
            toggle.IsChecked = shown.Pressed;
        }
    }
}
