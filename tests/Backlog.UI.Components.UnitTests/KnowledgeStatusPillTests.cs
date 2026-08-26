namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The status on its own, which is the whole reason this component exists: a
/// folder-aware pill used to be reachable only by handing MetadataView a
/// block and taking the description list that came with it, so a row of
/// statuses read "status draft status proposed status active".
///
/// <para>What it emits has to stay identical to what the record's headline
/// emits, because it is the same element: the application's status badge, with
/// the modifier the folder's word maps onto. The classes below are matched by the
/// stylesheet and by every status assertion in the suite.</para>
/// </summary>
public sealed class KnowledgeStatusPillTests
{
    [Fact]
    public void The_pill_is_a_bare_span_with_no_label_and_no_list_around_it()
    {
        using var context = new BunitContext();

        var pill = context.Render<KnowledgeStatusPill>(parameters => parameters
            .Add(p => p.Status, "draft"));

        var span = pill.Find("span");

        // The plain status badge and no state modifier: the modifier comes from
        // the tone, and with no folder there is no tone.
        Assert.Equal("badge badge--status", span.GetAttribute("class"));
        Assert.Equal("draft", span.TextContent);

        // The span and nothing else. A label repeated before every pill is
        // exactly what this component was extracted to stop.
        Assert.Equal(span.OuterHtml, pill.Markup.Trim());
    }

    [Fact]
    public void No_status_means_no_element()
    {
        // A block that states no status should not leave an empty pill sitting
        // on the line.
        using var context = new BunitContext();

        foreach (var status in new string?[] { null, "", "   " })
        {
            var pill = context.Render<KnowledgeStatusPill>(parameters => parameters
                .Add(p => p.Status, status));

            Assert.Equal(string.Empty, pill.Markup.Trim());
        }
    }

    [Fact]
    public void Without_a_folder_nothing_is_judged()
    {
        using var context = new BunitContext();

        var pill = context.Render<KnowledgeStatusPill>(parameters => parameters
            .Add(p => p.Status, "shipped"));

        var span = pill.Find("span");
        Assert.Equal("badge badge--status", span.GetAttribute("class"));
        Assert.Null(span.GetAttribute("title"));
    }

    [Fact]
    public void With_a_folder_the_pill_wears_the_badge_that_folders_tone_maps_onto()
    {
        // `adopted` is `.tech`'s word for live and current, and the application
        // spells that state `active`. Nothing defines `.badge--status-adopted`, so
        // a badge left to spell its own modifier from the word would fall through
        // to plain grey — which is what "no status" looks like.
        using var context = new BunitContext();

        var pill = context.Render<KnowledgeStatusPill>(parameters => parameters
            .Add(p => p.Status, "adopted")
            .Add(p => p.Folder, KnowledgeFolder.Tech));

        var span = pill.Find("span");
        Assert.Equal("badge badge--status badge--status-active", span.GetAttribute("class"));
        Assert.Equal("adopted", span.TextContent);
        Assert.Null(span.GetAttribute("title"));
    }

    [Fact]
    public void A_status_the_folder_does_not_define_is_flagged_and_says_what_was_expected()
    {
        using var context = new BunitContext();

        var pill = context.Render<KnowledgeStatusPill>(parameters => parameters
            .Add(p => p.Status, "shipped")
            .Add(p => p.Folder, KnowledgeFolder.Tech));

        var span = pill.Find("span");
        Assert.Contains("knowledge-status--unrecognised", span.ClassList);

        // `archived` is the badge that spends no colour, so the flag reads as
        // "not a state" rather than as an alarm. Borrowing `draft` would report a
        // verdict nobody reached, and `blocked` would be indistinguishable from a
        // status that genuinely is.
        Assert.Equal(
            "badge badge--status badge--status-archived knowledge-status--unrecognised",
            span.GetAttribute("class"));
        Assert.DoesNotContain("badge--status-blocked", span.ClassList);
        Assert.Contains("candidate, trial, adopted, hold, retired", span.GetAttribute("title"));
    }

    [Fact]
    public void A_caller_class_is_appended_and_never_displaces_the_ones_the_stylesheet_matches()
    {
        using var context = new BunitContext();

        var pill = context.Render<KnowledgeStatusPill>(parameters => parameters
            .Add(p => p.Status, "adopted")
            .Add(p => p.Folder, KnowledgeFolder.Tech)
            .Add(p => p.CssClass, "sb-swatch")
            .Add(p => p.TestId, "state-pill")
            .AddUnmatched("data-role", "state"));

        var span = pill.Find("span");
        Assert.Equal(
            "badge badge--status badge--status-active sb-swatch",
            span.GetAttribute("class"));
        Assert.Equal("state-pill", span.GetAttribute("data-testid"));
        Assert.Equal("state", span.GetAttribute("data-role"));
    }

    [Fact]
    public void The_pill_renders_what_the_record_headline_renders()
    {
        // Two components, one element. If these ever diverge the stylesheet is
        // painting one of them and not the other.
        //
        // The status is deliberately one the folder does not define. A record
        // whose folder *and* status are both known draws a BadgeSelect instead —
        // so this is the case where the two still meet, and it is the one worth
        // pinning anyway: the flagged pill is the most elaborate thing either
        // component emits.
        using var context = new BunitContext();

        var standalone = context.Render<KnowledgeStatusPill>(parameters => parameters
            .Add(p => p.Status, "shipped")
            .Add(p => p.Folder, KnowledgeFolder.Tech));

        var inRecord = context.Render<MetadataView>(parameters => parameters
            .Add(v => v.Metadata, MetadataReader.Parse("status: shipped"))
            .Add(v => v.Vocabulary, KnowledgeStatus.Vocabulary(KnowledgeFolder.Tech)));

        Assert.Equal(
            standalone.Find("span").OuterHtml,
            inRecord.Find(".knowledge-record__headline .badge--status").OuterHtml);
    }

    [Fact]
    public void The_pill_is_never_a_control_however_much_the_folder_is_known()
    {
        // The record's headline swaps in a select where a vocabulary exists; the
        // pill does not, and must not. The State page's vocabulary and tone rows
        // are showing what a status *looks* like, and twenty selects would be
        // twenty invitations to change something that is not there to be changed.
        using var context = new BunitContext();

        foreach (var folder in Enum.GetValues<KnowledgeFolder>())
        {
            var pill = context.Render<KnowledgeStatusPill>(parameters => parameters
                .Add(p => p.Status, "draft")
                .Add(p => p.Folder, folder));

            Assert.Empty(pill.FindAll("select"));
            Assert.Equal("draft", pill.Find("span").TextContent);
        }
    }
}
