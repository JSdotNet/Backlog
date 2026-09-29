using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Services;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// The Dependency Proposal's tier-one rules: an item comes after what its own
/// text names — another item's link or title, an issue an open task was filed
/// as, an open task's link — and the reason is the text that named it, exactly
/// as written. What these also pin is what is <em>not</em> proposed.
/// </summary>
public sealed class DependencyProposalTests
{
    private static InboxItem Item(string title, string body = "", string? sourceUrl = null, params string[] repos)
    {
        var item = new InboxItem(Guid.CreateVersion7(), title, body, sourceUrl, Items.Noon, Items.Noon, ContentKind.Text,
            new InboxSource(InboxEnumMap.ManualChannel, null), replicaBacked: false);
        if (repos.Length > 0) item.SetRepoIds(repos);
        return item;
    }

    private static InboxTaskReferenceDto Task(
        string title,
        string? importItemId = null,
        IReadOnlyList<string>? repos = null,
        IReadOnlyList<InboxIssueReferenceDto>? issues = null,
        IReadOnlyList<string>? links = null) =>
        new(Guid.CreateVersion7(), importItemId, title, repos ?? [], issues ?? [], links ?? []);

    private static IReadOnlyList<ProposedDependency> Propose(IReadOnlyList<InboxItem> batch, params InboxTaskReferenceDto[] tasks) =>
        DependencyProposal.Propose(batch, tasks);

    // --- Rule 1: another item's source link ----------------------------------------

    [Fact]
    public void An_item_linking_to_another_items_source_comes_after_it_with_the_link_as_written()
    {
        var article = Item("Read the design doc", sourceUrl: "https://example.com/design");
        var follow = Item("Summarise it", "Notes on HTTPS://Example.com/design/, then decide.");

        var dependency = Assert.Single(Propose([article, follow]));

        Assert.Equal(follow.Id, dependency.From);
        Assert.Equal(DependencyTarget.ForItem(article.Id, "Read the design doc"), dependency.To);
        Assert.Equal("HTTPS://Example.com/design/", dependency.Reason);
        Assert.Equal(DependencyTier.Stated, dependency.Tier);
    }

    [Fact]
    public void A_longer_link_on_the_same_site_is_not_the_source()
    {
        var article = Item("Read the design doc", sourceUrl: "https://example.com/design");
        var other = Item("Something else", "See https://example.com/design-v2 instead.");

        Assert.Empty(Propose([article, other]));
    }

    // --- Rule 2: another item's title -----------------------------------------------

    [Fact]
    public void An_item_naming_another_items_title_comes_after_it_with_the_matched_text()
    {
        var first = Item("Set up the pipeline");
        var second = Item("Deploy the preview", "Once we  set up\nthe PIPELINE, deploy.");

        var dependency = Assert.Single(Propose([first, second]));

        Assert.Equal(second.Id, dependency.From);
        Assert.Equal(first.Id, dependency.To.Id);
        Assert.Equal(DependencyTargetKind.Item, dependency.To.Kind);
        Assert.Equal("set up\nthe PIPELINE", dependency.Reason);
    }

    [Theory]
    [InlineData("Pipeline")]
    [InlineData("Fix bug")]
    public void A_title_of_one_word_or_under_eight_characters_is_too_short_to_name(string title)
    {
        var short1 = Item(title);
        var other = Item("Something else", $"Mentions {title} in passing.");

        Assert.Empty(Propose([short1, other]));
    }

    [Fact]
    public void A_title_is_only_named_as_whole_words()
    {
        var first = Item("Set up the pipe");
        var other = Item("Other work", "We set up the pipeline today.");

        Assert.Empty(Propose([first, other]));
    }

    [Fact]
    public void Two_items_with_the_same_title_do_not_wait_on_each_other()
    {
        Assert.Empty(Propose([Item("Call the dentist"), Item("call the dentist")]));
    }

    // --- Rule 3: an issue an open task was filed as ----------------------------------

    [Fact]
    public void An_issue_number_in_one_of_the_items_repositories_names_the_task_filed_as_it()
    {
        var task = Task("Fix the login flow", importItemId: "0199a3f2-7c41-7d1a-9d0f-3d2c5e6f7a8b", issues: [new("JSdotNet/Backlog", 123)]);
        var item = Item("Follow up the login fix", "Blocked by #123 for now.", null, "jsdotnet/backlog");

        var dependency = Assert.Single(Propose([item], task));

        Assert.Equal(item.Id, dependency.From);
        Assert.Equal(DependencyTargetKind.Task, dependency.To.Kind);
        Assert.Equal(task.Id, dependency.To.Id);
        Assert.Equal("Fix the login flow", dependency.To.Title);
        Assert.Equal("0199a3f2-7c41-7d1a-9d0f-3d2c5e6f7a8b", dependency.To.After);
        Assert.Equal("#123", dependency.Reason);
    }

    [Fact]
    public void An_issue_number_is_not_the_tasks_when_the_item_is_not_in_its_repository()
    {
        var task = Task("Fix the login flow", issues: [new("JSdotNet/Backlog", 123)]);

        Assert.Empty(Propose([Item("Follow up", "Blocked by #123.", null, "jsdotnet/other")], task));
        Assert.Empty(Propose([Item("Follow up", "Blocked by #123.")], task));
    }

