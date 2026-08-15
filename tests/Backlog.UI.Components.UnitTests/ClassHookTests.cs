namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Slice 5 leaned on these parameters to keep the app's DOM identical while the
/// markup moved into the library, so what they are worth pinning for is that
/// they <em>replace</em> the library's own class names rather than adding to them.
/// </summary>
public sealed class ClassHookTests
{
    [Fact]
    public void An_empty_state_can_be_dressed_entirely_in_the_hosts_own_names()
    {
        using var context = new BunitContext();

        var empty = context.Render<EmptyState>(parameters => parameters
            .Add(e => e.BaseClass, "backlog-empty")
            .Add(e => e.TitleCssClass, "backlog-empty__head")
            .Add(e => e.DescriptionCssClass, "backlog-empty__body")
            .Add(e => e.ActionCssClass, "backlog-empty__action")
            .Add(e => e.Title, "Nothing captured yet")
            .Add(e => e.Description, "Write the first entry")
            .Add(e => e.Action, "<button type=\"button\">New</button>"));

        Assert.Equal("backlog-empty", empty.Find("div").GetAttribute("class"));
        Assert.NotNull(empty.Find(".backlog-empty__head"));
        Assert.NotNull(empty.Find(".backlog-empty__body"));
        Assert.NotNull(empty.Find(".backlog-empty__action button"));
        Assert.Empty(empty.FindAll(".empty-state"));
    }

    [Fact]
    public void A_null_action_class_drops_the_action_wrapper_entirely()
    {
        using var context = new BunitContext();

        var empty = context.Render<EmptyState>(parameters => parameters
            .Add(e => e.ActionCssClass, null)
            .Add(e => e.Action, "<button type=\"button\">New</button>"));

        Assert.Empty(empty.FindAll(".empty-state__action"));
        Assert.NotNull(empty.Find(".empty-state > button"));
    }

    [Fact]
    public void A_badge_is_its_base_class_plus_the_kind_and_value_it_carries()
    {
        using var context = new BunitContext();

        var badge = context.Render<Badge>(parameters => parameters
            .Add(b => b.Kind, "status")
            .Add(b => b.Slug, "ready"));

        Assert.Equal("badge badge--status badge--status-ready", badge.Find("span").GetAttribute("class"));
    }

    [Fact]
    public void A_badge_with_no_kind_collapses_to_a_single_modifier()
    {
        using var context = new BunitContext();

        var badge = context.Render<Badge>(parameters => parameters
            .Add(b => b.BaseClass, "chip")
            .Add(b => b.Kind, string.Empty)
            .Add(b => b.Slug, "ready"));

        Assert.Equal("chip chip--ready", badge.Find("span").GetAttribute("class"));
    }

    [Fact]
    public void A_status_badge_derives_its_slug_from_the_status_and_can_still_show_other_text()
    {
        using var context = new BunitContext();

        var badge = context.Render<StatusBadge>(parameters => parameters
            .Add(b => b.Status, "In Progress")
            .Add(b => b.Text, "Working on it"));

        var element = badge.Find("span");

        // Anything that is not a letter, a digit or a hyphen is dropped rather
        // than translated, which is what `.badge--status-inprogress` expects.
        Assert.Contains("badge--status-inprogress", element.ClassList);
        Assert.Equal("Working on it", element.TextContent);
    }

    [Fact]
    public void An_unknown_status_falls_back_rather_than_producing_a_class_of_nothing()
    {
        using var context = new BunitContext();

        var badge = context.Render<StatusBadge>(parameters => parameters
            .Add(b => b.Status, "   ")
            .Add(b => b.FallbackSlug, "draft"));

        Assert.Contains("badge--status-draft", badge.Find("span").ClassList);
    }

    [Fact]
    public void An_alert_with_nothing_to_say_renders_nothing()
    {
        using var context = new BunitContext();

        var alert = context.Render<Alert>();

        Assert.Equal(string.Empty, alert.Markup.Trim());
    }

    [Fact]
    public void An_alert_can_be_pointed_at_the_hosts_own_styling()
    {
        using var context = new BunitContext();

        var alert = context.Render<Alert>(parameters => parameters
            .Add(a => a.Message, "Could not open the folder")
            .Add(a => a.BaseClass, "knowledge-menu__error")
            .Add(a => a.CssClass, "knowledge-menu__error--open"));

        var element = alert.Find("p");

        Assert.Equal("knowledge-menu__error knowledge-menu__error--open", element.GetAttribute("class"));
        Assert.Equal("alert", element.GetAttribute("role"));
    }
}
