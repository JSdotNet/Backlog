using AngleSharp.Dom;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Inbox pane on its own: what a row says, and how the drawers fold.
///
/// <para>Rendered without the shell around it, because everything here is the
/// pane's — the kind mark and its word, the source badge and the person chip,
/// the PARA drawers, the lens switch and the fold state. The shell's tests keep
/// pinning that the pane is composed rather than inlined.</para>
/// </summary>
public sealed class InboxPaneTests
{
    private static readonly InboxItem Video = new("v", "Aspire 13 — what changed", null)
    {
        Kind = InboxItemKind.YouTube,
        Source = new InboxSource("youtube"),
        Tags = ["aspire"],
        Para = ParaCategory.Resources
    };

    private static readonly InboxItem Shared = new("s", "Local-first sync patterns", "Platform")
    {
        Kind = InboxItemKind.Article,
        Source = new InboxSource("web_clipper", "@maria"),
        Tags = ["sync", "aspire"],
        Repository = "JSdotNet/Backlog",
        Para = ParaCategory.Projects
    };

    private static readonly InboxItem Note = new("n", "Ask about the trial length", null)
    {
        Source = new InboxSource("manual")
    };

    private static IRenderedComponent<InboxPane> Render(BunitContext context, params InboxItem[] items) =>
        context.Render<InboxPane>(parameters => parameters.Add(pane => pane.Items, items));

    // --- What a row says --------------------------------------------------

    [Fact]
    public void A_row_says_what_kind_of_thing_it_is_with_a_mark_and_the_word()
    {
        using var context = new BunitContext();

        var pane = Render(context, Video);

        var kind = pane.Find("[data-testid='inbox-pane-item-kind']");
        var mark = kind.QuerySelector("svg");

        Assert.NotNull(mark);
        Assert.Contains("capture-kind-marker--youtube", mark.ClassList);
        // The word is printed and the mark is hidden, so a listener hears it once.
        Assert.Equal("true", mark.GetAttribute("aria-hidden"));
        Assert.Contains("YouTube", kind.TextContent);
        Assert.Equal("youtube", pane.Find(".inbox-pane__item").GetAttribute("data-inbox-kind"));
    }

    [Fact]
    public void A_row_says_where_it_came_from_as_a_channel_badge_and_a_person_chip()
    {
        using var context = new BunitContext();

        var pane = Render(context, Shared);

        var source = pane.Find("[data-testid='inbox-pane-item-source']");
        Assert.Contains("badge--source", source.ClassList);
        Assert.Equal("Web clipper", source.TextContent.Trim());

        var person = pane.Find("[data-testid='inbox-pane-item-person']");
        Assert.Contains("tag-chip--person", person.ClassList);
        Assert.Equal("@maria", person.TextContent.Trim());
        Assert.Equal("Shared by @maria", person.GetAttribute("title"));
    }

    [Fact]
    public void A_row_with_nobody_behind_it_shows_the_channel_and_no_chip()
    {
        using var context = new BunitContext();

        var pane = Render(context, Video);

        Assert.Equal("YouTube", pane.Find("[data-testid='inbox-pane-item-source']").TextContent.Trim());
        Assert.Empty(pane.FindAll("[data-testid='inbox-pane-item-person']"));
    }

    [Fact]
    public void A_row_prints_its_tags_and_its_repository()
    {
        using var context = new BunitContext();

        var pane = Render(context, Shared);

        Assert.Equal(["#sync", "#aspire"], pane.FindAll("[data-testid='inbox-pane-item-tag']").Select(chip => chip.TextContent.Trim()));
        Assert.Equal("JSdotNet/Backlog", pane.Find("[data-testid='inbox-pane-item-repository']").TextContent.Trim());
    }

    // --- The drawers ------------------------------------------------------

    [Fact]
    public void The_queue_is_always_read_as_para_drawers_with_their_counts()
    {
        using var context = new BunitContext();

        var pane = Render(context, Video, Shared, Note);

        Assert.Equal("true", pane.Find("[data-testid='inbox-pane-group-none']").GetAttribute("aria-pressed"));

        var drawers = pane.FindAll("[data-testid='inbox-pane-drawer']");
        Assert.Equal(["projects", "resources", "unsorted"], drawers.Select(drawer => drawer.GetAttribute("data-inbox-drawer")));
        Assert.Equal(["Projects", "Resources", "Unsorted"], pane.FindAll(".inbox-pane__drawer-name").Select(name => name.TextContent.Trim()));
        Assert.Equal(["1", "1", "1"], pane.FindAll("[data-testid='inbox-pane-drawer-count']").Select(count => count.TextContent.Trim()));
    }

