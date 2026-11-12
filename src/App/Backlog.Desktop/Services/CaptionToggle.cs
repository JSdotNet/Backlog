using Backlog.Desktop.UI.Shell;

namespace Backlog.Desktop.Services;

/// <summary>
/// One toggle of a popped-out window's title bar (local ADR 0043 §4): Keep on top or
/// Remember this window, a glyph in a box beside the system caption buttons.
/// <para>
/// A view of its own rather than a <see cref="Button"/>, because MAUI has no toggle
/// button: on Windows <see cref="CaptionToggleHandler"/> draws it as a WinUI
/// <c>ToggleButton</c>, which gives it a pressed look and the UI Automation Toggle
/// pattern, so a screen reader hears its state and not only its name. The name is
/// <see cref="SemanticProperties.DescriptionProperty"/>, the tooltip
/// <see cref="ToolTipProperties.TextProperty"/> and the id <see cref="Element.AutomationId"/>,
/// which MAUI's own view mapper writes onto the control.
/// </para>
/// </summary>
public sealed class CaptionToggle : View
{
    /// <summary>The backing property of <see cref="IsChecked"/>.</summary>
    public static readonly BindableProperty IsCheckedProperty =
        BindableProperty.Create(nameof(IsChecked), typeof(bool), typeof(CaptionToggle), false);

    /// <summary>The toggle's target: the title bar's full 32px height, so no strip
    /// above or below it drags the window instead of pressing it — the recorded
    /// exception to the 44px target allows the title bar's height and no less.</summary>
    public const double TargetHeight = 32;

    /// <summary>A toggle standing for <paramref name="kind"/>, which also picks its glyph.</summary>
    public CaptionToggle(SurfaceWindowCaptionToggleKind kind)
    {
        Kind = kind;

        // Set here, on the view MAUI lays out, rather than on the WinUI control:
        // MAUI's own view mapper writes the control's size and arranges it.
        WidthRequest = 36;
        HeightRequest = TargetHeight;
        VerticalOptions = LayoutOptions.Fill;

        // spacing-xs between two toggles, and nothing above or below.
        Margin = new Thickness(2, 0);
    }

    /// <summary>What the toggle stands for.</summary>
    public SurfaceWindowCaptionToggleKind Kind { get; }

    /// <summary>Whether it is drawn pressed. Set from <see cref="SurfaceWindowCaption"/>,
    /// never kept by the control alone.</summary>
    public bool IsChecked
    {
        get => (bool)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>The person pressed it, by pointer or key.</summary>
    public event EventHandler? Pressed;

    /// <summary>Raised by the handler on a press.</summary>
    internal void RaisePressed() => Pressed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// The glyph's outline on Lucide's 24x24 grid, as <c>PinIcon</c> and
    /// <c>BookmarkIcon</c> draw it in the component library, written out in absolute
    /// commands with every number apart so the XAML geometry parser reads it the way
    /// a browser reads the SVG.
    /// </summary>
    internal string Glyph => Kind switch
    {
        SurfaceWindowCaptionToggleKind.KeepOnTop =>
            "M 12,17 V 22 "
            + "M 9,10.76 A 2,2 0 0 1 7.89,12.55 L 6.11,13.45 A 2,2 0 0 0 5,15.24 V 16 A 1,1 0 0 0 6,17 H 18 "
            + "A 1,1 0 0 0 19,16 V 15.24 A 2,2 0 0 0 17.89,13.45 L 16.11,12.55 A 2,2 0 0 1 15,10.76 V 7 "
            + "A 1,1 0 0 1 16,6 A 2,2 0 0 0 16,2 H 8 A 2,2 0 0 0 8,6 A 1,1 0 0 1 9,7 Z",
        SurfaceWindowCaptionToggleKind.Remember =>
            "M 17,3 A 2,2 0 0 1 19,5 V 20 A 1,1 0 0 1 17.504,20.868 L 12.992,18.29 "
            + "A 2,2 0 0 0 11.008,18.29 L 6.496,20.868 A 1,1 0 0 1 5,20 V 5 A 2,2 0 0 1 7,3 Z",
        _ => throw new InvalidOperationException($"No glyph for {Kind}."),
    };
}

#if WINDOWS
/// <summary>
/// Draws a <see cref="CaptionToggle"/> as a WinUI <c>ToggleButton</c> in the dark
/// tokens of <c>.devbook/design/color-scheme.md</c>: transparent at rest,
/// <c>color-background-alt</c> under the pointer, and — pressed — the library
/// toggle's own pressed look, a <c>color-primary</c> edge over a 16% fill of it, with
/// the glyph lit from <c>color-text-secondary</c> to <c>color-text-primary</c>.
/// </summary>
public sealed class CaptionToggleHandler() : Microsoft.Maui.Handlers.ViewHandler<CaptionToggle, Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>(Mapper)
{
    /// <summary>MAUI's view mapper — name, tooltip, id, visibility — and the pressed state.</summary>
    public static readonly IPropertyMapper<CaptionToggle, CaptionToggleHandler> Mapper =
        new PropertyMapper<CaptionToggle, CaptionToggleHandler>(ViewMapper)
        {
            [nameof(CaptionToggle.IsChecked)] = MapIsChecked,
        };

