namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The metadata strip grew a typed, folder-aware, navigable second form. This
/// class exists to prove the first one did not move.
///
/// <para>A failure here is a <strong>breaking change</strong>, not a styling
/// nit: the desktop app and the storybook both render this component with two
/// parameters and nothing else, and they are entitled to the markup they had
/// when they were written. If one of these fails, the new branch has leaked into
/// the old one — fix the branch, do not update the expectation.</para>
/// </summary>
public sealed class MetadataBadgeCompatibilityTests
{
    [Fact]
    public void The_two_parameter_badge_renders_the_legacy_markup_exactly_breaking_change_if_this_fails()
    {
        using var context = new BunitContext();

        var badge = context.Render<MetadataBadge>(parameters => parameters
            .Add(b => b.Status, "ready")
            .Add(b => b.Related, ["a", "b"]));

        var root = badge.Find("div");
        Assert.Equal("knowledge-meta", root.GetAttribute("class"));
        Assert.Equal("Metadata", root.GetAttribute("aria-label"));

        var status = badge.Find("span");
        Assert.Equal("knowledge-status knowledge-status--ready", status.GetAttribute("class"));
        Assert.Equal("ready", status.TextContent);

        var related = badge.FindAll("code");
        Assert.Equal(2, related.Count);
        Assert.All(related, chip => Assert.Equal("knowledge-related", chip.GetAttribute("class")));
        Assert.Equal(["a", "b"], related.Select(chip => chip.TextContent));

        // The old strip is a flat row of chips. No description list, no link, no
        // control, and nothing a stylesheet could catch by tone.
        Assert.Empty(badge.FindAll("a"));
        Assert.Empty(badge.FindAll("button"));
        Assert.Empty(badge.FindAll("dl"));
        Assert.DoesNotContain("tone-", badge.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("unrecognised", badge.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_legacy_strip_has_no_select_in_it_breaking_change_if_this_fails()
    {
        // The typed record's headline draws a BadgeSelect where the folder has a
        // vocabulary to offer. None of that can reach here: a two-parameter
        // caller hands over a loose string and has no way to hear about a change,
        // so a control would be an affordance leading nowhere — on top of
        // replacing the markup these callers were written against.
        using var context = new BunitContext();

        foreach (var status in new[] { "ready", "draft", "in-progress", "adopted", "active" })
        {
            var badge = context.Render<MetadataBadge>(parameters => parameters
                .Add(b => b.Status, status)
                .Add(b => b.Related, ["a"]));

            Assert.Empty(badge.FindAll("select"));
            Assert.Empty(badge.FindAll("label"));
            Assert.DoesNotContain("status-editor", badge.Markup, StringComparison.Ordinal);
            Assert.Equal($"knowledge-status knowledge-status--{status}", badge.Find("span").GetAttribute("class"));
        }
    }

    [Fact]
    public void A_status_with_no_relations_renders_the_pill_alone_breaking_change_if_this_fails()
    {
        using var context = new BunitContext();

        var badge = context.Render<MetadataBadge>(parameters => parameters
            .Add(b => b.Status, "draft"));

        Assert.Equal("knowledge-status knowledge-status--draft", badge.Find("span").GetAttribute("class"));
        Assert.Empty(badge.FindAll("code"));
    }

    [Fact]
    public void An_empty_badge_still_renders_nothing_at_all_breaking_change_if_this_fails()
    {
        // The strip must not leave a gap where a document has no metadata.
        using var context = new BunitContext();

        var badge = context.Render<MetadataBadge>();

        Assert.Equal(string.Empty, badge.Markup.Trim());
    }

    [Fact]
    public void The_class_label_and_test_id_hooks_still_land_where_they_did_breaking_change_if_this_fails()
    {
        using var context = new BunitContext();

        var badge = context.Render<MetadataBadge>(parameters => parameters
            .Add(b => b.Status, "active")
            .Add(b => b.CssClass, "entry-doc__meta")
            .Add(b => b.AriaLabel, "Entry metadata")
            .Add(b => b.TestId, "entry-meta")
            .AddUnmatched("data-role", "meta"));

        var root = badge.Find("div");
        Assert.Equal("knowledge-meta entry-doc__meta", root.GetAttribute("class"));
        Assert.Equal("Entry metadata", root.GetAttribute("aria-label"));
        Assert.Equal("entry-meta", root.GetAttribute("data-testid"));
        Assert.Equal("meta", root.GetAttribute("data-role"));
    }

    [Fact]
    public void Only_asking_for_something_new_switches_the_badge_to_the_typed_form()
    {
        // The old parameters keep working through the new branch too — a caller
        // that adds a folder should not also have to hand over a parsed block.
        //
        // What the folder buys used to be a tone class on the pill and is now the
        // vocabulary itself: with a folder named, the loose status is read as one
        // of that folder's values and the headline offers the rest. Either way
        // this asserts the same thing — the folder reached the status.
        using var context = new BunitContext();

        var badge = context.Render<MetadataBadge>(parameters => parameters
            .Add(b => b.Status, "ready")
            .Add(b => b.Related, ["a"])
            .Add(b => b.Folder, KnowledgeFolder.Backlog));

        Assert.Equal("knowledge-meta", badge.Find("div").GetAttribute("class"));
        Assert.NotNull(badge.Find("dl.knowledge-fields"));

        var select = badge.Find(".knowledge-record__headline .status-editor select");
        Assert.Equal("ready", select.GetAttribute("value"));
        Assert.Equal(
            ["draft", "ready", "in-progress", "done", "blocked"],
            select.QuerySelectorAll("option").Select(option => option.GetAttribute("value")));

        Assert.Equal("a", badge.Find("dd.knowledge-fields__value code").TextContent);
    }

    [Fact]
    public void A_status_change_reaches_the_caller_that_asked_to_hear_about_it()
    {
        // The badge wraps a record and the record raises this, but the badge was
        // not passing it on — so a typed caller could watch the headline's select
        // change and never hear about it, and the choice lived on screen and
        // nowhere else. The record keeps the value showing either way, which is
        // exactly what made the gap invisible.
        using var context = new BunitContext();
        var chosen = new List<string>();

        var badge = context.Render<MetadataBadge>(parameters => parameters
            .Add(b => b.Metadata, MetadataReader.Parse("status: draft"))
            .Add(b => b.Folder, KnowledgeFolder.Backlog)
            .Add(b => b.OnStatusChanged, EventCallback.Factory.Create<string>(this, chosen.Add)));

        badge.Find(".status-editor select").Change("in-progress");

        Assert.Equal("in-progress", Assert.Single(chosen));
        Assert.Equal("in-progress", badge.Find(".status-editor select").GetAttribute("value"));
    }

    [Fact]
    public void Wanting_to_hear_about_a_status_change_is_itself_asking_for_the_typed_form()
    {
        // Consistent with the rule the other three follow: asking for a block, a
        // folder, a link resolver or a callback is asking for the record. A
        // caller that wires this up and gets the legacy strip would be handed
        // markup with nothing in it that could ever raise the callback.
        using var context = new BunitContext();

        var badge = context.Render<MetadataBadge>(parameters => parameters
            .Add(b => b.Status, "ready")
            .Add(b => b.OnStatusChanged, EventCallback.Factory.Create<string>(this, _ => { })));

        Assert.NotNull(badge.Find(".knowledge-record"));
    }

    [Fact]
    public void A_parsed_block_supersedes_the_loose_status_and_relations()
    {
        using var context = new BunitContext();

        var badge = context.Render<MetadataBadge>(parameters => parameters
            .Add(b => b.Status, "ready")
            .Add(b => b.Related, ["ignored"])
            .Add(b => b.Metadata, MetadataReader.Parse("status: adopted\nkind: format")));

        Assert.Equal("adopted", badge.Find(".badge--status").TextContent);
        Assert.DoesNotContain("ignored", badge.Markup, StringComparison.Ordinal);
        Assert.Contains("format", badge.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_typed_badge_states_its_label_once_and_the_legacy_one_still_states_it_on_the_wrapper()
    {
        // The split got subtler here. The legacy strip is one flat div and its
        // label belongs on it, unchanged. The typed form is a wrapper around a
        // record, and setting the label on both announced the same block twice
        // under the same name — so the record, which is the thing being
        // labelled, is the only one that carries it.
        using var context = new BunitContext();

        var legacy = context.Render<MetadataBadge>(parameters => parameters
            .Add(b => b.Status, "ready")
            .Add(b => b.AriaLabel, "Entry metadata"));

        Assert.Equal("Entry metadata", legacy.Find("div.knowledge-meta").GetAttribute("aria-label"));

        var typed = context.Render<MetadataBadge>(parameters => parameters
            .Add(b => b.Status, "ready")
            .Add(b => b.Related, ["a"])
            .Add(b => b.Folder, KnowledgeFolder.Backlog)
            .Add(b => b.AriaLabel, "Entry metadata"));

        // Exactly one element claims to be this block. The headline's status
        // select carries a label of its own ("Change status"), which names a
        // control rather than the block — so the count is of things called
        // "Entry metadata", not of aria-labels.
        var labelled = typed.FindAll("[aria-label='Entry metadata']");
        Assert.Contains("knowledge-record", Assert.Single(labelled).ClassList);
    }

    [Fact]
    public void A_typed_badge_with_nothing_in_it_still_renders_nothing()
    {
        using var context = new BunitContext();

        var badge = context.Render<MetadataBadge>(parameters => parameters
            .Add(b => b.Metadata, MetadataRecord.Empty)
            .Add(b => b.Folder, KnowledgeFolder.Tech));

        Assert.Equal(string.Empty, badge.Markup.Trim());
    }
}
