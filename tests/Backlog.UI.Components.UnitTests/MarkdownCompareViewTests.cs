namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// What the comparison view puts in the DOM: the name of its scroll region, the
/// four block states and their labels, and the collapse rules — which are the
/// part a reader notices first and the part most easily broken by accident.
/// </summary>
public sealed class MarkdownCompareViewTests
{
    /// <summary>Six paragraphs under one heading, the first and last edited, so
    /// the four unchanged blocks between them are a run bounded on both
    /// sides.</summary>
    private const string RunBefore = """
        # Guide

        One.

        Two.

        Three.

        Four.

        Five.

        Six.
        """;

    private const string RunAfter = """
        # Guide

        One, edited.

        Two.

        Three.

        Four.

        Five.

        Six, edited.
        """;

    /// <summary>One section edited, one section untouched, so exactly one
    /// section is wholly unchanged.</summary>
    private const string SectionsBefore = """
        # Guide

        ## Setup

        Install it.

        ## Notes

        Read this first.

        Then read this.
        """;

    private const string SectionsAfter = """
        # Guide

        ## Setup

        Install it, then run it once.

        ## Notes

        Read this first.

        Then read this.
        """;

    [Fact]
    public void The_scroll_region_is_named_for_the_relationship_and_not_only_the_file()
    {
        using var context = new BunitContext();

        var view = Render(context, RunBefore, RunAfter);

        // A comparison body shows a relationship, so naming it after the file
        // alone would give a screen-reader user two regions called "README.md"
        // in one app and no way to tell the compare pane from the file view.
        // The wording is fixed so it can be asserted.
        var body = view.Find(".md-compare__body");

        Assert.Equal("README.md, Last commit to Working tree", body.GetAttribute("aria-label"));
        Assert.Equal("region", body.GetAttribute("role"));
        Assert.Equal("0", body.GetAttribute("tabindex"));
    }

    [Fact]
    public void A_run_of_unchanged_blocks_folds_only_what_it_would_hide()
    {
        using var context = new BunitContext();

        var view = Render(context, RunBefore, RunAfter);

        // Four unchanged blocks, bounded by a change on each side: one kept as
        // context at each end, two hidden — which is exactly the threshold.
        var trigger = view.Find(".md-compare-fold .fold__trigger");

        Assert.Equal("2 unchanged blocks", trigger.QuerySelector(".fold__label")!.TextContent);

        // No "Show"/"Hide" verb: aria-expanded already carries the state, and a
        // verb that flipped would contradict it and rename the trigger
        // mid-interaction.
        Assert.DoesNotContain("Show", trigger.TextContent);
        Assert.DoesNotContain("Hide", trigger.TextContent);
    }

    [Fact]
    public void A_fold_is_collapsed_on_first_render_and_opens_without_moving_focus()
    {
        using var context = new BunitContext();

        var view = Render(context, RunBefore, RunAfter);
        var trigger = view.Find(".md-compare-fold .fold__trigger");

        Assert.Equal("false", trigger.GetAttribute("aria-expanded"));
        Assert.True(view.Find(".md-compare-fold .fold__region").HasAttribute("hidden"));

        trigger.Click();

        Assert.Equal("true", view.Find(".md-compare-fold .fold__trigger").GetAttribute("aria-expanded"));

        // The region is kept in the DOM rather than removed, which is what lets
        // a host measure or anchor into it — and is also why find-in-page will
        // not match inside a collapsed fold.
        Assert.False(view.Find(".md-compare-fold .fold__region").HasAttribute("hidden"));
    }

    [Fact]
    public void A_wholly_unchanged_section_is_rendered_collapsed_and_labelled_by_its_heading()
    {
        using var context = new BunitContext();

        var view = Render(context, SectionsBefore, SectionsAfter);

        var labels = view.FindAll(".md-compare-fold .fold__label").Select(label => label.TextContent).ToList();

        Assert.Contains("Unchanged — Notes", labels);
        Assert.All(
            view.FindAll(".md-compare-fold .fold__trigger"),
            trigger => Assert.Equal("false", trigger.GetAttribute("aria-expanded")));
    }

    [Fact]
    public void A_section_with_a_change_beneath_it_is_never_collapsed()
    {
        using var context = new BunitContext();

        var view = Render(context, SectionsBefore, SectionsAfter);

        // The edited section's heading is on screen without anything having to
        // be opened. This is the half of the collapse rule that makes the
        // absent breadcrumb safe: a changed section always has visible
        // ancestors.
        var headings = view.FindAll(".md-compare-section__heading").Select(heading => heading.TextContent).ToList();

        Assert.Contains("Guide", headings);
        Assert.Contains("Setup", headings);
    }

    [Fact]
    public void The_header_toggle_opens_every_fold_at_once_and_closing_it_re_collapses_them()
    {
        using var context = new BunitContext();

        var view = Render(context, SectionsBefore, SectionsAfter);

        view.Find("[data-testid=compare-unchanged]").Click();

        Assert.All(
            view.FindAll(".md-compare-fold .fold__trigger"),
            trigger => Assert.Equal("true", trigger.GetAttribute("aria-expanded")));

        view.Find("[data-testid=compare-unchanged]").Click();

        // Per-fold state is discarded either way, so the toggle always means
        // the same thing. That is the design, not a bug.
        Assert.All(
            view.FindAll(".md-compare-fold .fold__trigger"),
            trigger => Assert.Equal("false", trigger.GetAttribute("aria-expanded")));
    }

    [Fact]
    public void A_host_can_ask_for_the_whole_file_and_get_every_fold_open()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownCompareView>(parameters => parameters
            .Add(v => v.Section, MarkdownCompare.Compare(SectionsBefore, SectionsAfter))
            .Add(v => v.FileName, "README.md")
            .Add(v => v.UnchangedExpanded, true)
            .Add(v => v.TestId, "compare"));