    private static readonly Windows.UI.Color TextPrimary = CaptionColors.ToWinUI(CaptionColors.TextPrimary);
    private static readonly Windows.UI.Color TextSecondary = CaptionColors.ToWinUI(CaptionColors.TextSecondary);

    private Microsoft.UI.Xaml.Shapes.Path? _glyph;

    protected override Microsoft.UI.Xaml.Controls.Primitives.ToggleButton CreatePlatformView()
    {
        _glyph = new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = (Microsoft.UI.Xaml.Media.Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                typeof(Microsoft.UI.Xaml.Media.Geometry), VirtualView.Glyph),
            StrokeThickness = 2,
            StrokeStartLineCap = Microsoft.UI.Xaml.Media.PenLineCap.Round,
            StrokeEndLineCap = Microsoft.UI.Xaml.Media.PenLineCap.Round,
            StrokeLineJoin = Microsoft.UI.Xaml.Media.PenLineJoin.Round,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.None,
            Stroke = Brush(TextSecondary),
        };

        // The grid keeps Lucide's 24x24 box, so the glyph scales as the library's
        // 16px icon does rather than to its own outline's bounds.
        var grid = new Microsoft.UI.Xaml.Controls.Grid { Width = 24, Height = 24 };
        grid.Children.Add(_glyph);

        var toggle = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton
        {
            Content = new Microsoft.UI.Xaml.Controls.Viewbox { Width = 16, Height = 16, Child = grid },
            Padding = new Microsoft.UI.Xaml.Thickness(0),
            BorderThickness = new Microsoft.UI.Xaml.Thickness(1),
            CornerRadius = new Microsoft.UI.Xaml.CornerRadius(4),
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch,
            HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
            VerticalContentAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
            // The product is dark only, whatever the system theme says.
            RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Dark,
        };

        // Lightweight styling: the template's own states, in the product's colours
        // rather than the system accent.
        var transparent = Brush(Microsoft.UI.Colors.Transparent);
        // color-primary at 16% pressed, as the library toggle; 24% and 32% under the
        // pointer and pressed down.
        var pressedFill = Brush(CaptionColors.ToWinUI(CaptionColors.Primary, alpha: 0x29));
        var pressedFillHover = Brush(CaptionColors.ToWinUI(CaptionColors.Primary, alpha: 0x3D));
        var pressedFillDown = Brush(CaptionColors.ToWinUI(CaptionColors.Primary, alpha: 0x52));
        var edge = Brush(CaptionColors.ToWinUI(CaptionColors.Primary));
        var resources = toggle.Resources;
        resources["ToggleButtonBackground"] = transparent;
        resources["ToggleButtonBackgroundPointerOver"] = Brush(CaptionColors.ToWinUI(CaptionColors.BackgroundAlt));
        resources["ToggleButtonBackgroundPressed"] = Brush(CaptionColors.ToWinUI(CaptionColors.BackgroundRaised));
        resources["ToggleButtonBackgroundChecked"] = pressedFill;
        resources["ToggleButtonBackgroundCheckedPointerOver"] = pressedFillHover;
        resources["ToggleButtonBackgroundCheckedPressed"] = pressedFillDown;
        resources["ToggleButtonBorderBrush"] = transparent;
        resources["ToggleButtonBorderBrushPointerOver"] = transparent;
        resources["ToggleButtonBorderBrushPressed"] = transparent;
        resources["ToggleButtonBorderBrushChecked"] = edge;
        resources["ToggleButtonBorderBrushCheckedPointerOver"] = edge;
        resources["ToggleButtonBorderBrushCheckedPressed"] = edge;

        return toggle;
    }

    protected override void ConnectHandler(Microsoft.UI.Xaml.Controls.Primitives.ToggleButton platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Click += OnClick;
    }

    protected override void DisconnectHandler(Microsoft.UI.Xaml.Controls.Primitives.ToggleButton platformView)
    {
        platformView.Click -= OnClick;
        base.DisconnectHandler(platformView);
    }

    /// <summary>
    /// Click, not Checked: setting IsChecked from the caption must not read as a press.
    /// The control has already flipped itself by now, and the press is only a request:
    /// the caption redraws what the head then holds. A press that changes nothing there
    /// — a window already gone — raises no change of <see cref="CaptionToggle.IsChecked"/>,
    /// so the control is put back to the model here rather than left flipped.
    /// </summary>
    private void OnClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (VirtualView is not { } view) return;

        view.RaisePressed();
        Show(this, view);
    }

    private static void MapIsChecked(CaptionToggleHandler handler, CaptionToggle view) => Show(handler, view);

    private static void Show(CaptionToggleHandler handler, CaptionToggle view)
    {
        handler.PlatformView.IsChecked = view.IsChecked;

        // A shape takes no foreground from the control it sits in, so the glyph is
        // lit here as the template lights a text label.
        if (handler._glyph is { } glyph) glyph.Stroke = Brush(view.IsChecked ? TextPrimary : TextSecondary);
    }

    private static Microsoft.UI.Xaml.Media.SolidColorBrush Brush(Windows.UI.Color color) => new(color);
}
#endif
