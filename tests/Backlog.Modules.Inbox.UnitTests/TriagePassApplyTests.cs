using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Extensions;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// Apply on the AI triage pass (local ADR 0023 §5), over the real module: each
/// accepted decision runs through the port act the reader would have used by
/// hand, in the order given — a plan as one batch under its own plan tag, a
/// duplicate as a merge or an archive, a single route, a filing, an archive —
/// and the first one refused stops the rest, with a sentence naming the item,
/// while what already landed stays.
/// </summary>
public sealed class TriagePassApplyTests : IDisposable
{
    private const string TagShape = @"^\+[a-z0-9-]+-[0-9a-f]{8}$";

    private readonly InMemoryInboxStore _store = new();
    private readonly FakeBacklogTarget _target = new();
    private readonly ServiceProvider _provider;

    public TriagePassApplyTests()
    {
        _provider = new ServiceCollection()
            .AddSingleton<TimeProvider>(new FakeTimeProvider(Items.Noon.AddHours(1)))
            .AddSingleton<IInboxItemRepository>(_store)
            .AddSingleton<IInboxOrganizerRepository>(_store)
            .AddSingleton<IInboxBacklogTarget>(_target)
            .AddInboxModule()
            .BuildServiceProvider();
    }

    public void Dispose() => _provider.Dispose();

    private IInboxItems Inbox => _provider.GetRequiredService<IInboxItems>();

    [Fact]
    public async Task A_plan_routes_its_members_in_one_batch_under_a_plus_tag_built_from_its_title_with_its_repositories()
    {
        var first = Seed("Tasks pane loses scroll position");
        var second = Seed("Scroll jumps to top when phone edits");

        var outcome = await ApplyAsync(new InboxTriagePlanDecision("Tasks sync polish", [first.Id, second.Id], ["acme/web", "acme/sync"]));

        Assert.Null(outcome.Failure);
        var request = Assert.Single(_target.BatchRequests);
        Assert.Matches(TagShape, request.PlanTag);
        Assert.StartsWith("+tasks-sync-polish-", request.PlanTag, StringComparison.Ordinal);
        Assert.Equal([first.Id, second.Id], request.Items.Select(item => item.InboxItemId));
        Assert.All(request.Items, item => Assert.Equal(["acme/web", "acme/sync"], item.RepoIds));

        var applied = Assert.Single(outcome.Applied);
        Assert.NotNull(applied.Batch);
        Assert.Equal(request.PlanTag, applied.Batch.PlanTag);
        Assert.Equal([first.Id, second.Id], applied.Batch.Routed.Select(routed => routed.InboxItemId));
        Assert.Equal(InboxStatus.Triaged, first.Status);
        Assert.Equal(InboxStatus.Triaged, second.Status);
    }

    [Fact]
    public async Task A_plan_without_repositories_routes_each_member_to_its_own()
    {
        var first = Seed("One", repoIds: ["acme/web"]);
        var second = Seed("Two");

        var outcome = await ApplyAsync(new InboxTriagePlanDecision("Plan", [first.Id, second.Id], []));

        Assert.Null(outcome.Failure);
        var request = Assert.Single(_target.BatchRequests);
        Assert.Equal(["acme/web"], request.Items[0].RepoIds);
        Assert.Empty(request.Items[1].RepoIds);
    }