        // For the host that is not asking "what changed" — a page documenting
        // the states, or a pane whose reader has already said they want the
        // file as a file.
        Assert.NotEmpty(view.FindAll(".md-compare-fold .fold__trigger"));
        Assert.All(
            view.FindAll(".md-compare-fold .fold__trigger"),
            trigger => Assert.Equal("true", trigger.GetAttribute("aria-expanded")));
        Assert.Equal("true", view.Find("[data-testid=compare-unchanged]").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void A_changed_block_shows_both_halves_stacked_and_labelled()
    {
        using var context = new BunitContext();

        var view = Render(context, RunBefore, RunAfter);

        var changed = view.FindAll(".md-compare-block--changed").First();

        Assert.Equal("Changed", changed.QuerySelector(".md-compare-block__label")!.TextContent);
        Assert.Equal("±", changed.QuerySelector(".md-compare-block__gutter")!.TextContent);

        // Both halves, because "Changed" with only the after side on screen is
        // an assertion this component cannot back up.
        var was = changed.QuerySelector(".md-compare-block__side--was")!;
        var now = changed.QuerySelector(".md-compare-block__side--now")!;

        Assert.Contains("One.", was.TextContent);
        Assert.Contains("One, edited.", now.TextContent);
        Assert.Equal("Was", was.QuerySelector(".md-compare-block__side-label")!.TextContent);
        Assert.Equal("Now", now.QuerySelector(".md-compare-block__side-label")!.TextContent);
    }

    [Fact]
    public void An_unchanged_block_says_nothing_extra_and_wears_no_chrome()
    {
        using var context = new BunitContext();

        var view = Render(context, RunBefore, RunAfter);

        view.Find("[data-testid=compare-unchanged]").Click();

        // Silence is the absence of change. Prefixing several hundred
        // paragraphs with "Unchanged" would drown the two that matter.
        Assert.NotEmpty(view.FindAll(".md-compare-block--unchanged"));
        Assert.Empty(view.FindAll(".md-compare-block--unchanged .md-compare-block__label"));
        Assert.All(
            view.FindAll(".md-compare-block--unchanged .md-compare-block__gutter"),
            gutter => Assert.Equal(string.Empty, gutter.TextContent));
    }

    [Fact]
    public void An_added_section_and_a_removed_section_are_each_labelled_in_words()
    {
        using var context = new BunitContext();

        var view = Render(
            context,
            "# Guide\n\n## Gone\n\nOld prose that is going away.\n",
            "# Guide\n\n## Arrived\n\nBrand new prose about something else.\n");

        var labels = view.FindAll(".md-compare-section__head .md-compare-block__label")
            .Select(label => label.TextContent)
            .ToList();

        Assert.Contains("Removed", labels);
        Assert.Contains("Added", labels);
    }

    [Fact]
    public void A_renamed_heading_shows_both_texts_and_only_one_of_them_is_a_heading()
    {
        using var context = new BunitContext();

        var view = Render(
            context,
            "# Guide\n\n## Setup\n\nInstall the CLI from the release page.\n",
            "# Guide\n\n## Installation\n\nInstall the CLI from the release page.\n");

        Assert.Equal("Was: Setup", view.Find(".md-compare-section__was").TextContent);
        Assert.Equal("Now: Installation", view.Find(".md-compare-section__now").TextContent);

        // Two heading roles would put one section into the host's outline
        // twice and land heading navigation on a title that is no longer there.
        Assert.False(view.Find(".md-compare-section__was").HasAttribute("role"));
        Assert.Equal("heading", view.Find(".md-compare-section__now").GetAttribute("role"));
    }

    [Fact]
    public void Headings_carry_their_level_without_joining_the_hosts_outline()
    {
        using var context = new BunitContext();

        var view = Render(context, SectionsBefore, SectionsAfter);

        // MarkdownView's rule, unchanged: still a paragraph, so it never joins
        // the host page's own outline, but carrying its level so heading
        // navigation works at all.
        var heading = view.Find(".md-compare-section__heading");

        Assert.Equal("P", heading.TagName);
        Assert.Equal("heading", heading.GetAttribute("role"));
        Assert.Equal("1", heading.GetAttribute("aria-level"));
        Assert.Empty(view.FindAll(".md-compare__body h1, .md-compare__body h2, .md-compare__body h3"));
    }

    [Fact]
    public void The_header_counts_what_moved_when_the_host_does_not_say()
    {
        using var context = new BunitContext();

        var view = Render(context, RunBefore, RunAfter);

        Assert.Equal("Last commit → Working tree · 2 changed", view.Find(".md-compare__meta").TextContent);
    }

    [Fact]
    public void With_no_comparison_the_view_says_so_rather_than_rendering_an_empty_frame()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownCompareView>(parameters => parameters
            .Add(v => v.Section, null)
            .Add(v => v.FileName, "README.md")
            .Add(v => v.TestId, "compare"));

        Assert.Equal("No file selected", view.Find(".empty-state__title").TextContent);
        Assert.Empty(view.FindAll(".md-compare-block"));
    }

    private static IRenderedComponent<MarkdownCompareView> Render(BunitContext context, string before, string after) =>
        context.Render<MarkdownCompareView>(parameters => parameters
            .Add(v => v.Section, MarkdownCompare.Compare(before, after))
            .Add(v => v.FileName, "README.md")
            .Add(v => v.Path, "docs/README.md")
            .Add(v => v.BeforeLabel, "Last commit")
            .Add(v => v.AfterLabel, "Working tree")
            .Add(v => v.TestId, "compare"));
}