    [Theory]
    [InlineData("See #1234.")]
    [InlineData("See page#123.")]
    [InlineData("See &#123;")]
    public void Another_number_or_an_anchor_is_not_the_issue(string body)
    {
        var task = Task("Fix the login flow", issues: [new("JSdotNet/Backlog", 123)]);

        Assert.Empty(Propose([Item("Follow up", body, null, "JSdotNet/Backlog")], task));
    }

    [Fact]
    public void A_github_issue_url_names_the_task_whatever_the_items_repositories()
    {
        var task = Task("Fix the login flow", issues: [new("JSdotNet/Backlog", 123)]);
        var item = Item("Follow up", "Waiting on https://github.com/jsdotnet/backlog/issues/123#issuecomment-9.");

        var dependency = Assert.Single(Propose([item], task));

        Assert.Equal("https://github.com/jsdotnet/backlog/issues/123#issuecomment-9", dependency.Reason);
        Assert.Equal(task.Id.ToString("D"), dependency.To.After);
    }

    // --- Rule 4: an open task's link ------------------------------------------------

    [Fact]
    public void An_open_tasks_pull_request_or_source_link_names_it_with_the_link_as_written()
    {
        var pull = Task("Ship the parser", links: ["https://github.com/JSdotNet/Backlog/pull/42"]);
        var sourced = Task("Read the spec", links: ["https://example.com/spec"]);
        var item = Item("Wire it up", "After https://github.com/JSdotNet/Backlog/pull/42 and (https://example.com/spec).");

        var dependencies = Propose([item], pull, sourced);

        Assert.Equal([pull.Id, sourced.Id], dependencies.Select(dependency => dependency.To.Id));
        Assert.Equal(["https://github.com/JSdotNet/Backlog/pull/42", "https://example.com/spec"], dependencies.Select(dependency => dependency.Reason));
    }

    // --- The shape of the answer ----------------------------------------------------

    [Fact]
    public void One_dependency_per_pair_with_the_first_rules_reason()
    {
        var article = Item("Read the design doc", sourceUrl: "https://example.com/design");
        var follow = Item("Summarise", "Read the design doc at https://example.com/design.");

        var dependency = Assert.Single(Propose([article, follow]));

        Assert.Equal("https://example.com/design", dependency.Reason);
    }

    [Fact]
    public void An_item_never_waits_on_itself()
    {
        var item = Item("Read the design doc", "Read the design doc at https://example.com/design.", "https://example.com/design");

        Assert.Empty(Propose([item]));
    }

    [Fact]
    public void Unrelated_items_and_tasks_propose_nothing()
    {
        var task = Task("Refactor the store", issues: [new("a/one", 7)], links: ["https://github.com/a/one/issues/7"]);

        Assert.Empty(Propose([Item("Call the dentist", "Tomorrow at nine.", null, "a/one"), Item("Buy milk and bread")], task));
    }

    // --- What a task edge is written with -------------------------------------------

    /// <summary>An id an Inbox batch imported the task under is its item's guid
    /// — with the repository on the end when the item had several — and so
    /// names that one task anywhere in the store.</summary>
    [Theory]
    [InlineData("0199a3f2-7c41-7d1a-9d0f-3d2c5e6f7a8b")]
    [InlineData("0199a3f2-7c41-7d1a-9d0f-3d2c5e6f7a8b/JSdotNet/Backlog")]
    public void A_task_imported_by_an_inbox_batch_is_named_by_the_id_it_was_imported_under(string importItemId)
    {
        var task = Task("Earlier", importItemId: importItemId);

        Assert.Equal(importItemId, DependencyTarget.ForTask(task).After);
    }

    /// <summary>A hand-written slug may be carried by two plans at once, and
    /// Tasks leaves an ambiguous one unresolved — the new entry would wait on
    /// nothing for good. So a task imported under anything else is named by its
    /// own id.</summary>
    [Theory]
    [InlineData("setup")]
    [InlineData("0199a3f2-7c41-7d1a-9d0f-3d2c5e6f7a8bx")]
    [InlineData("0199a3f27c417d1a9d0f3d2c5e6f7a8b")]
    [InlineData("step/0199a3f2-7c41-7d1a-9d0f-3d2c5e6f7a8b")]
    [InlineData(null)]
    [InlineData("  ")]
    public void A_task_imported_under_any_other_id_is_named_by_its_own_id(string? importItemId)
    {
        var task = Task("Earlier", importItemId: importItemId);

        Assert.Equal(task.Id.ToString("D"), DependencyTarget.ForTask(task).After);
    }

    [Fact]
    public void Each_item_may_wait_on_several_things_and_both_directions_are_proposed()
    {
        var first = Item("Write the release notes", "After we ship the release.");
        var second = Item("Ship the release", "Write the release notes first.");

        var dependencies = Propose([first, second]);

        Assert.Equal([(first.Id, second.Id), (second.Id, first.Id)], dependencies.Select(dependency => (dependency.From, dependency.To.Id)));
    }
}
