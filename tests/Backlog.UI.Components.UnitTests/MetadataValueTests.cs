namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The smallest metadata components: one per value shape, each asked about on its
/// own.
///
/// <para><c>MetadataViewTests</c> asks the same questions through a whole
/// record, which is how a reader meets these and is the wrong altitude for
/// reviewing one. A shape that only ever renders inside the record can only be
/// changed by changing the record, and the empty case — the one every one of these
/// has and the one a record hides by not drawing the row — is invisible from up
/// there.</para>
///
/// <para>Three questions each: what the shape draws, which class hooks it hands
/// over, and what it does with nothing.</para>
/// </summary>
public sealed class MetadataValueTests
{
    [Fact]
    public void A_scalar_is_a_quiet_code_span_and_never_a_link()
    {
        using var context = new BunitContext();

        var scalar = context.Render<MetadataScalar>(parameters => parameters
            .Add(value => value.Value, "framework"));

        Assert.Equal("framework", scalar.Find("code.knowledge-value").TextContent);
        Assert.Empty(scalar.FindAll("a"));
    }

    [Fact]
    public void A_scalar_prints_what_was_authored_rather_than_a_tidied_version_of_it()
    {
        // 10.0 is a version string and not the number ten, and a stray capital in a
        // kind is the author's. Normalising either would hide the typo rather than
        // its cause.
        using var context = new BunitContext();

        var version = context.Render<MetadataScalar>(parameters => parameters
            .Add(value => value.Value, "10.0"));

        var kind = context.Render<MetadataScalar>(parameters => parameters
            .Add(value => value.Value, "Framework"));

        Assert.Equal("10.0", version.Find("code").TextContent);
        Assert.Equal("Framework", kind.Find("code").TextContent);
    }

    [Fact]
    public void A_scalar_hands_over_its_class_and_takes_a_test_id()
    {
        using var context = new BunitContext();

        var dressed = context.Render<MetadataScalar>(parameters => parameters
            .Add(value => value.Value, "framework")
            .Add(value => value.ValueCssClass, "spec__value")
            .Add(value => value.TestId, "spec-kind"));

        Assert.Equal("spec__value", dressed.Find("code").GetAttribute("class"));
        Assert.Equal("spec-kind", dressed.Find("code").GetAttribute("data-testid"));

        var bare = context.Render<MetadataScalar>(parameters => parameters
            .Add(value => value.Value, "framework")
            .Add(value => value.ValueCssClass, null));

        // Null drops the attribute rather than emitting an empty one, for a host
        // laying values out under names of its own.
        Assert.Null(bare.Find("code").GetAttribute("class"));
    }

    [Fact]
    public void A_prefix_is_drawn_inside_the_value_and_a_title_names_the_field()
    {
        // `v10.0` is a version anywhere it appears, which is what lets the row it
        // sits in drop its visible label. The sigil is inside the element on
        // purpose: it travels with the string a reader copies.
        using var context = new BunitContext();

        var version = context.Render<MetadataScalar>(parameters => parameters
            .Add(value => value.Value, "10.0")
            .Add(value => value.Prefix, "v")
            .Add(value => value.Title, "version: 10.0"));

        Assert.Equal("v10.0", version.Find("code.knowledge-value").TextContent);
        Assert.Equal("version: 10.0", version.Find("code").GetAttribute("title"));
    }

    [Fact]
    public void A_prefix_alone_is_not_a_value_and_draws_nothing()
    {
        // Otherwise a field the file never stated would render a lone `v`, which
        // asserts a version rather than saying nothing.
        using var context = new BunitContext();

        var scalar = context.Render<MetadataScalar>(parameters => parameters
            .Add(value => value.Prefix, "v"));

        Assert.Equal(string.Empty, scalar.Markup.Trim());
    }

