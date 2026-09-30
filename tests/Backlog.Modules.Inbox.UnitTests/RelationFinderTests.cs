using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Services;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// The Relation Finder's rules: which other items look like the same capture —
/// the same link, nearly the same title, the same site — and which tasks already
/// carry the item — routed from it, the same link, an issue its text names —
/// each with the reason in words, the strongest one winning and the item itself
/// never among them.
/// </summary>
public sealed class RelationFinderTests
{
    private static InboxItem Item(string title, string? sourceUrl = null, string body = "", params string[] repos)
    {
        var item = new InboxItem(Guid.CreateVersion7(), title, body, sourceUrl, Items.Noon, Items.Noon, ContentKind.Text,
            new InboxSource(InboxEnumMap.ManualChannel, null), replicaBacked: false);
        if (repos.Length > 0) item.SetRepoIds(repos);
        return item;
    }

    private static InboxTaskReferenceDto Task(
        string title,
        string? importItemId = null,
        IReadOnlyList<InboxIssueReferenceDto>? issues = null,
        Guid? sourceInboxId = null,
        string? sourceUrl = null,
        bool isOpen = true) =>
        new(Guid.CreateVersion7(), importItemId, title, [], issues ?? [], sourceUrl is null ? [] : [sourceUrl], sourceInboxId, sourceUrl, isOpen);

    private static InboxRelationsDto Find(InboxItem item, IReadOnlyList<InboxItem> others, params InboxTaskReferenceDto[] tasks) =>
        RelationFinder.Find(item, others, tasks);

    // --- Items -------------------------------------------------------------------

    [Fact]
    public void An_item_with_the_same_link_relates_as_same_link()
    {
        var item = Item("Read the design doc", "https://www.Example.com/design/#intro");
        var other = Item("Something else entirely", "https://example.com/design");

        var relation = Assert.Single(Find(item, [item, other]).Items);

        Assert.Equal(other.Id, relation.Id);
        Assert.Equal(InboxRelationKind.SameLink, relation.Kind);
        Assert.Equal("Same link", relation.Reason);
        Assert.Equal("Something else entirely", relation.Title);
        Assert.Equal(InboxStatus.Unprocessed, relation.Status);
    }

    [Fact]
    public void An_item_on_the_same_site_relates_as_same_site()
    {
        var item = Item("Read about caching", "https://example.com/blog/caching");
        var other = Item("Read about queues", "https://www.example.com/blog/queues");

        var relation = Assert.Single(Find(item, [other]).Items);

        Assert.Equal(InboxRelationKind.SameSite, relation.Kind);
        Assert.Equal("Same site", relation.Reason);
    }

    [Fact]
    public void Two_issues_in_one_github_repository_share_a_site_and_two_repositories_do_not()
    {
        var item = Item("Look at the crash", "https://github.com/JSdotNet/Backlog/issues/12");
        var sameRepo = Item("Look at the leak", "https://github.com/jsdotnet/backlog/issues/40");
        var otherRepo = Item("Look at the parser", "https://github.com/dotnet/runtime/issues/7");

        var relation = Assert.Single(Find(item, [sameRepo, otherRepo]).Items);

        Assert.Equal(sameRepo.Id, relation.Id);
        Assert.Equal(InboxRelationKind.SameSite, relation.Kind);
    }

    [Fact]
    public void A_near_identical_title_relates_as_nearly_the_same_title()
    {
        var item = Item("Set up the nightly pipeline");
        var other = Item("set up the nightly pipelines!");

        var relation = Assert.Single(Find(item, [other]).Items);

        Assert.Equal(InboxRelationKind.SimilarTitle, relation.Kind);
        Assert.Equal("Nearly the same title", relation.Reason);
    }

    [Fact]
    public void A_title_shorter_than_eight_characters_never_relates_by_title()
    {
        Assert.Empty(Find(Item("Fix bug"), [Item("fix bug")]).Items);
    }

