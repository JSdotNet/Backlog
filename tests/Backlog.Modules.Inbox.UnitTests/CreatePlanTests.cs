using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.CreatePlan;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// Create plan is routing through a drafter: the drafter is asked, the answer
/// is checked for the one thing that makes it a plan, Tasks' import is handed
/// it, and the item records what came back. Every refusal before the import
/// leaves Tasks unasked.
/// </summary>
public sealed class CreatePlanTests
{
    private static readonly DateTimeOffset Decision = Items.Noon.AddHours(1);

    [Fact]
    public async Task Without_a_drafter_the_plan_is_not_configured_and_tasks_is_not_asked()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var item = Items.Manual();
        store.Seed(item);

        var result = await Create(store, target, drafter: null, item.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("inbox.plan.not_configured", result.Error.Code);
        Assert.Empty(target.Imports);
        Assert.False(item.IsRouted);
    }

    [Fact]
    public async Task A_drafter_that_is_not_available_gives_its_own_reason()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var drafter = new FakePlanDrafter { IsAvailable = false, UnavailableReason = "Configure Azure Foundry in Settings." };
        var item = Items.Manual();
        store.Seed(item);

        var result = await Create(store, target, drafter, item.Id);

        Assert.Equal("inbox.plan.not_configured", result.Error.Code);
        Assert.Equal("Configure Azure Foundry in Settings.", result.Error.Message);
        Assert.Empty(drafter.Requests);
        Assert.Empty(target.Imports);
    }

    [Fact]
    public async Task An_answer_without_an_entry_heading_is_not_a_plan()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var drafter = new FakePlanDrafter { Answer = "I would suggest breaking this into three steps." };
        var item = Items.Manual();
        store.Seed(item);

        var result = await Create(store, target, drafter, item.Id);

        Assert.Equal("inbox.plan.empty", result.Error.Code);
        Assert.Empty(target.Imports);
        Assert.False(item.IsRouted);
        Assert.Empty(store.ItemWrites);
    }

    [Fact]
    public async Task A_drafter_failure_comes_back_as_it_was()
    {
        var store = new InMemoryInboxStore();
        var drafter = new FakePlanDrafter { FailWith = InboxErrors.PlanFailed("503 from the endpoint") };
        var item = Items.Manual();
        store.Seed(item);

        var result = await Create(store, new FakeBacklogTarget(), drafter, item.Id);

        Assert.Equal("inbox.plan.failed", result.Error.Code);
        Assert.False(item.IsRouted);
    }

    [Fact]
    public async Task A_plan_is_imported_with_the_items_id_and_the_item_is_routed()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var drafter = new FakePlanDrafter
        {
            Answer = "# Step one\n`prompt` `!ready` `#ship-the-thing`\n\nDo it.\n\n# Step two\n`prompt` `!ready` `#ship-the-thing`\n\nThen this.\n"
        };
        var item = Items.Manual("Ship the thing!");
        item.SetRepoIds(["jsdotnet/backlog"]);
        item.SetTags([new InboxTag("deploy", false)]);
        store.Seed(item);

        var result = await Create(store, target, drafter, item.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TaskIds.Count);

        var request = Assert.Single(drafter.Requests);
        Assert.Equal(CreatePlanCommandHandler.PlanTag("Ship the thing!", item.Id), request.PlanTag);
        Assert.StartsWith("ship-the-thing-", request.PlanTag, StringComparison.Ordinal);
        Assert.Equal("text", request.KindSlug);
        Assert.Equal(["jsdotnet/backlog"], request.RepoIds);
        Assert.Equal(["deploy"], request.Tags);

        var import = Assert.Single(target.Imports);
        Assert.Equal(drafter.Answer, import.Plan);
        Assert.Equal(item.Id, import.SourceInboxId);

        Assert.Equal(InboxStatus.Triaged, item.Status);
        Assert.Equal(result.Value.TaskIds, item.Routing!.TaskIds);
        Assert.Equal(Decision, item.Routing.RoutedAt);
    }

    [Fact]
    public async Task Tasks_own_import_errors_are_passed_back_unchanged()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget { FailWith = Error.Validation("import.duplicate_item_id", "twice") };
        var item = Items.Manual();
        store.Seed(item);

        var result = await Create(store, target, new FakePlanDrafter(), item.Id);

        Assert.Equal("import.duplicate_item_id", result.Error.Code);
        Assert.False(item.IsRouted);
    }

    /// <summary>The tag is the plan's id on the Tasks side (ADR 0007's
    /// <c>import_plan_id</c>), and a re-import clears every not-started entry
    /// under it. Two items with one title must therefore not share one, or the
    /// second "Create plan" would tombstone the first item's entries. The
    /// in-memory target cannot express the clearing itself; what it can show
    /// is that the two requests, and the two plans handed to Tasks, carry
    /// different tags.</summary>
    [Fact]
    public async Task Two_items_with_the_same_title_get_different_plan_tags()
    {
        var store = new InMemoryInboxStore();
        var target = new FakeBacklogTarget();
        var drafter = new FakePlanDrafter { Answer = "# Step one\n`prompt` `!draft` `#{tag}`\n\nDo it.\n" };
        var first = Items.Manual("Ship the thing");
        var second = Items.Manual("Ship the thing");
        store.Seed(first);
        store.Seed(second);

        Assert.True((await Create(store, target, drafter, first.Id)).IsSuccess);
        Assert.True((await Create(store, target, drafter, second.Id)).IsSuccess);

        Assert.Equal(2, drafter.Requests.Count);
        Assert.NotEqual(drafter.Requests[0].PlanTag, drafter.Requests[1].PlanTag);
        Assert.All(drafter.Requests, request => Assert.StartsWith("ship-the-thing-", request.PlanTag, StringComparison.Ordinal));

        Assert.Equal(2, target.Imports.Count);
        Assert.Contains($"`#{drafter.Requests[0].PlanTag}`", target.Imports[0].Plan, StringComparison.Ordinal);
        Assert.Contains($"`#{drafter.Requests[1].PlanTag}`", target.Imports[1].Plan, StringComparison.Ordinal);
        Assert.NotEqual(target.Imports[0].Plan, target.Imports[1].Plan);
    }

    [Theory]
    [InlineData("Ship the thing!", "ship-the-thing")]
    [InlineData("  Local-first  sync patterns ", "local-first-sync-patterns")]
    [InlineData("2026 roadmap", "plan-2026-roadmap")]
    [InlineData("!!!", "inbox-plan")]
    [InlineData("A title that goes on for considerably longer than forty characters", "a-title-that-goes-on-for-consid")]
    public void The_plan_tag_is_a_legal_tag_made_from_the_title_and_the_items_id(string title, string expected)
    {
        var id = Guid.Parse("0199a3f2-7c41-7d1a-9d0f-3d2c5e6f7a8b");

        var tag = CreatePlanCommandHandler.PlanTag(title, id);

        Assert.Equal(expected + "-5e6f7a8b", tag);
        Assert.True(tag.Length <= 40);
        Assert.True(char.IsAsciiLetter(tag[0]));
        Assert.Matches(@"^[A-Za-z][\w-]*$", tag);
    }

    /// <summary>The id's random tail, not its head: a version-7 guid opens with
    /// a millisecond timestamp whose first eight hex digits change about once a
    /// minute, so two captures in the same minute would share them and the
    /// suffix would not be a suffix.</summary>
    [Fact]
    public void Items_captured_in_the_same_minute_still_get_different_plan_tags()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        Assert.NotEqual(
            CreatePlanCommandHandler.PlanTag("Ship the thing", first),
            CreatePlanCommandHandler.PlanTag("Ship the thing", second));
    }

    private static Task<Result<InboxRoutedDto>> Create(
        InMemoryInboxStore store,
        FakeBacklogTarget target,
        IInboxPlanDrafter? drafter,
        Guid id) =>
        new CreatePlanCommandHandler(store, target, new FakeTimeProvider(Decision), drafter)
            .Handle(new CreatePlanCommand(id), TestContext.Current.CancellationToken);
}
