namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A sheet is a drawer, and the interaction rules ask two things of a drawer that
/// are easy to leave out: it holds the focus while it is open, and it gives it
/// back when it closes. <c>Modal</c> does neither, which is exactly why these are
/// pinned here rather than assumed.
///
/// <para>The trap itself runs in JS, so what is asserted is that it is armed and
/// released at the right moments — not the cycling, which bUnit cannot
/// observe.</para>
/// </summary>
public sealed class DetailSheetTests
{
    private static IRenderedComponent<DetailSheet> Render(
        BunitContext context,
        bool open = true,
        Action<bool>? onOpenChanged = null,
        Action<ComponentParameterCollectionBuilder<DetailSheet>>? extra = null) =>
        context.Render<DetailSheet>(parameters =>
        {
            parameters.Add(s => s.Open, open);
            parameters.Add(s => s.Title, "Butter");
            parameters.Add(s => s.Kicker, "Pantry");

            if (onOpenChanged is not null)
            {
                parameters.Add(s => s.OpenChanged, EventCallback.Factory.Create<bool>(new object(), onOpenChanged));
            }

            extra?.Invoke(parameters);
        });

    /// <summary>Closed, it is still in the tree — a removed element cannot animate
    /// out — but <c>inert</c>, which keeps everything in it off the tab order and
    /// off the accessibility tree just as firmly.</summary>
    [Fact]
    public void A_closed_sheet_stays_in_the_tree_and_is_inert()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var sheet = Render(context, open: false);
        var panel = sheet.Find(".detail-sheet");

        Assert.Equal("false", panel.GetAttribute("data-open"));
        Assert.True(panel.HasAttribute("inert"));
    }

    [Fact]
    public void An_open_sheet_is_a_dialog_named_by_its_title()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var sheet = Render(context);
        var panel = sheet.Find(".detail-sheet");

        Assert.Equal("dialog", panel.GetAttribute("role"));
        Assert.Equal("true", panel.GetAttribute("aria-modal"));
        Assert.Equal("true", panel.GetAttribute("data-open"));
        Assert.False(panel.HasAttribute("inert"));

        var labelledBy = panel.GetAttribute("aria-labelledby");
        Assert.Equal("Butter", sheet.Find($"#{labelledBy}").TextContent.Trim());
    }

    [Fact]
    public void A_sheet_with_no_title_is_named_by_its_label()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var sheet = context.Render<DetailSheet>(parameters => parameters
            .Add(s => s.Open, true)
            .Add(s => s.AriaLabel, "Ingredient details"));

        var panel = sheet.Find(".detail-sheet");

        Assert.Null(panel.GetAttribute("aria-labelledby"));
        Assert.Equal("Ingredient details", panel.GetAttribute("aria-label"));
    }

    [Fact]
    public void Escape_closes_it()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        bool? open = null;

        var sheet = Render(context, onOpenChanged: value => open = value);
        sheet.Find(".detail-sheet").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.False(open);
    }

    [Fact]
    public void A_sheet_that_declines_escape_stays_open()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        bool? open = null;

        var sheet = Render(context, onOpenChanged: value => open = value,
            extra: parameters => parameters.Add(s => s.CloseOnEscape, false));

        sheet.Find(".detail-sheet").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Null(open);
    }

    [Fact]
    public void The_backdrop_closes_it()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        bool? open = null;

        var sheet = Render(context, onOpenChanged: value => open = value);
        sheet.Find(".detail-sheet-backdrop").Click();

        Assert.False(open);
    }

    [Fact]
    public void A_sheet_that_declines_the_backdrop_stays_open()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        bool? open = null;

        var sheet = Render(context, onOpenChanged: value => open = value,
            extra: parameters => parameters.Add(s => s.CloseOnBackdropClick, false));

        sheet.Find(".detail-sheet-backdrop").Click();

        Assert.Null(open);
    }

    [Fact]
    public void The_close_button_closes_it_and_says_so()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        bool? open = null;

        var sheet = Render(context, onOpenChanged: value => open = value,
            extra: parameters => parameters.Add(s => s.CloseLabel, "Close ingredient details"));

        var close = sheet.Find(".detail-sheet__close");

        Assert.Equal("Close ingredient details", close.GetAttribute("aria-label"));

        close.Click();

        Assert.False(open);
    }

    /// <summary>Opening arms the trap and names where focus goes back to. This is
    /// the half <c>Modal</c> leaves out.</summary>
    [Fact]
    public void Opening_holds_the_focus_and_remembers_where_it_came_from()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        Render(context, extra: parameters => parameters.Add(s => s.RestoreFocusToId, "open-sheet-button"));

        var invocation = Assert.Single(context.JSInterop.Invocations["backlogFocusTrap"]);

        Assert.Equal("open-sheet-button", invocation.Arguments[1]);
    }

    [Fact]
    public void Closing_gives_the_focus_back()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var sheet = Render(context);
        sheet.Render(parameters => parameters.Add(s => s.Open, false));

        Assert.Single(context.JSInterop.Invocations["backlogReleaseFocusTrap"]);
    }

    /// <summary>A sheet that never opened has nothing to release, and saying so
    /// would move a focus the reader put somewhere else.</summary>
    [Fact]
    public void A_sheet_that_never_opened_releases_nothing()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        Render(context, open: false);

        Assert.Empty(context.JSInterop.Invocations["backlogFocusTrap"]);
        Assert.Empty(context.JSInterop.Invocations["backlogReleaseFocusTrap"]);
    }

    [Fact]
    public void The_kicker_the_title_and_the_lede_are_all_optional()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var bare = context.Render<DetailSheet>(parameters => parameters.Add(s => s.Open, true));

        Assert.Empty(bare.FindAll(".detail-sheet__kicker"));
        Assert.Empty(bare.FindAll(".detail-sheet__title"));
        Assert.Empty(bare.FindAll(".detail-sheet__lede"));

        var dressed = Render(context, extra: parameters => parameters.Add(s => s.Lede, "A staple."));

        Assert.Equal("Pantry", dressed.Find(".detail-sheet__kicker").TextContent.Trim());
        Assert.Equal("A staple.", dressed.Find(".detail-sheet__lede").TextContent.Trim());
    }

    /// <summary>No footer, no footer element. An empty bar under the body is a
    /// rule the sheet draws for nothing.</summary>
    [Fact]
    public void The_footer_is_only_drawn_when_there_is_one()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        Assert.Empty(Render(context).FindAll(".detail-sheet__footer"));

        var footed = Render(context, extra: parameters => parameters
            .Add(s => s.Footer, (RenderFragment)(builder => builder.AddMarkupContent(0, "<span>pager</span>"))));

        Assert.Equal("pager", footed.Find(".detail-sheet__footer").TextContent.Trim());
    }
}