    [Fact]
    public async Task Two_plans_go_as_two_batches_with_two_different_tags()
    {
        var a = Seed("A one");
        var b = Seed("A two");
        var c = Seed("B one");
        var d = Seed("B two");

        var outcome = await ApplyAsync(
            new InboxTriagePlanDecision("Same title", [a.Id, b.Id], []),
            new InboxTriagePlanDecision("Same title", [c.Id, d.Id], []));

        Assert.Null(outcome.Failure);
        Assert.Equal(2, _target.BatchRequests.Count);
        Assert.NotEqual(_target.BatchRequests[0].PlanTag, _target.BatchRequests[1].PlanTag);
        Assert.All(_target.BatchRequests, request => Assert.StartsWith("+same-title-", request.PlanTag, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_merge_into_a_task_writes_the_comment_and_archives_the_capture_as_its_duplicate()
    {
        var item = Seed("Scroll jumps to top");
        var task = Guid.CreateVersion7();

        var outcome = await ApplyAsync(new InboxTriageDuplicateDecision(item.Title, item.Id, InboxTriageTargetKind.Task, task, InboxTriageDuplicateChoice.Merge));

        Assert.Null(outcome.Failure);
        var comment = Assert.Single(_target.Comments);
        Assert.Equal(item.Id, comment.InboxItemId);
        Assert.Equal(task, comment.TaskId);
        Assert.Equal(InboxStatus.Archived, item.Status);
        Assert.Equal(task, item.DuplicateOf);
        Assert.True(item.DuplicateOfTask);
    }

    [Fact]
    public async Task A_merge_with_another_item_archives_the_capture_as_that_items_duplicate()
    {
        var kept = Seed("Aspire 13 dashboard walkthrough");
        var item = Seed("What is new in the Aspire 13 dashboard");

        var outcome = await ApplyAsync(new InboxTriageDuplicateDecision(item.Title, item.Id, InboxTriageTargetKind.InboxItem, kept.Id, InboxTriageDuplicateChoice.Merge));

        Assert.Null(outcome.Failure);
        Assert.Empty(_target.Comments);
        Assert.Equal(InboxStatus.Archived, item.Status);
        Assert.Equal(kept.Id, item.DuplicateOf);
        Assert.False(item.DuplicateOfTask);
        Assert.Equal(InboxStatus.Unprocessed, kept.Status);
    }

    [Fact]
    public async Task Archiving_the_capture_of_a_duplicate_archives_it_and_leaves_the_task_alone()
    {
        var item = Seed("Scroll jumps to top");

        var outcome = await ApplyAsync(new InboxTriageDuplicateDecision(item.Title, item.Id, InboxTriageTargetKind.Task, Guid.CreateVersion7(), InboxTriageDuplicateChoice.Archive));

        Assert.Null(outcome.Failure);
        Assert.Empty(_target.Comments);
        Assert.Equal(InboxStatus.Archived, item.Status);
        Assert.Null(item.DuplicateOf);
    }

    [Fact]
    public async Task A_single_route_assigns_its_repositories_then_routes()
    {
        var item = Seed("Ask about the Cosmos emulator", repoIds: ["acme/old"]);

        var outcome = await ApplyAsync(new InboxTriageRouteDecision(item.Title, item.Id, ["acme/sync"]));

        Assert.Null(outcome.Failure);
        var request = Assert.Single(_target.Requests);
        Assert.Equal(["acme/sync"], request.RepoIds);
        Assert.Equal(["acme/sync"], item.RepoIds);
        Assert.Equal(InboxStatus.Triaged, item.Status);
        var applied = Assert.Single(outcome.Applied);
        Assert.Equal(item.Id, applied.Routed?.InboxItemId);
    }

    [Fact]
    public async Task A_single_route_without_repositories_keeps_the_items_own()
    {
        var item = Seed("Read the design doc", repoIds: ["acme/web"]);

        var outcome = await ApplyAsync(new InboxTriageRouteDecision(item.Title, item.Id, []));

        Assert.Null(outcome.Failure);
        Assert.Equal(["acme/web"], Assert.Single(_target.Requests).RepoIds);
    }

    [Fact]
    public async Task A_filing_moves_the_item_to_its_list_and_an_archive_archives()
    {
        var reading = InboxList.Create("Reading", null, 0, Items.Noon);
        _store.Seed(reading);
        var filed = Seed(".NET 11 performance");
        var dropped = Seed("ok");

        var outcome = await ApplyAsync(
            new InboxTriageFilingDecision(filed.Title, filed.Id, reading.Id),
            new InboxTriageArchiveDecision(dropped.Title, dropped.Id));

        Assert.Null(outcome.Failure);
        Assert.Equal(reading.Id, filed.ListId);
        Assert.Equal(InboxStatus.Unprocessed, filed.Status);
        Assert.Equal(InboxStatus.Archived, dropped.Status);
        Assert.Equal(2, outcome.Applied.Count);
        Assert.Equal(0, outcome.NotTried);
    }

    [Fact]
    public async Task The_decisions_run_in_the_order_given()
    {
        var archived = Seed("First");
        var routed = Seed("Second");

        var decisions = new InboxTriageDecision[]
        {
            new InboxTriageArchiveDecision(archived.Title, archived.Id),
            new InboxTriageRouteDecision(routed.Title, routed.Id, []),
        };

        var outcome = await InboxTriagePassApply.ApplyAsync(Inbox, decisions, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(decisions, outcome.Applied.Select(applied => applied.Decision));
        Assert.Equal([archived.Id, routed.Id], _store.ItemWrites);
    }

    [Fact]
    public async Task The_first_refusal_stops_the_rest_names_the_item_and_leaves_what_landed()
    {
        var before = Seed("Archived first");
        var merged = Seed("Scroll jumps to top");
        var after = Seed("Never tried");
        _target.FailCommentWith = Error.Validation("comment.not_prose", "A comment is prose");

        var first = new InboxTriageArchiveDecision(before.Title, before.Id);
        var failing = new InboxTriageDuplicateDecision(merged.Title, merged.Id, InboxTriageTargetKind.Task, Guid.CreateVersion7(), InboxTriageDuplicateChoice.Merge);
        var outcome = await ApplyAsync(first, failing, new InboxTriageRouteDecision(after.Title, after.Id, []));

        Assert.Equal([first], outcome.Applied.Select(applied => applied.Decision));
        Assert.NotNull(outcome.Failure);
        Assert.Same(failing, outcome.Failure.Decision);
        Assert.Equal(merged.Id, outcome.Failure.ItemId);
        Assert.Equal("Scroll jumps to top", outcome.Failure.ItemName);
        Assert.Equal("comment.not_prose", outcome.Failure.Error.Code);
        Assert.Equal(1, outcome.NotTried);
        Assert.Equal(
            "Couldn't merge \"Scroll jumps to top\" into the backlog entry: A comment is prose. The decisions before it stay applied; the rest were not tried.",
            outcome.Failure.Sentence);

        Assert.Equal(InboxStatus.Archived, before.Status);
        Assert.Equal(InboxStatus.Unprocessed, merged.Status);
        Assert.Equal(InboxStatus.Unprocessed, after.Status);
        Assert.Empty(_target.Requests);
    }

    [Fact]
    public async Task A_plan_the_backlog_refuses_part_of_keeps_what_routed_and_names_the_first_refused_member_by_its_title()
    {
        var sent = Seed("Sent");
        var refused = Seed("Refused");
        var later = Seed("Later");
        _target.RefuseItems[refused.Id] = Error.Validation("inbox.batch.notes", "Its notes would split the plan.");

        var plan = new InboxTriagePlanDecision("Sync polish", [sent.Id, refused.Id], []);
        var titles = new Dictionary<Guid, string> { [sent.Id] = "Sent", [refused.Id] = "The refused one" };
        var outcome = await InboxTriagePassApply.ApplyAsync(
            Inbox,
            [plan, new InboxTriageArchiveDecision(later.Title, later.Id)],
            titles,
            TestContext.Current.CancellationToken);

        var applied = Assert.Single(outcome.Applied);
        Assert.Same(plan, applied.Decision);
        Assert.Equal([sent.Id], applied.Batch!.Routed.Select(routed => routed.InboxItemId));
        Assert.NotNull(outcome.Failure);
        Assert.Equal(refused.Id, outcome.Failure.ItemId);
        Assert.Equal(
            "Couldn't move \"The refused one\" to the backlog as part of \"Sync polish\": Its notes would split the plan. The decisions before it stay applied; the rest were not tried.",
            outcome.Failure.Sentence);
        Assert.Equal(1, outcome.NotTried);
        Assert.Equal(InboxStatus.Triaged, sent.Status);
        Assert.Equal(InboxStatus.Unprocessed, later.Status);
    }

    [Fact]
    public async Task A_plan_refused_whole_is_named_by_its_title_and_stops_the_rest()
    {
        var first = Seed("First");
        var second = Seed("Second");
        var later = Seed("Later");
        _target.FailWith = Error.Validation("tasks.import.refused", "The backlog refused the plan");

        var outcome = await ApplyAsync(
            new InboxTriagePlanDecision("Sync polish", [first.Id, second.Id], []),
            new InboxTriageArchiveDecision(later.Title, later.Id));

        Assert.Empty(outcome.Applied);
        Assert.NotNull(outcome.Failure);
        Assert.True(outcome.Failure.WholePlan);
        Assert.StartsWith("Couldn't make the plan \"Sync polish\": ", outcome.Failure.Sentence, StringComparison.Ordinal);
        Assert.EndsWith(" The decisions before it stay applied; the rest were not tried.", outcome.Failure.Sentence, StringComparison.Ordinal);
        Assert.Equal(1, outcome.NotTried);
        Assert.Equal(InboxStatus.Unprocessed, later.Status);
    }

    [Fact]
    public async Task A_decision_on_an_item_already_decided_is_the_failure()
    {
        var item = Seed("Gone already");
        item.Archive(Items.Noon);

        var outcome = await ApplyAsync(new InboxTriageArchiveDecision(item.Title, item.Id));

        Assert.Empty(outcome.Applied);
        Assert.NotNull(outcome.Failure);
        Assert.StartsWith("Couldn't archive \"Gone already\": ", outcome.Failure.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_thrown_exception_becomes_the_failure()
    {
        var item = Seed("Throws");
        _store.FailOnSave = 1;

        var outcome = await ApplyAsync(new InboxTriageArchiveDecision(item.Title, item.Id));

        Assert.Empty(outcome.Applied);
        Assert.NotNull(outcome.Failure);
        Assert.Equal(item.Id, outcome.Failure.ItemId);
        Assert.Contains("The disk is full.", outcome.Failure.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_list_applies_nothing()
    {
        var outcome = await ApplyAsync();

        Assert.Empty(outcome.Applied);
        Assert.Null(outcome.Failure);
        Assert.Equal(0, outcome.NotTried);
        Assert.Empty(_target.BatchRequests);
        Assert.Empty(_target.Requests);
        Assert.Empty(_store.ItemWrites);
    }

    private Task<InboxTriagePassOutcome> ApplyAsync(params InboxTriageDecision[] decisions) =>
        InboxTriagePassApply.ApplyAsync(Inbox, decisions, cancellationToken: TestContext.Current.CancellationToken);

    private InboxItem Seed(string title, IReadOnlyList<string>? repoIds = null)
    {
        var item = Items.Manual(title);
        if (repoIds is not null) item.SetRepoIds(repoIds);
        _store.Seed(item);
        return item;
    }
}