    [Fact]
    public void The_strongest_reason_wins_and_each_item_is_listed_once()
    {
        var item = Item("Read the design doc", "https://example.com/design");
        var sameLinkAndTitle = Item("Read the design doc", "https://example.com/design/");
        var sameTitleAndSite = Item("Read the design docs", "https://example.com/other");

        var relations = Find(item, [sameTitleAndSite, sameLinkAndTitle]).Items;

        Assert.Equal([sameLinkAndTitle.Id, sameTitleAndSite.Id], relations.Select(relation => relation.Id));
        Assert.Equal([InboxRelationKind.SameLink, InboxRelationKind.SimilarTitle], relations.Select(relation => relation.Kind));
    }

    [Fact]
    public void The_item_itself_is_never_related_and_an_unrelated_item_is_left_out()
    {
        var item = Item("Read the design doc", "https://example.com/design");

        Assert.Empty(Find(item, [item, Item("Buy milk")]).Items);
    }

    [Fact]
    public void A_decided_item_still_relates_with_its_status()
    {
        var item = Item("Read the design doc", "https://example.com/design");
        var archived = Item("Read the design doc again", "https://example.com/design");
        archived.Archive(Items.Noon);

        var relation = Assert.Single(Find(item, [archived]).Items);

        Assert.Equal(InboxStatus.Archived, relation.Status);
    }

    // --- Tasks -------------------------------------------------------------------

