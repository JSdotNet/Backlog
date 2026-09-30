using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.ProposeBatch;
using Backlog.Modules.Inbox.Features.RouteBatchToBacklog;
using Backlog.Modules.Inbox.Services;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// Tier two: what the Inbox notices about a batch's order beyond what its items
/// state — same list, same repository, capture order, setup first, duplicates.
/// What these pin as much as each rule is that a hint is only ever said: it is
/// its own type, it moves nothing in the order, and a duplicate group is offered
/// as one merge rather than chained with tier-one edges.
/// </summary>
public sealed class OrderingHintTests
{
    private static InboxItem Item(
        string title,
        string? sourceUrl = null,
        string body = "",
        int minutes = 0,
        Guid? listId = null,
        params string[] repos)
    {
        var item = new InboxItem(Guid.CreateVersion7(), title, body, sourceUrl, Items.Noon.AddMinutes(minutes), Items.Noon,
            ContentKind.Text, new InboxSource(InboxEnumMap.ManualChannel, null), replicaBacked: false);
        if (repos.Length > 0) item.SetRepoIds(repos);
        if (listId is { } list) item.MoveToList(list);
        return item;
    }

    private static IReadOnlyList<OrderingHint> Hints(IReadOnlyList<InboxItem> batch, IReadOnlyDictionary<Guid, string>? lists = null) =>
        DependencyProposal.Hints(batch, DependencyProposal.Propose(batch, []), lists ?? new Dictionary<Guid, string>());

    private static IReadOnlyList<OrderingHint> Of(IReadOnlyList<OrderingHint> hints, OrderingHintKind kind) =>
        [.. hints.Where(hint => hint.Kind == kind)];

    [Fact]
    public void A_batch_of_one_has_no_hints()
    {
        Assert.Empty(Hints([Item("Set up the pipeline", minutes: 5)]));
    }

    // --- Same list -----------------------------------------------------------------

    [Fact]
    public void Items_sharing_a_list_are_named_with_the_lists_name()
    {
        var reading = Guid.NewGuid();
        var first = Item("Read the design doc", listId: reading);
        var second = Item("Read the rollout plan", minutes: 1, listId: reading);
        var third = Item("Buy milk", minutes: 2);

        var hint = Assert.Single(Of(Hints([first, second, third], new Dictionary<Guid, string> { [reading] = "Reading" }), OrderingHintKind.SameList));

        Assert.Equal([first.Id, second.Id], hint.Items);
        Assert.Equal("Same list: Reading", hint.Reason);
    }

    [Fact]
    public void A_list_every_item_shares_says_nothing_about_the_order()
    {
        var reading = Guid.NewGuid();

        Assert.Empty(Of(Hints([Item("Read one thing", listId: reading), Item("Read another thing", minutes: 1, listId: reading)]), OrderingHintKind.SameList));
    }

    // --- Same repository -------------------------------------------------------------

    [Fact]
    public void Items_sharing_a_repository_are_named_with_it_whatever_the_case()
    {
        var first = Item("Fix the parser", repos: "JSdotNet/Backlog");
        var second = Item("Fix the lexer", minutes: 1, repos: "jsdotnet/backlog");
        var third = Item("Fix the docs", minutes: 2, repos: "jsdotnet/site");

        var hint = Assert.Single(Of(Hints([first, second, third]), OrderingHintKind.SameRepository));

        Assert.Equal([first.Id, second.Id], hint.Items);
        Assert.Equal("Same repository: JSdotNet/Backlog", hint.Reason);
    }

    // --- Capture order ---------------------------------------------------------------

    [Fact]
    public void Capture_order_is_hinted_only_when_it_differs_from_the_proposed_order()
    {
        var older = Item("Write the tests", minutes: 0);
        var newer = Item("Write the code", minutes: 10);

        Assert.Empty(Of(Hints([older, newer]), OrderingHintKind.CaptureOrder));

        var hint = Assert.Single(Of(Hints([newer, older]), OrderingHintKind.CaptureOrder));
        Assert.Equal([older.Id, newer.Id], hint.Items);
        Assert.Equal("Captured in another order", hint.Reason);
    }