    [Fact]
    public void A_scalar_with_no_title_emits_no_title_attribute()
    {
        // Every value that keeps its visible label has nothing to put there, and an
        // empty title is a tooltip that flashes and says nothing.
        using var context = new BunitContext();

        var scalar = context.Render<MetadataScalar>(parameters => parameters
            .Add(value => value.Value, "framework"));

        Assert.Null(scalar.Find("code").GetAttribute("title"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_scalar_draws_nothing_because_blank_is_how_a_file_says_nothing(string? value)
    {
        using var context = new BunitContext();

        var scalar = context.Render<MetadataScalar>(parameters => parameters
            .Add(field => field.Value, value));

        Assert.Equal(string.Empty, scalar.Markup.Trim());
    }

    [Fact]
    public void A_value_list_draws_one_scalar_per_entry_with_no_separator_between_them()
    {
        // The gap is the stylesheet's. A separator here would travel with a value
        // that a reader copied off the screen.
        using var context = new BunitContext();

        var list = context.Render<MetadataValueList>(parameters => parameters
            .Add(values => values.Values, ["Azure Functions", "Controller-based ASP.NET Core"]));

        Assert.Equal(
            ["Azure Functions", "Controller-based ASP.NET Core"],
            list.FindAll("code.knowledge-value").Select(value => value.TextContent));

        Assert.Equal(2, list.FindComponents<MetadataScalar>().Count);
        Assert.DoesNotContain(",", list.Markup);
    }

    [Fact]
    public void A_value_list_offers_no_way_to_make_an_entry_followable()
    {
        // The type is the guarantee: a technology that was weighed and rejected was
        // never written up, so there is nowhere for it to go, and this component
        // takes no href and no handler for a host to change its mind with. An alias
        // is the field that moved out from under this rule — see the alias list
        // below, where the term being named *is* modelled somewhere.
        using var context = new BunitContext();

        var list = context.Render<MetadataValueList>(parameters => parameters
            .Add(values => values.Values, ["Azure Functions"])
            .Add(values => values.ValueCssClass, "spec__value"));

        Assert.Empty(list.FindAll("a"));
        Assert.Empty(list.FindAll("button"));
        Assert.Equal("spec__value", list.Find("code").GetAttribute("class"));
    }

    [Fact]
    public void An_empty_value_list_draws_nothing()
    {
        using var context = new BunitContext();

        var list = context.Render<MetadataValueList>();

        Assert.Equal(string.Empty, list.Markup.Trim());
    }

    [Fact]
    public void A_reference_list_offers_every_entry_the_same_way()
    {
        // One entry a link beside one inert would read as a claim about the two
        // targets rather than about what the host can resolve.
        using var context = new BunitContext();

        var list = context.Render<MetadataReferenceList>(parameters => parameters
            .Add(references => references.References,
            [
                KnowledgeReference.Parse(".tech/shared.md#markdown")!,
                KnowledgeReference.Parse(".arc42/02-constraints.md")!
            ])
            .Add(references => references.HrefFor, reference => "/knowledge/" + reference.Path));

        var links = list.FindAll("a.knowledge-ref--link");
        Assert.Equal(2, links.Count);
        Assert.Equal("/knowledge/.tech/shared.md", links[0].GetAttribute("href"));
        Assert.Equal(".tech/shared.md#markdown", links[0].TextContent);
    }

    [Fact]
    public void A_reference_list_leaves_the_unresolved_entry_inert_and_the_rest_alone()
    {
        // HrefFor is asked per reference because only the host knows which of them
        // it can route to. The one it cannot stays text rather than becoming a link
        // to nowhere.
        using var context = new BunitContext();

        var list = context.Render<MetadataReferenceList>(parameters => parameters
            .Add(references => references.References,
            [
                KnowledgeReference.Parse(".tech/shared.md")!,
                KnowledgeReference.Parse(".domain/context-map.md")!
            ])
            .Add(references => references.HrefFor,
                reference => reference.Path.StartsWith(".tech") ? "/tech" : null));

        Assert.Single(list.FindAll("a.knowledge-ref--link"));
        Assert.Single(list.FindAll("code.knowledge-ref--inert"));
    }

    [Fact]
    public void An_empty_reference_list_draws_nothing()
    {
        using var context = new BunitContext();

        var list = context.Render<MetadataReferenceList>();

        Assert.Equal(string.Empty, list.Markup.Trim());
    }

    [Fact]
    public void An_effort_badge_says_what_the_number_counts()
    {
        // "5" on its own does not say what it counts, and the one thing a reader
        // must not do with a story-point count is read it as days.
        using var context = new BunitContext();

        var badge = context.Render<MetadataEffortBadge>(parameters => parameters
            .Add(effort => effort.Effort, 5));

        Assert.Equal("5 pts", badge.Find(".badge--effort").TextContent);
        Assert.Equal("knowledge-effort-badge", badge.Find(".badge").GetAttribute("data-testid"));

        // The row this sits in hides its label, so the field name has to reach a
        // pointer user from the badge itself.
        Assert.Equal("effort: 5 story points", badge.Find(".badge").GetAttribute("title"));
    }

    [Fact]
    public void An_effort_of_zero_is_an_estimate_and_still_draws()
    {
        // Nobody has sized this, against sized and found to be nothing. Collapsing
        // the two would lose the sizing.
        using var context = new BunitContext();

        var zero = context.Render<MetadataEffortBadge>(parameters => parameters
            .Add(effort => effort.Effort, 0));

        var absent = context.Render<MetadataEffortBadge>();

        Assert.Equal("0 pts", zero.Find(".badge--effort").TextContent);
        Assert.Equal(string.Empty, absent.Markup.Trim());
    }

    [Fact]
    public void An_effort_badge_takes_a_modifier_and_can_be_renamed_for_a_test()
    {
        using var context = new BunitContext();

        var badge = context.Render<MetadataEffortBadge>(parameters => parameters
            .Add(effort => effort.Effort, 3)
            .Add(effort => effort.CssClass, "spec__estimate")
            .Add(effort => effort.TestId, "spec-effort"));

        Assert.Contains("spec__estimate", badge.Find(".badge").GetAttribute("class"));
        Assert.Equal("spec-effort", badge.Find(".badge").GetAttribute("data-testid"));
    }

    [Fact]
    public void An_issue_url_is_followable_and_reads_as_the_number_and_its_repository()
    {
        // The GitHub reference the product already draws everywhere else: the mark,
        // the number, the repository beside it. Not the whole URL — the address is
        // on the title, where a reader can still check it.
        using var context = new BunitContext();

        var issue = context.Render<MetadataIssueLink>(parameters => parameters
            .Add(link => link.Issue, "https://github.com/JSdotNet/Backlog/issues/118"));

        var link = issue.Find("a.integration-link--link");
        Assert.Equal("https://github.com/JSdotNet/Backlog/issues/118", link.GetAttribute("href"));
        Assert.Equal("https://github.com/JSdotNet/Backlog/issues/118", link.GetAttribute("title"));
        Assert.Equal("#118", issue.Find(".integration-link__label").TextContent);
        Assert.Equal("JSdotNet/Backlog", issue.Find(".integration-link__repository").TextContent);
    }

    [Fact]
    public void An_issue_is_drawn_through_the_integrations_family_and_not_by_hand()
    {
        // The point of the field's rewrite: MetadataIssueLink maps the authored
        // string onto a reference record and draws none of it. A second rendering of
        // the same shape is what three application screens already are.
        using var context = new BunitContext();

        var issue = context.Render<MetadataIssueLink>(parameters => parameters
            .Add(link => link.Issue, "JSdotNet/Backlog#118"));

        var reference = Assert.Single(issue.FindComponents<IntegrationLink>());
        Assert.Equal(IntegrationProvider.GitHub, reference.Instance.Link.Provider);
        Assert.Equal(IntegrationLinkKind.Issue, reference.Instance.Link.Kind);

        // A fence says which issue, never whether it is open. Unknown means "we
        // looked and could not tell", and nobody looked — so no state, and no chip.
        Assert.Null(reference.Instance.Link.ArtifactState);
        Assert.Null(reference.Instance.Link.SessionState);
        Assert.Empty(issue.FindAll(".badge--integration"));
    }

    [Fact]
    public void The_owner_repo_shorthand_is_shown_and_stays_unfollowable()
    {
        // Resolving it needs a remote this library knows nothing about, and guessing
        // one would produce a link to somewhere the issue is not. So it gets the
        // inert span the tri-state already has for exactly that case.
        using var context = new BunitContext();

        var issue = context.Render<MetadataIssueLink>(parameters => parameters
            .Add(link => link.Issue, "JSdotNet/Backlog#118"));

        Assert.Equal("#118", issue.Find("span.integration-link--inert .integration-link__label").TextContent);
        Assert.Equal("JSdotNet/Backlog", issue.Find(".integration-link__repository").TextContent);

        // Nothing to follow, and no tab stop spent refusing to.
        Assert.Empty(issue.FindAll("a"));
        Assert.Empty(issue.FindAll("button"));

        // The authored string is what a reader checks the shortened label against.
        Assert.Equal("JSdotNet/Backlog#118", issue.Find("span.integration-link--inert").GetAttribute("title"));
    }

    [Theory]
    [InlineData("file:///c:/secrets.txt")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/118")]
    public void An_issue_that_is_not_http_is_shown_and_not_offered_as_a_click(string issue)
    {
        // Any other scheme in a knowledge file is either a mistake or an attack, and
        // neither is something to hand a reader a click on.
        using var context = new BunitContext();

        var rendered = context.Render<MetadataIssueLink>(parameters => parameters
            .Add(link => link.Issue, issue));

        Assert.Empty(rendered.FindAll("a"));
        Assert.Equal(issue, rendered.Find(".integration-link__label").TextContent);
    }

    [Fact]
    public void An_issue_neither_form_parses_is_drawn_whole_rather_than_relabelled()
    {
        // A malformed issue is a typo, and a typo is only useful once it is visible.
        // Inventing a number for it would hide the fact that there is not one.
        using var context = new BunitContext();

        var issue = context.Render<MetadataIssueLink>(parameters => parameters
            .Add(link => link.Issue, "backlog-118"));

        Assert.Equal("backlog-118", issue.Find(".integration-link__label").TextContent);
        Assert.Empty(issue.FindAll(".integration-link__repository"));
    }

    [Fact]
    public void A_url_that_is_not_an_issue_address_keeps_its_whole_self_and_stays_followable()
    {
        // The shape is checked, not the host: an enterprise GitHub is the same shape
        // at a different address. What must not happen is a URL with no issue number
        // in it being relabelled as one it does not have.
        using var context = new BunitContext();

        var issue = context.Render<MetadataIssueLink>(parameters => parameters
            .Add(link => link.Issue, "https://example.com/tracker/118"));

        Assert.Equal("https://example.com/tracker/118", issue.Find("a").GetAttribute("href"));
        Assert.Equal("https://example.com/tracker/118", issue.Find(".integration-link__label").TextContent);
        Assert.Empty(issue.FindAll(".integration-link__repository"));
    }

    [Fact]
    public void An_absent_issue_draws_nothing_and_a_test_id_reaches_both_forms()
    {
        using var context = new BunitContext();

        var absent = context.Render<MetadataIssueLink>();
        Assert.Equal(string.Empty, absent.Markup.Trim());

        var url = context.Render<MetadataIssueLink>(parameters => parameters
            .Add(link => link.Issue, "https://github.com/JSdotNet/Backlog/issues/1")
            .Add(link => link.TestId, "spec-issue"));

        var shorthand = context.Render<MetadataIssueLink>(parameters => parameters
            .Add(link => link.Issue, "JSdotNet/Backlog#118")
            .Add(link => link.TestId, "spec-issue"));

        // The same id whichever shape drew it: a test looking for the issue should
        // not have to know which form the file used.
        Assert.Equal("spec-issue", url.Find("a").GetAttribute("data-testid"));
        Assert.Equal("spec-issue", shorthand.Find("span.integration-link--inert").GetAttribute("data-testid"));
    }

    [Fact]
    public void A_kind_is_a_classification_chip_and_never_a_per_value_class()
    {
        // A closed vocabulary is the fact worth drawing, and it is what lets the row
        // lose its visible label. A Slug would put an authored word into a class
        // name, which is how `.badge--kind-framework` becomes a rule nobody wrote.
        using var context = new BunitContext();

        var kind = context.Render<MetadataKindBadge>(parameters => parameters
            .Add(badge => badge.Kind, "framework"));

        var chip = kind.Find("span.badge");
        Assert.Equal("badge badge--kind", chip.GetAttribute("class"));
        Assert.Equal("framework", chip.TextContent);

        // The row hides its label, so the field name has to reach a pointer user.
        Assert.Equal("kind: framework", chip.GetAttribute("title"));
    }

    [Fact]
    public void A_kind_prints_what_was_authored_and_a_blank_one_draws_nothing()
    {
        // The vocabulary is the folder's and is not checked here: a word the folder
        // does not define is a typo worth seeing, not one worth normalising away.
        using var context = new BunitContext();

        var odd = context.Render<MetadataKindBadge>(parameters => parameters
            .Add(badge => badge.Kind, "Framework"));

        Assert.Equal("Framework", odd.Find(".badge").TextContent);
        Assert.Equal(string.Empty, context.Render<MetadataKindBadge>().Markup.Trim());
    }

    [Fact]
    public void An_alias_is_a_badge_and_stays_a_span_until_a_host_can_place_it()
    {
        // An alias with no answer stays unfollowable rather than becoming a link to
        // a page nobody wrote.
        using var context = new BunitContext();

        var aliases = context.Render<MetadataAliasList>(parameters => parameters
            .Add(list => list.Aliases, ["TaskItem", "backlog_entry_id"]));

        Assert.Equal(
            ["TaskItem", "backlog_entry_id"],
            aliases.FindAll("span.badge--alias").Select(badge => badge.TextContent));

        Assert.Empty(aliases.FindAll("a"));
        Assert.Empty(aliases.FindAll("button"));

        // The field is named on each badge, because an alias is drawn as a qualifier
        // and a reader may meet one away from its row.
        Assert.Equal("alias: TaskItem", aliases.Find(".badge--alias").GetAttribute("title"));
    }

    [Fact]
    public void An_alias_resolver_is_asked_per_alias_and_answering_for_one_leaves_the_rest_alone()
    {
        // The claim is about the individual word: one alias may be the canonical
        // term's own class name and the next a legacy database column. A host that
        // can place one and not the other tells the truth by saying so.
        using var context = new BunitContext();

        var aliases = context.Render<MetadataAliasList>(parameters => parameters
            .Add(list => list.Aliases, ["TaskItem", "backlog_entry_id"])
            .Add(list => list.HrefFor, alias => alias is "TaskItem" ? "/naming#backlog-entry" : null));

        var link = Assert.Single(aliases.FindAll("a.badge--alias"));
        Assert.Equal("/naming#backlog-entry", link.GetAttribute("href"));
        Assert.Equal("TaskItem", link.TextContent);

        var inert = Assert.Single(aliases.FindAll("span.badge--alias"));
        Assert.Equal("backlog_entry_id", inert.TextContent);
    }

    [Fact]
    public void An_alias_is_a_button_for_a_host_that_routes_in_process()
    {
        // The third leg of the tri-state, and the one that reports which alias: a
        // surface with no address to hand over still has to be able to act.
        using var context = new BunitContext();

        var selected = new List<string>();

        var aliases = context.Render<MetadataAliasList>(parameters => parameters
            .Add(list => list.Aliases, ["TaskItem", "backlog_entry_id"])
            .Add(list => list.OnSelect, EventCallback.Factory.Create<string>(this, selected.Add)));

        var buttons = aliases.FindAll("button.badge--alias");
        Assert.Equal(2, buttons.Count);

        buttons[1].Click();

        Assert.Equal(["backlog_entry_id"], selected);
    }

    [Fact]
    public void An_address_wins_over_a_handler_so_one_alias_is_never_two_ways_to_go()
    {
        using var context = new BunitContext();

        var aliases = context.Render<MetadataAliasList>(parameters => parameters
            .Add(list => list.Aliases, ["TaskItem"])
            .Add(list => list.HrefFor, _ => "/naming")
            .Add(list => list.OnSelect, EventCallback.Factory.Create<string>(this, _ => { })));

        Assert.Single(aliases.FindAll("a.badge--alias"));
        Assert.Empty(aliases.FindAll("button"));
    }

    [Fact]
    public void An_alias_list_takes_a_modifier_on_every_badge_and_draws_nothing_when_empty()
    {
        using var context = new BunitContext();

        var aliases = context.Render<MetadataAliasList>(parameters => parameters
            .Add(list => list.Aliases, ["TaskItem"])
            .Add(list => list.AliasCssClass, "spec__alias"));

        Assert.Equal("badge badge--alias spec__alias", aliases.Find(".badge").GetAttribute("class"));
        Assert.Equal(string.Empty, context.Render<MetadataAliasList>().Markup.Trim());
    }

    [Fact]
    public void A_key_list_draws_a_feature_badge_per_key_and_never_a_link()
    {
        // A flag key is an identifier something outside these files answers to, and
        // the feature-badge family is the product's existing language for "an
        // application feature is involved here".
        using var context = new BunitContext();

        var keys = context.Render<MetadataKeyList>(parameters => parameters
            .Add(list => list.Keys, ["inbox-pane", "inbox-filters"]));

        Assert.Equal(
            ["inbox-pane", "inbox-filters"],
            keys.FindAll("span.badge--feature").Select(badge => badge.TextContent));

        // No slug, and that is the whole of it: a per-value modifier would emit
        // `.badge--feature-inbox-pane`, a class no stylesheet defines.
        Assert.All(
            keys.FindAll(".badge"),
            badge => Assert.Equal("badge badge--feature", badge.GetAttribute("class")));

        Assert.Empty(keys.FindAll("a"));

        // Nor a control: filtering by a key needs a list to filter, and a badge that
        // took focus to do nothing would be worse than a label.
        Assert.Empty(keys.FindAll("button"));
    }

    [Fact]
    public void A_key_list_takes_a_modifier_on_every_badge_and_draws_nothing_when_empty()
    {
        using var context = new BunitContext();

        var keys = context.Render<MetadataKeyList>(parameters => parameters
            .Add(list => list.Keys, ["sync-service"])
            .Add(list => list.KeyCssClass, "spec__flag"));

        Assert.Equal("badge badge--feature spec__flag", keys.Find(".badge").GetAttribute("class"));
        Assert.Equal(string.Empty, context.Render<MetadataKeyList>().Markup.Trim());
    }

    [Fact]
    public void An_unrecognised_key_is_drawn_as_a_row_under_its_own_name()
    {
        // A reader that dropped it would make a genuine schema addition invisible:
        // the field in the file, absent from the view, and nobody able to say which
        // of the two was wrong.
        using var context = new BunitContext();

        var extra = context.Render<MetadataExtraFields>(parameters => parameters
            .Add(fields => fields.Extra, new Dictionary<string, IReadOnlyList<string>>
            {
                ["owner"] = ["platform-team"],
                ["related"] = ["not an address"]
            }));

        Assert.Equal(
            ["owner", "related"],
            extra.FindAll("div.knowledge-fields__row > dt").Select(label => label.TextContent));

        Assert.Equal(2, extra.FindComponents<MetadataField>().Count);

        // Nothing here knows what an unrecognised key means, so nothing here may
        // promise a destination for it.
        Assert.Empty(extra.FindAll("a"));
        Assert.Equal(
            ["platform-team", "not an address"],
            extra.FindAll("code.knowledge-value").Select(value => value.TextContent));
    }

    [Fact]
    public void No_unrecognised_keys_draws_no_rows()
    {
        using var context = new BunitContext();

        var extra = context.Render<MetadataExtraFields>();

        Assert.Equal(string.Empty, extra.Markup.Trim());
    }
}
