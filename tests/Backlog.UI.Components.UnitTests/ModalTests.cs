namespace Backlog.UI.Components.UnitTests;

public sealed class ModalTests
{
    [Fact]
    public void A_closed_dialog_puts_nothing_in_the_document()
    {
        // Nothing behind it should be reachable through a hidden node, so the
        // closed state is an absence rather than a `hidden` attribute.
        using var context = new BunitContext();

        var modal = context.Render<Modal>(parameters => parameters.AddChildContent("<p>Body</p>"));

        Assert.Equal(string.Empty, modal.Markup.Trim());
    }

    [Fact]
    public void An_open_dialog_is_a_modal_dialog_labelled_by_its_title()
    {
        using var context = new BunitContext();

        var modal = context.Render<Modal>(parameters => parameters
            .Add(m => m.Open, true)
            .Add(m => m.Title, "Delete entry")
            .AddChildContent("<p>Body</p>"));

        var dialog = modal.Find("[role='dialog']");

        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.Equal(modal.Find(".modal__title").Id, dialog.GetAttribute("aria-labelledby"));
        Assert.False(dialog.HasAttribute("aria-label"));
    }

    [Fact]
    public void Without_a_title_the_dialog_carries_its_own_label()
    {
        using var context = new BunitContext();

        var modal = context.Render<Modal>(parameters => parameters
            .Add(m => m.Open, true)
            .Add(m => m.AriaLabel, "Entry actions")
            .AddChildContent("<p>Body</p>"));

        Assert.Equal("Entry actions", modal.Find("[role='dialog']").GetAttribute("aria-label"));
    }

    [Fact]
    public void Escape_closes_the_dialog_only_when_it_is_allowed_to()
    {
        using var context = new BunitContext();
        var closed = 0;
        var ignored = 0;

        var closes = context.Render<Modal>(parameters => parameters
            .Add(m => m.Open, true)
            .Add(m => m.OnClosed, () => closed++)
            .AddChildContent("<p>Body</p>"));
        var stays = context.Render<Modal>(parameters => parameters
            .Add(m => m.Open, true)
            .Add(m => m.CloseOnEscape, false)
            .Add(m => m.OnClosed, () => ignored++)
            .AddChildContent("<p>Body</p>"));

        closes.Find("[role='dialog']").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        stays.Find("[role='dialog']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal(1, closed);
        Assert.Equal(0, ignored);
        Assert.Equal(string.Empty, closes.Markup.Trim());
    }

    [Fact]
    public void A_backdrop_click_closes_the_dialog_only_when_it_is_allowed_to()
    {
        using var context = new BunitContext();
        var closed = 0;
        var ignored = 0;

        var closes = context.Render<Modal>(parameters => parameters
            .Add(m => m.Open, true)
            .Add(m => m.OnClosed, () => closed++)
            .AddChildContent("<p>Body</p>"));
        var stays = context.Render<Modal>(parameters => parameters
            .Add(m => m.Open, true)
            .Add(m => m.CloseOnBackdropClick, false)
            .Add(m => m.OnClosed, () => ignored++)
            .AddChildContent("<p>Body</p>"));

        closes.Find(".modal-backdrop").Click();
        stays.Find(".modal-backdrop").Click();

        Assert.Equal(1, closed);
        Assert.Equal(0, ignored);
    }

    [Fact]
    public void The_host_can_choose_the_element_the_classes_and_whether_the_body_is_wrapped()
    {
        // Slice 5 leaned on exactly these hooks to keep the app's dialog DOM
        // unchanged while the behaviour moved into the library.
        using var context = new BunitContext();

        var modal = context.Render<Modal>(parameters => parameters
            .Add(m => m.Open, true)
            .Add(m => m.Element, "section")
            .Add(m => m.BaseClass, "entry-dialog")
            .Add(m => m.BackdropCssClass, "entry-dialog__scrim")
            .Add(m => m.UseBodyWrapper, false)
            .Add(m => m.Header, "<header class=\"entry-dialog__head\">Head</header>")
            .AddChildContent("<p class=\"entry-dialog__body\">Body</p>"));

        var dialog = modal.Find("[role='dialog']");

        Assert.Equal("section", dialog.NodeName.ToLowerInvariant());
        Assert.Equal("entry-dialog", dialog.GetAttribute("class"));
        Assert.NotNull(modal.Find(".entry-dialog__scrim"));
        Assert.Empty(modal.FindAll(".modal__body"));
        Assert.Empty(modal.FindAll(".modal__header"));
        Assert.NotNull(modal.Find(".entry-dialog__head"));
        Assert.Equal("p", dialog.Children.Last().NodeName.ToLowerInvariant());
    }

    [Fact]
    public void The_footer_is_only_rendered_when_there_is_one()
    {
        using var context = new BunitContext();

        var without = context.Render<Modal>(parameters => parameters
            .Add(m => m.Open, true)
            .AddChildContent("<p>Body</p>"));
        var with = context.Render<Modal>(parameters => parameters
            .Add(m => m.Open, true)
            .Add(m => m.Footer, "<button type=\"button\">Ok</button>")
            .AddChildContent("<p>Body</p>"));

        Assert.Empty(without.FindAll(".modal__footer"));
        Assert.NotNull(with.Find(".modal__footer button"));
    }
}