    // --- Setup first -----------------------------------------------------------------

    [Theory]
    [InlineData("Set up the pipeline", true)]
    [InlineData("Setup CI", true)]
    [InlineData("set-up the repo", true)]
    [InlineData("Install the SDK", true)]
    [InlineData("Configure logging", true)]
    [InlineData("Scaffold the module", true)]
    [InlineData("Bootstrap the app", true)]
    [InlineData("git init the folder", true)]
    [InlineData("Initialise the store", true)]
    [InlineData("Uninstall the old agent", false)]
    [InlineData("Write the installation guide", false)]
    [InlineData("Ship the release", false)]
    public void A_title_reads_like_setup_by_its_whole_words(string title, bool expected)
    {
        Assert.Equal(expected, DependencyProposal.IsSetupShaped(title));
    }

    [Fact]
    public void A_setup_item_behind_other_work_is_hinted_to_go_first_and_one_already_first_is_not()
    {
        var work = Item("Write the endpoint", minutes: 0);
        var setup = Item("Set up the database", minutes: 1);

        var hint = Assert.Single(Of(Hints([work, setup]), OrderingHintKind.SetupFirst));
        Assert.Equal([setup.Id], hint.Items);

        Assert.Empty(Of(Hints([setup, work]), OrderingHintKind.SetupFirst));
    }

    // --- Duplicates ----------------------------------------------------------------

    [Fact]
    public void Items_with_the_same_link_are_one_duplicate_group_kept_in_the_batchs_order()
    {
        var first = Item("Read this", "https://example.com/post");
        var second = Item("Something to read", "https://www.example.com/post/", minutes: 1);
        var other = Item("Buy milk", minutes: 2);

        var hint = Assert.Single(Of(Hints([first, other, second]), OrderingHintKind.Duplicate));

        Assert.Equal([first.Id, second.Id], hint.Items);
        Assert.Equal("Same link", hint.Reason);
    }

    [Fact]
    public void Items_with_nearly_the_same_title_are_one_duplicate_group()
    {
        var first = Item("Write the release notes");
        var second = Item("Write the release note", minutes: 1);

        var hint = Assert.Single(Of(Hints([first, second]), OrderingHintKind.Duplicate));

        Assert.Equal("Nearly the same title", hint.Reason);
    }

    [Fact]
    public void A_group_holds_only_items_that_are_duplicates_of_its_first_item_directly()
    {
        // A and B share a link, B and C nearly a title; A and C have nothing to
        // do with each other, so C is not folded into A through B.
        var a = Item("Read the post", "https://example.com/post");
        var b = Item("Read the post later", "https://example.com/post", minutes: 1);
        var c = Item("Read the post later!", minutes: 2);

        var hint = Assert.Single(Of(Hints([a, b, c]), OrderingHintKind.Duplicate));

        Assert.Equal([a.Id, b.Id], hint.Items);
        Assert.Equal("Same link", hint.Reason);
    }

    [Fact]
    public void A_group_whose_members_match_the_first_for_different_reasons_says_both()
    {
        var first = Item("Read the design doc", "https://example.com/post");
        var sameLink = Item("Something to read", "https://example.com/post", minutes: 1);
        var sameTitle = Item("Read the design docs", minutes: 2);

        var hint = Assert.Single(Of(Hints([first, sameLink, sameTitle]), OrderingHintKind.Duplicate));

        Assert.Equal([first.Id, sameLink.Id, sameTitle.Id], hint.Items);
        Assert.Equal("Same link or nearly the same title", hint.Reason);
    }

    [Fact]
    public void Only_direct_duplicates_lose_their_edges_so_a_chain_keeps_the_real_one()
    {
        // A~B by link, B~C by title, A and C unrelated. C naming A's link is a
        // real dependency and stays; B and C naming each other does not.
        var a = Item("Read the post", "https://example.com/post");
        var b = Item("Read the post later", "https://example.com/post", "Read the post later!", minutes: 1);
        var c = Item("Read the post later!", body: "After https://example.com/post, and Read the post later.", minutes: 2);

        var dependencies = DependencyProposal.Propose([a, b, c], []);

        Assert.Contains(dependencies, dependency => dependency.From == c.Id && dependency.To.Id == a.Id);
        Assert.DoesNotContain(dependencies, dependency => dependency.From == c.Id && dependency.To.Id == b.Id);
        Assert.DoesNotContain(dependencies, dependency => dependency.From == b.Id && dependency.To.Id == a.Id);
    }