    [Fact]
    public void A_task_carrying_the_item_as_its_source_was_routed_from_it()
    {
        var item = Item("Ship the parser");
        var task = Task("Ship the parser", sourceInboxId: item.Id, isOpen: false);

        var relation = Assert.Single(Find(item, [], task).Tasks);

        Assert.Equal(task.Id, relation.Id);
        Assert.Equal(InboxRelationKind.RoutedFromItem, relation.Kind);
        Assert.Equal("Routed from this item", relation.Reason);
        Assert.False(relation.IsOpen);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/JSdotNet/Backlog")]
    public void A_task_imported_under_the_items_id_was_routed_from_it(string suffix)
    {
        var item = Item("Ship the parser");
        var task = Task("Ship the parser", importItemId: item.Id.ToString("D") + suffix);

        Assert.Equal(InboxRelationKind.RoutedFromItem, Assert.Single(Find(item, [], task).Tasks).Kind);
    }

    [Fact]
    public void A_task_the_items_routing_names_was_routed_from_it()
    {
        var item = Item("Ship the parser");
        var task = Task("Something the item was linked to");
        item.LinkToTask(task.Id, [], Items.Noon);

        Assert.Equal(InboxRelationKind.RoutedFromItem, Assert.Single(Find(item, [], task).Tasks).Kind);
    }

    [Fact]
    public void An_import_id_that_only_starts_like_the_items_id_is_not_it()
    {
        var item = Item("Ship the parser");
        var task = Task("Ship the parser", importItemId: item.Id.ToString("D") + "x");

        Assert.Empty(Find(item, [], task).Tasks);
    }

    [Fact]
    public void A_task_with_the_same_source_link_relates_as_same_link()
    {
        var item = Item("Read the design doc", "https://example.com/design");
        var task = Task("Write up the design", sourceUrl: "https://EXAMPLE.com/design/");

        var relation = Assert.Single(Find(item, [], task).Tasks);

        Assert.Equal(InboxRelationKind.SameLink, relation.Kind);
        Assert.Equal("Same link", relation.Reason);
    }

    [Fact]
    public void A_task_filed_as_an_issue_the_item_names_by_number_in_its_repository_relates_as_that_issue()
    {
        var item = Item("Follow up the crash", body: "See #12 for the trace.", repos: "jsdotnet/backlog");
        var task = Task("Fix the crash", issues: [new InboxIssueReferenceDto("JSdotNet/Backlog", 12)]);

        var relation = Assert.Single(Find(item, [], task).Tasks);

        Assert.Equal(InboxRelationKind.SameIssue, relation.Kind);
        Assert.Equal("Links issue JSdotNet/Backlog#12", relation.Reason);
    }

    [Fact]
    public void A_task_filed_as_an_issue_the_item_links_relates_wherever_the_issue_lives()
    {
        var item = Item("Follow up https://github.com/JSdotNet/Backlog/issues/12");
        var task = Task("Fix the crash", issues: [new InboxIssueReferenceDto("JSdotNet/Backlog", 12)]);

        Assert.Equal(InboxRelationKind.SameIssue, Assert.Single(Find(item, [], task).Tasks).Kind);
    }

    [Fact]
    public void An_issue_number_outside_the_items_repositories_names_nothing()
    {
        var item = Item("Follow up the crash", body: "See #12.", repos: "jsdotnet/other");
        var task = Task("Fix the crash", issues: [new InboxIssueReferenceDto("JSdotNet/Backlog", 12)]);

        Assert.Empty(Find(item, [], task).Tasks);
    }

    [Fact]
    public void Routed_from_this_item_outranks_the_same_link_and_tasks_keep_the_backlogs_order_within_a_reason()
    {
        var item = Item("Read the design doc", "https://example.com/design");
        var sameLink = Task("Summarise the design", sourceUrl: "https://example.com/design");
        var routed = Task("Read the design doc", sourceInboxId: item.Id, sourceUrl: "https://example.com/design");
        var unrelated = Task("Buy milk");

        var relations = Find(item, [], sameLink, unrelated, routed).Tasks;

        Assert.Equal([routed.Id, sameLink.Id], relations.Select(relation => relation.Id));
        Assert.Equal([InboxRelationKind.RoutedFromItem, InboxRelationKind.SameLink], relations.Select(relation => relation.Kind));
    }

    // --- The shared rules ----------------------------------------------------------

    [Theory]
    [InlineData("https://example.com/a", "https://example.com/a/")]
    [InlineData("https://example.com/a#frag", "HTTPS://EXAMPLE.COM/a")]
    [InlineData("https://www.example.com/a", "https://example.com/a")]
    [InlineData("http://example.com/a", "https://example.com/a")]
    [InlineData("https://example.com/a/?utm_source=x&utm_medium=mail", "https://example.com/a")]
    [InlineData("https://example.com/a?fbclid=1&id=7&gclid=2", "https://example.com/a?id=7")]
    [InlineData("https://twitter.com/x/status/1?ref_src=twsrc", "https://twitter.com/x/status/1")]
    public void Links_that_differ_only_in_case_fragment_slash_www_scheme_or_tracking_are_the_same(string a, string b)
    {
        Assert.True(InboxUrl.Same(a, b));
    }

    [Theory]
    [InlineData("https://example.com/issues/12", "https://example.com/issues/123")]
    [InlineData("https://example.com/watch?v=abc", "https://example.com/watch?v=xyz")]
    [InlineData("https://example.com/a?id=7", "https://example.com/a")]
    [InlineData(null, null)]
    public void Different_or_missing_links_are_not_the_same(string? a, string? b)
    {
        Assert.False(InboxUrl.Same(a, b));
    }

    [Theory]
    [InlineData("https://www.Example.com/blog/a", "example.com")]
    [InlineData("https://github.com/JSdotNet/Backlog/pull/3", "github.com/jsdotnet/backlog")]
    [InlineData("https://gitlab.com/group/project/-/issues/1", "gitlab.com/group/project")]
    [InlineData("https://github.com/JSdotNet", null)]
    [InlineData("not a link", null)]
    public void A_site_is_the_host_or_on_a_forge_the_repository(string url, string? site)
    {
        Assert.Equal(site, InboxUrl.Site(url));
    }

    [Fact]
    public void A_normalised_title_drops_case_punctuation_and_extra_spacing()
    {
        Assert.Equal("set up ci now", TitleSimilarity.Normalise("  Set-up   CI, now! "));
    }

    [Theory]
    [InlineData("Set up the pipeline", "set-up the PIPELINE", true)]
    [InlineData("Write the release notes", "Write the release note", true)]
    [InlineData("Write the release notes", "Write the design notes", false)]
    [InlineData("Fix bug", "Fix bug", false)]
    [InlineData("Fix login", "Fix logins", true)]
    [InlineData("Fix login", "Fix loginss", false)]
    public void Titles_are_near_identical_when_equal_normalised_or_within_one_edit_in_ten(string a, string b, bool expected)
    {
        Assert.Equal(expected, TitleSimilarity.IsNearIdentical(a, b));
    }
}