    [Fact]
    public void Projects_are_sectioned_per_project_and_the_other_drawers_are_one_list()
    {
        using var context = new BunitContext();

        var pane = Render(context, Video, Shared, Note);

        var projects = pane.Find("[data-inbox-drawer='projects']");
        var section = Assert.Single(projects.QuerySelectorAll("[data-testid='inbox-pane-group']"));
        Assert.Equal("projects-repo-jsdotnet-backlog", section.GetAttribute("data-inbox-group"));
        Assert.Equal("JSdotNet/Backlog", section.QuerySelector(".inbox-pane__group-name")!.TextContent.Trim());
        Assert.Equal("1", section.QuerySelector("[data-testid='inbox-pane-group-count']")!.TextContent.Trim());

        var resources = pane.Find("[data-inbox-drawer='resources']");
        Assert.Empty(resources.QuerySelectorAll("[data-testid='inbox-pane-group']"));
        Assert.Single(resources.QuerySelectorAll("[data-testid='inbox-pane-list'] .inbox-pane__item"));
    }

    [Fact]
    public void The_triage_button_is_the_first_button_in_a_row_under_every_lens()
    {
        using var context = new BunitContext();

        InboxItem? opened = null;
        var pane = context.Render<InboxPane>(parameters => parameters
            .Add(p => p.Items, new[] { Shared })
            .Add(p => p.OnOpen, (InboxItem item) => opened = item));

        pane.Find("[data-testid='inbox-pane-list'] .inbox-pane__item button").Click();
        Assert.Same(Shared, opened);

        opened = null;
        pane.Find("[data-testid='inbox-pane-group-tag']").Click();
        pane.Find("[data-testid='inbox-pane-list'] .inbox-pane__item button").Click();
        Assert.Same(Shared, opened);
    }

    [Fact]
    public void The_tag_lens_sections_every_drawer_and_an_item_sits_under_each_of_its_tags()
    {
        using var context = new BunitContext();

        var pane = Render(context, Video, Shared, Note);

        pane.Find("[data-testid='inbox-pane-group-tag']").Click();

        Assert.Equal("true", pane.Find("[data-testid='inbox-pane-group-tag']").GetAttribute("aria-pressed"));
        Assert.Equal("false", pane.Find("[data-testid='inbox-pane-group-none']").GetAttribute("aria-pressed"));

        // The drawers are untouched by the lens.
        Assert.Equal(["projects", "resources", "unsorted"], pane.FindAll("[data-testid='inbox-pane-drawer']").Select(drawer => drawer.GetAttribute("data-inbox-drawer")));

        var sections = pane.FindAll("[data-testid='inbox-pane-group']").Select(section => section.GetAttribute("data-inbox-group"));
        Assert.Equal(["projects-tag-aspire", "projects-tag-sync", "resources-tag-aspire", "unsorted-untagged"], sections);
        Assert.Equal(4, pane.FindAll(".inbox-pane__item").Count);
    }

    [Fact]
    public void The_repository_lens_names_the_repo_and_the_rest_inside_each_drawer()
    {
        using var context = new BunitContext();

        var pane = Render(context, Video, Shared, Note);

        pane.Find("[data-testid='inbox-pane-group-repo']").Click();

        Assert.Equal(["JSdotNet/Backlog", "No repository", "No repository"], pane.FindAll(".inbox-pane__group-name").Select(name => name.TextContent.Trim()));
        Assert.Equal(["projects-repo-jsdotnet-backlog", "resources-no-repository", "unsorted-no-repository"], pane.FindAll("[data-testid='inbox-pane-group']").Select(section => section.GetAttribute("data-inbox-group")));
    }

    // --- Folding ----------------------------------------------------------

    [Fact]
    public void A_drawer_folds_its_contents_away_through_the_librarys_own_region()
    {
        using var context = new BunitContext();

        var pane = Render(context, Video, Shared, Note);

        var toggle = pane.FindAll("[data-testid='inbox-pane-drawer-toggle']")[0];
        var regionId = toggle.GetAttribute("aria-controls");
        Assert.False(string.IsNullOrEmpty(regionId));
        Assert.Equal("true", toggle.GetAttribute("aria-expanded"));
        Assert.Equal("Projects, 1 item", toggle.GetAttribute("aria-label"));

        var region = pane.Find("#" + regionId);
        Assert.Contains("fold__region", region.ClassList);
        Assert.False(region.HasAttribute("hidden"));
        Assert.Single(region.QuerySelectorAll(".inbox-pane__item"));

        toggle.Click();

        toggle = pane.FindAll("[data-testid='inbox-pane-drawer-toggle']")[0];
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.True(pane.Find("#" + regionId).HasAttribute("hidden"));

        // The other drawers are untouched.
        Assert.Equal("true", pane.FindAll("[data-testid='inbox-pane-drawer-toggle']")[1].GetAttribute("aria-expanded"));
    }

