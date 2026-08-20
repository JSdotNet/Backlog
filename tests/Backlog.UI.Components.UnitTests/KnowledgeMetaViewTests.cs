namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// What a metadata block looks like once it is read as a record rather than
/// printed as a fenced listing. The rules worth pinning are the ones a reader
/// would not notice were missing: a reference is only ever a control when
/// something is listening, and a status is only judged when the folder that
/// defines it is known.
/// </summary>
public sealed class KnowledgeMetaViewTests
{
    [Fact]
    public void A_block_that_states_nothing_renders_nothing()
    {
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMetadata.Empty));

        Assert.Equal(string.Empty, view.Markup.Trim());
    }

    [Fact]
    public void Every_populated_field_gets_its_own_labelled_row()
    {
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("""
                status: adopted
                kind: format
                version: "10.0"
                related: [".tech/technology-graph.md"]
                depends-on: [".tech/shared.md#markdown"]
                alternatives: [YamlDotNet]
                """)));

        // No `status` row. The status is on the headline now, and a record that
        // showed it in both places would be stating it twice.
        Assert.Equal(
            ["related", "depends-on", "alternatives", "kind", "version"],
            view.FindAll("dt").Select(label => label.TextContent));
    }

    [Fact]
    public void Without_a_folder_the_status_badge_claims_no_state()
    {
        // A host that has not said where the block came from gets the status
        // shown and not judged: the plain badge, with no state modifier on it.
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("status: adopted")));

        Assert.Equal("badge badge--status", view.Find(".badge--status").GetAttribute("class"));
        Assert.Equal("adopted", view.Find(".badge--status").TextContent);
    }

    [Fact]
    public void With_a_folder_the_status_becomes_a_select_over_that_folders_vocabulary()
    {
        // A folder is a list of allowed values, and once the list is known the
        // status can be offered rather than only reported.
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("status: adopted"))
            .Add(v => v.Folder, KnowledgeFolder.Tech));

        var select = view.Find(".knowledge-record__headline .status-editor select");
        Assert.Equal(
            ["candidate", "trial", "adopted", "hold", "retired"],
            select.QuerySelectorAll("option").Select(option => option.GetAttribute("value")));

        // The authored value is the one showing, and the control wears the
        // application's status badge for the state `adopted` means — the same
        // badge the read-only form wears. A status has to look the same whether
        // or not it happens to be selectable.
        Assert.Equal("adopted", select.GetAttribute("value"));
        Assert.Equal(
            "status-editor badge badge--status badge--status-active",
            view.Find("label.status-editor").GetAttribute("class"));

        // One or the other, never both: two statuses on one line is two answers
        // to the question the headline exists to answer. Both forms are the same
        // badge now, so the thing that must not also be here is the read-only
        // one — the span.
        Assert.Empty(view.FindAll("span.badge--status"));
    }

    [Fact]
    public void Every_status_a_folder_offers_looks_the_same_selectable_or_not()
    {
        // One status, one badge. The headline draws it as a select where the
        // folder names a vocabulary and as a read-only badge everywhere else, and
        // it used to dress the two out of different scales: the select spelled
        // its own modifier from the folder's word, so `adopted`, `candidate`,
        // `trial`, `hold`, `retired`, `proposed` and `deprecated` matched no rule
        // at all and fell through to plain grey, `in-progress` slugged past
        // `.badge--status-inprogress` and missed as well, and the four that did
        // land took a colour that had nothing to do with the folder's tone. Both
        // forms ask KnowledgeStatusBadge which of the application's states the
        // word means, so a reader is never told by colour that a status they can
        // change is a different kind of thing from one they cannot.
        using var context = new BunitContext();

        foreach (var folder in Enum.GetValues<KnowledgeFolder>())
        {
            foreach (var status in KnowledgeStatus.Values(folder))
            {
                var pill = context.Render<KnowledgeStatusPill>(parameters => parameters
                    .Add(p => p.Status, status)
                    .Add(p => p.Folder, folder));

                var view = context.Render<KnowledgeMetaView>(parameters => parameters
                    .Add(v => v.Metadata, KnowledgeMeta.Parse($"status: {status}"))
                    .Add(v => v.Folder, folder));

                // Every value in the list is one the headline offers, so this is
                // the select every time — the premise of the comparison, and
                // worth failing on rather than quietly comparing two pills.
                var wrapper = view.Find("label.status-editor");
                Assert.NotNull(view.Find(".status-editor select"));

                // The structural class the stylesheet reaches the control
                // through, and then the read-only form's own string, unchanged.
                Assert.Equal(
                    $"status-editor {pill.Find("span").GetAttribute("class")}",
                    wrapper.GetAttribute("class"));

                // And it is the application's badge in both, not a scale of this
                // folder's own. Every value of every vocabulary has a tone, so a
                // modifier is always reached — a bare `badge--status` here would
                // mean a value fell through to "no opinion".
                var expected = $"badge--status-{KnowledgeStatusBadge.Slug(folder, status)}";
                Assert.Contains(expected, wrapper.ClassList);
                Assert.Contains(expected, pill.Find("span").ClassList);
                Assert.DoesNotContain("knowledge-status", view.Markup, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Without_a_vocabulary_to_offer_the_status_stays_a_static_pill()
    {
        // The select is only honest while every state it can show is a state the
        // folder allows. A folder-blind block has no list at all.
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("status: adopted")));

        Assert.Empty(view.FindAll("select"));
        Assert.Equal("adopted", view.Find(".knowledge-record__headline .badge--status").TextContent);
    }

    [Fact]
    public void A_status_the_folder_does_not_define_is_flagged_and_says_what_was_expected()
    {
        // Surfacing the typo is the whole point of knowing the vocabulary — and
        // it is why a status outside the vocabulary keeps the pill. A select
        // cannot show a value it has no option for: it would fall back to the
        // first one and quietly report `candidate` as if the file said so.
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("status: shipped"))
            .Add(v => v.Folder, KnowledgeFolder.Tech));

        var status = view.Find(".badge--status");
        Assert.Contains("knowledge-status--unrecognised", status.ClassList);

        // Flagged on `archived`, the one modifier that spends no colour. Answered
        // before any tone is consulted, so it cannot collide with the `blocked`
        // red an Attention status legitimately wears.
        Assert.Equal(
            "badge badge--status badge--status-archived knowledge-status--unrecognised",
            status.GetAttribute("class"));
        Assert.DoesNotContain("badge--status-blocked", status.ClassList);
        Assert.Contains("candidate, trial, adopted, hold, retired", status.GetAttribute("title"));
        Assert.Empty(view.FindAll("select"));
    }

    [Fact]
    public void A_status_spelled_with_a_stray_capital_keeps_the_pill()
    {
        // A stray capital is not a different status — the vocabulary check that
        // decides tone says so — but it is a different string, and a browser
        // matches a select's value against its options literally. Offered here,
        // `Adopted` would land in a select that has only `adopted` and leave it
        // showing `candidate`. The pill prints the word as it was written.
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("status: Adopted"))
            .Add(v => v.Folder, KnowledgeFolder.Tech));

        Assert.Empty(view.FindAll("select"));

        var status = view.Find(".badge--status");
        Assert.Equal("Adopted", status.TextContent);

        // And it is still not a typo: the tone is the folder's, so the badge is
        // the one `adopted` gets, and nothing is flagged.
        Assert.Contains("badge--status-active", status.ClassList);
        Assert.DoesNotContain("knowledge-status--unrecognised", status.ClassList);
    }

    [Fact]
    public void A_block_with_a_folder_but_no_status_still_offers_nothing()
    {
        // Nothing in, nothing out. A select defaulted to the first value would
        // be inventing a state the file never stated.
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("kind: format"))
            .Add(v => v.Folder, KnowledgeFolder.Tech));

        Assert.Empty(view.FindAll("select"));
        Assert.Empty(view.FindAll(".badge--status"));
        Assert.Empty(view.Find(".knowledge-record__headline").Children);
    }

    [Fact]
    public void Choosing_a_status_changes_what_is_shown_even_with_nobody_listening()
    {
        // BadgeSelect is one-way. Rendered straight off Metadata.Status it would
        // snap back on the next render, so a reader would watch the control undo
        // them — and with no host wired up, nothing would have happened at all.
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("status: adopted"))
            .Add(v => v.Folder, KnowledgeFolder.Tech));

        view.Find(".status-editor select").Change("retired");

        Assert.Equal("retired", view.Find(".status-editor select").GetAttribute("value"));

        // And the badge follows the choice: `.tech`'s `retired` is the state the
        // application spells `archived`.
        Assert.Equal(
            "status-editor badge badge--status badge--status-archived",
            view.Find("label.status-editor").GetAttribute("class"));
    }

    [Fact]
    public void Choosing_a_status_tells_a_host_that_asked_to_hear_about_it()
    {
        using var context = new BunitContext();
        var chosen = new List<string>();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("status: draft"))
            .Add(v => v.Folder, KnowledgeFolder.Backlog)
            .Add(v => v.OnStatusChanged, EventCallback.Factory.Create<string>(this, chosen.Add)));

        view.Find(".status-editor select").Change("in-progress");

        Assert.Equal("in-progress", Assert.Single(chosen));
        Assert.Equal("in-progress", view.Find(".status-editor select").GetAttribute("value"));
    }

    [Fact]
    public void A_different_block_does_not_inherit_the_previous_ones_selection()
    {
        // The choice belongs to the record it was made on. Carried over, the
        // next chapter would open claiming a state nobody gave it.
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("status: draft"))
            .Add(v => v.Folder, KnowledgeFolder.Backlog));

        view.Find(".status-editor select").Change("done");
        Assert.Equal("done", view.Find(".status-editor select").GetAttribute("value"));

        view.Render(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("status: blocked")));

        Assert.Equal("blocked", view.Find(".status-editor select").GetAttribute("value"));
    }

    [Fact]
    public void Being_handed_the_same_block_again_keeps_the_selection()
    {
        // The reseed is on the block changing, not on the parameters being set:
        // a host that re-renders for its own reasons should not keep snatching
        // the reader's choice back.
        using var context = new BunitContext();
        var block = KnowledgeMeta.Parse("status: draft");

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, block)
            .Add(v => v.Folder, KnowledgeFolder.Backlog));

        view.Find(".status-editor select").Change("ready");

        view.Render(parameters => parameters
            .Add(v => v.Metadata, block)
            .Add(v => v.AriaLabel, "Chapter metadata"));

        Assert.Equal("ready", view.Find(".status-editor select").GetAttribute("value"));
    }

    [Fact]
    public void A_reference_nobody_can_follow_is_text_and_not_a_control()
    {
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("related: [\".tech/shared.md#markdown\"]")));

        Assert.Equal("knowledge-ref knowledge-ref--inert", view.Find(".knowledge-fields__value code").GetAttribute("class"));
        Assert.Empty(view.FindAll("a"));
        Assert.Empty(view.FindAll("button"));
    }

    [Fact]
    public void A_href_turns_a_reference_into_a_link_that_still_shows_the_full_reference()
    {
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("related: [\".tech/shared.md#markdown\"]"))
            .Add(v => v.HrefFor, reference => $"/knowledge/{reference.Path}"));

        var link = view.Find("a.knowledge-ref--link");
        Assert.Equal("/knowledge/.tech/shared.md", link.GetAttribute("href"));
        Assert.Equal(".tech/shared.md#markdown", link.GetAttribute("title"));
        Assert.Empty(view.FindAll("button"));
    }

    [Fact]
    public void A_handler_turns_a_reference_into_a_button_that_reports_the_whole_reference()
    {
        using var context = new BunitContext();
        var followed = new List<KnowledgeReference>();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("""
                related: [".arc42/01-introduction.md"]
                depends-on: [".tech/shared.md#markdown"]
                """))
            .Add(v => v.OnNavigate, EventCallback.Factory.Create<KnowledgeReference>(this, followed.Add)));

        view.FindAll("button.knowledge-ref--action")[1].Click();

        var reference = Assert.Single(followed);
        Assert.Equal(".tech/shared.md#markdown", reference.Raw);
        Assert.Equal("markdown", reference.Slug);
        Assert.Equal(KnowledgeFolder.Tech, reference.Folder);
    }

    [Fact]
    public void A_link_wins_over_a_handler_so_one_reference_is_never_two_ways_to_go()
    {
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("related: [\".tech/shared.md\"]"))
            .Add(v => v.HrefFor, _ => "/knowledge/tech")
            .Add(v => v.OnNavigate, EventCallback.Factory.Create<KnowledgeReference>(this, _ => { })));

        Assert.Single(view.FindAll("a.knowledge-ref--link"));
        Assert.Empty(view.FindAll("button"));
    }

    [Fact]
    public void An_issue_url_is_followable_and_the_shorthand_is_not()
    {
        using var context = new BunitContext();

        var url = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("issue: https://github.com/JSdotNet/Backlog/issues/42")));

        Assert.Equal("https://github.com/JSdotNet/Backlog/issues/42", url.Find("a.knowledge-ref--link").GetAttribute("href"));

        var shorthand = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("issue: JSdotNet/Backlog#42")));

        Assert.Empty(shorthand.FindAll("a"));
        Assert.Equal("JSdotNet/Backlog#42", shorthand.Find("code.knowledge-value").TextContent);
    }

    [Fact]
    public void Plain_string_fields_never_become_links_even_when_a_href_is_offered()
    {
        // An alias names a class or an id field, not a chapter. Turning it into
        // a link would promise a destination that does not exist.
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("aliases: [\"OrderLine\"]"))
            .Add(v => v.HrefFor, _ => "/knowledge/anything"));

        Assert.Empty(view.FindAll("a"));
        Assert.Equal("OrderLine", view.Find("code.knowledge-value").TextContent);

        // And not dressed as one either: the pill and the link treatment are
        // both promises this value cannot keep.
        Assert.Empty(view.FindAll(".knowledge-related"));
        Assert.Empty(view.FindAll(".knowledge-ref"));
    }

    [Fact]
    public void A_field_the_schema_does_not_define_is_shown_as_itself()
    {
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("status: active\nowner: platform-team")));

        Assert.Contains("owner", view.FindAll("dt").Select(label => label.TextContent));
        Assert.Contains("platform-team", view.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_class_the_label_and_the_test_id_all_land_on_the_record()
    {
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("status: active"))
            .Add(v => v.CssClass, "entry-doc__meta")
            .Add(v => v.TestId, "entry-meta"));

        var root = view.Find(".knowledge-record");
        Assert.Equal("knowledge-record entry-doc__meta", root.GetAttribute("class"));
        Assert.Equal("Knowledge metadata", root.GetAttribute("aria-label"));
        Assert.Equal("entry-meta", root.GetAttribute("data-testid"));

        // A bare <div> with a label is a label nothing announces, so the wrapper
        // says what it is.
        Assert.Equal("group", root.GetAttribute("role"));
    }

    [Fact]
    public void The_fields_stay_a_description_list_so_a_field_stays_paired_with_its_value()
    {
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("kind: format")));

        var fields = view.Find("dl");
        Assert.Equal("knowledge-fields", fields.GetAttribute("class"));

        // And the label is stated once, on the record. It used to be repeated
        // here, which announced the same block twice under the same name.
        Assert.Null(fields.GetAttribute("aria-label"));
    }

    [Fact]
    public void The_status_shares_the_headline_and_is_no_longer_a_row_in_the_list()
    {
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("""
                status: adopted
                kind: format
                """)));

        var headline = view.Find(".knowledge-record__headline");
        Assert.Equal("adopted", headline.QuerySelector(".badge--status")?.TextContent);

        // Once, and without a label in front of it. "status adopted" reads as a
        // field; "Shared Technologies adopted" reads as a sentence.
        Assert.Single(view.FindAll(".badge--status"));
        Assert.DoesNotContain("status", view.FindAll("dt").Select(label => label.TextContent));
    }

    [Fact]
    public void A_record_that_states_only_a_status_draws_no_list_at_all()
    {
        // An empty <dl> is a gap with a gap's spacing around it.
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("status: active")));

        Assert.Empty(view.FindAll("dl"));
        Assert.NotNull(view.Find(".knowledge-record__headline .badge--status"));
    }

    [Fact]
    public void A_heading_lands_on_the_same_line_as_the_status_and_before_it()
    {
        // Sharing a line is only real if the two share a parent — a margin trick
        // comes apart the moment the heading wraps.
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("status: adopted"))
            .Add(v => v.Folder, KnowledgeFolder.Tech)
            .Add(v => v.Heading, (RenderFragment)(builder =>
            {
                builder.OpenElement(0, "p");
                builder.AddAttribute(1, "class", "md-heading md-heading--1");
                builder.AddContent(2, "Shared Technologies");
                builder.CloseElement();
            })));

        // Two children and only two, in that order — the heading first, the
        // status second and therefore the one an auto margin can push right.
        var headline = view.Find(".knowledge-record__headline");
        Assert.Equal(
            ["p", "label"],
            headline.Children.Select(child => child.LocalName));
        Assert.Equal("Shared Technologies", headline.Children[0].TextContent);
        Assert.Contains("status-editor", headline.Children[1].ClassList);
    }

    [Fact]
    public void A_heading_lands_before_the_static_pill_too()
    {
        // Same order in the folder-blind case, because the same stylesheet rule
        // right-aligns both forms of the status.
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("status: adopted"))
            .Add(v => v.Heading, (RenderFragment)(builder =>
            {
                builder.OpenElement(0, "p");
                builder.AddAttribute(1, "class", "md-heading md-heading--1");
                builder.AddContent(2, "Shared Technologies");
                builder.CloseElement();
            })));

        var headline = view.Find(".knowledge-record__headline");
        Assert.Equal(["p", "span"], headline.Children.Select(child => child.LocalName));
        Assert.Contains("badge--status", headline.Children[1].ClassList);
    }

    [Fact]
    public void Without_a_heading_the_headline_holds_the_pill_alone()
    {
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("status: active")));

        var headline = view.Find(".knowledge-record__headline");
        Assert.Single(headline.Children);
        Assert.Equal("active", headline.Children[0].TextContent);
    }

    [Fact]
    public void An_effort_draws_a_points_badge_and_says_what_the_number_counts()
    {
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("""
                status: draft
                effort: 5
                """)));

        var badge = view.Find("[data-testid=\"knowledge-effort-badge\"]");
        Assert.Contains("badge", badge.ClassList);
        Assert.Equal("5 pts", badge.TextContent.Trim());

        // An effort row is labelled like every other secondary field.
        Assert.Contains("effort", view.FindAll("dt").Select(label => label.TextContent));
    }

    [Fact]
    public void An_effort_of_zero_still_draws_its_badge()
    {
        // Zero is an estimate, not the absence of one, so it shows.
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("""
                status: draft
                effort: 0
                """)));

        Assert.Equal("0 pts", view.Find("[data-testid=\"knowledge-effort-badge\"]").TextContent.Trim());
    }

    [Fact]
    public void No_effort_draws_no_badge()
    {
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("status: draft\nkind: framework")));

        Assert.Empty(view.FindAll("[data-testid=\"knowledge-effort-badge\"]"));
        Assert.DoesNotContain("effort", view.FindAll("dt").Select(label => label.TextContent));
    }

    [Fact]
    public void Roadmap_tags_draw_one_chip_each_inside_the_labelled_container()
    {
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("""
                status: draft
                roadmap: [sync-service, mobile-mvp]
                """)));

        var container = view.Find("[data-testid=\"knowledge-roadmap-tags\"]");
        var chips = container.QuerySelectorAll(".tag-chip");
        Assert.Equal(
            ["sync-service", "mobile-mvp"],
            chips.Select(chip => chip.TextContent.Trim()));

        // A tag names a roadmap item, it does not address a chapter, so it is
        // never dressed as a link.
        Assert.Empty(container.QuerySelectorAll("a"));
        Assert.Contains("roadmap", view.FindAll("dt").Select(label => label.TextContent));
    }

    [Fact]
    public void An_empty_roadmap_draws_no_chips_and_no_container()
    {
        using var context = new BunitContext();

        var view = context.Render<KnowledgeMetaView>(parameters => parameters
            .Add(v => v.Metadata, KnowledgeMeta.Parse("status: draft\nkind: framework")));

        Assert.Empty(view.FindAll("[data-testid=\"knowledge-roadmap-tags\"]"));
        Assert.DoesNotContain("roadmap", view.FindAll("dt").Select(label => label.TextContent));
    }
}