    [Fact]
    public void No_tier_one_dependency_is_proposed_inside_a_duplicate_group()
    {
        var first = Item("Read the post", "https://example.com/post");
        var second = Item("Summarise the post", "https://example.com/post", "Notes on https://example.com/post.", minutes: 1);
        var third = Item("Plan the talk", body: "After https://example.com/post, plan it.", minutes: 2);

        var dependencies = DependencyProposal.Propose([first, second, third], []);

        Assert.DoesNotContain(dependencies, dependency => dependency.From == second.Id && dependency.To.Id == first.Id);
        Assert.Contains(dependencies, dependency => dependency.From == third.Id && dependency.To.Id == first.Id);
    }

    [Fact]
    public async Task Hints_never_change_the_order_the_edges_the_loops_or_what_the_route_sends()
    {
        // Every kind of hint at once, beside one real dependency: the preview
        // names the endpoint. The endpoint and its near-twin are a duplicate
        // group; the setup item is behind other work; the preview and the
        // endpoint share a repository; the capture order is not the order proposed.
        var store = new InMemoryInboxStore();
        var deploy = Item("Deploy the preview", body: "After Write the endpoint.", minutes: 0, repos: "a/one");
        var work = Item("Write the endpoint", minutes: 5, repos: "a/one");
        var setup = Item("Set up the database", minutes: 1);
        var twin = Item("Write the endpoints", minutes: 6);
        IReadOnlyList<InboxItem> batch = [deploy, work, setup, twin];
        foreach (var item in batch) store.Seed(item);
        IReadOnlyList<Guid> ids = [.. batch.Select(item => item.Id)];

        var proposal = (await new ProposeBatchQueryHandler(store, store)
            .Handle(new ProposeBatchQuery(ids), TestContext.Current.CancellationToken)).Value;

        Assert.Equal(
            [OrderingHintKind.Duplicate, OrderingHintKind.SetupFirst, OrderingHintKind.SameRepository, OrderingHintKind.CaptureOrder],
            proposal.Hints.Select(hint => hint.Kind));

        // The order, the edges and the loops are the dependencies' alone.
        var edges = InboxBatchOrder.Edges(proposal.Dependencies);
        Assert.Equal([new InboxBatchEdge(deploy.Id, work.Id)], edges);
        Assert.Equal([work.Id, deploy.Id, setup.Id, twin.Id], InboxBatchOrder.Order(ids, edges));
        Assert.Empty(InboxBatchOrder.Loops(ids, edges));

        // And the route, handed the proposal's choices, sends every item in that
        // order with that one after: token — no hint became one, nothing merged.
        var target = new FakeBacklogTarget();
        var routed = await new RouteBatchToBacklogCommandHandler(store, store, target, new FakeTimeProvider(Items.Noon))
            .Handle(
                new RouteBatchToBacklogCommand(ids, null, new InboxBatchRouteChoicesDto(proposal.PlanTag, Dependencies: proposal.Dependencies)),
                TestContext.Current.CancellationToken);

        var request = Assert.Single(target.BatchRequests);
        Assert.Equal([work.Id, deploy.Id, setup.Id, twin.Id], request.Items.Select(item => item.InboxItemId));
        Assert.Equal([work.Id], request.Items.Single(item => item.InboxItemId == deploy.Id).AfterItems);
        Assert.All(request.Items.Where(item => item.InboxItemId != deploy.Id), item => Assert.Empty(item.AfterItems ?? []));
        Assert.All(request.Items, item => Assert.Empty(item.AfterTasks ?? []));
        Assert.Empty(routed.Value.Archived);
        Assert.Equal(4, routed.Value.Routed.Count);
    }
}