    [Fact]
    public void A_section_folds_on_its_own_inside_an_open_drawer()
    {
        using var context = new BunitContext();

        var pane = Render(context, Video, Shared, Note);

        var toggle = pane.Find("[data-testid='inbox-pane-group-toggle']");
        var regionId = toggle.GetAttribute("aria-controls");
        Assert.Equal("JSdotNet/Backlog, 1 item", toggle.GetAttribute("aria-label"));

        toggle.Click();

        Assert.Equal("false", pane.Find("[data-testid='inbox-pane-group-toggle']").GetAttribute("aria-expanded"));
        Assert.True(pane.Find("#" + regionId).HasAttribute("hidden"));
        Assert.Equal("true", pane.FindAll("[data-testid='inbox-pane-drawer-toggle']")[0].GetAttribute("aria-expanded"));
    }

    [Fact]
    public void A_drawer_fold_outlives_the_lens_and_a_section_fold_belongs_to_it()
    {
        using var context = new BunitContext();

        var pane = Render(context, Video, Shared, Note);
        pane.FindAll("[data-testid='inbox-pane-drawer-toggle']")[0].Click();
        pane.Find("[data-testid='inbox-pane-group-tag']").Click();

        // Projects stays shut whichever way its rows are split.
        Assert.Equal("false", pane.FindAll("[data-testid='inbox-pane-drawer-toggle']")[0].GetAttribute("aria-expanded"));

        // A section shut under the tag lens is open again under the repo lens,
        // and shut again on the way back.
        pane.FindAll("[data-testid='inbox-pane-drawer-toggle']")[0].Click();
        pane.FindAll("[data-testid='inbox-pane-group-toggle']")[0].Click();
        Assert.Equal("false", pane.FindAll("[data-testid='inbox-pane-group-toggle']")[0].GetAttribute("aria-expanded"));

        pane.Find("[data-testid='inbox-pane-group-repo']").Click();
        Assert.All(pane.FindAll("[data-testid='inbox-pane-group-toggle']"), t => Assert.Equal("true", t.GetAttribute("aria-expanded")));

        pane.Find("[data-testid='inbox-pane-group-tag']").Click();
        Assert.Equal("false", pane.FindAll("[data-testid='inbox-pane-group-toggle']")[0].GetAttribute("aria-expanded"));
    }

    // --- The lens and the host --------------------------------------------

    [Fact]
    public void An_empty_inbox_offers_no_drawers_and_no_lens()
    {
        using var context = new BunitContext();

        var pane = Render(context);

        Assert.NotEmpty(pane.FindAll(".inbox-pane__empty"));
        Assert.Empty(pane.FindAll("[data-testid='inbox-pane-group-by']"));
        Assert.Empty(pane.FindAll("[data-testid='inbox-pane-drawer']"));
    }

    [Fact]
    public void The_pane_can_start_out_under_a_lens()
    {
        using var context = new BunitContext();

        var pane = context.Render<InboxPane>(parameters => parameters
            .Add(p => p.Items, new[] { Video, Shared })
            .Add(p => p.Grouping, InboxGrouping.Repository));

        Assert.Equal("true", pane.Find("[data-testid='inbox-pane-group-repo']").GetAttribute("aria-pressed"));
        Assert.Equal(2, pane.FindAll("[data-testid='inbox-pane-group']").Count);
    }

    [Fact]
    public void Pressing_a_lens_tells_the_host_so_it_survives_a_re_mount()
    {
        using var context = new BunitContext();

        InboxGrouping? told = null;
        var pane = context.Render<InboxPane>(parameters => parameters
            .Add(p => p.Items, new[] { Video, Shared })
            .Add(p => p.GroupingChanged, (InboxGrouping grouping) => told = grouping));

        pane.Find("[data-testid='inbox-pane-group-tag']").Click();

        Assert.Equal(InboxGrouping.Tag, told);
        Assert.Equal("true", pane.Find("[data-testid='inbox-pane-group-tag']").GetAttribute("aria-pressed"));

        // A fresh instance handed the choice back is where the reader left it.
        var again = context.Render<InboxPane>(parameters => parameters
            .Add(p => p.Items, new[] { Video, Shared })
            .Add(p => p.Grouping, told!.Value));

        Assert.Equal("true", again.Find("[data-testid='inbox-pane-group-tag']").GetAttribute("aria-pressed"));
    }
}
