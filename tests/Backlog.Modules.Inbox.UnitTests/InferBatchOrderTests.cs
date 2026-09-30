using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.CreatePlan;
using Backlog.Modules.Inbox.Features.InferBatchOrder;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// "Ask the AI to order": the drafter is asked about the batch as a whole, its
/// answer is read by the backlog target — never by the module — and what comes
/// back is proposals of the third tier, between two items of the batch. Nothing
/// is routed or written, whatever the answer.
/// </summary>
public sealed class InferBatchOrderTests
{
    private const string Tag = "+inbox-batch-1a2b3c4d";

    [Fact]
    public async Task Without_a_drafter_the_order_is_not_configured_and_nothing_is_read()
    {
        var (store, first, second) = Batch();
        var target = new FakeBacklogTarget();

        var result = await Infer(store, target, drafter: null, [first.Id, second.Id]);

        Assert.Equal("inbox.plan.not_configured", result.Error.Code);
        Assert.Empty(target.OrderReads);
    }

    [Fact]
    public async Task A_drafter_that_is_not_available_gives_its_own_reason()
    {
        var (store, first, second) = Batch();
        var drafter = new FakePlanDrafter { IsAvailable = false, UnavailableReason = "Configure Azure Foundry in Settings to create plans." };

        var result = await Infer(store, new FakeBacklogTarget(), drafter, [first.Id, second.Id]);

        Assert.Equal("inbox.plan.not_configured", result.Error.Code);
        Assert.Equal("Configure Azure Foundry in Settings to create plans.", result.Error.Message);
        Assert.Empty(drafter.Requests);
    }

    [Fact]
    public async Task The_batch_is_carried_in_the_draft_request_with_each_item_and_the_bare_tag()
    {
        var (store, first, second) = Batch();
        var drafter = new FakePlanDrafter();

        await Infer(store, new FakeBacklogTarget(), drafter, [first.Id, second.Id]);

        var request = Assert.Single(drafter.Requests);
        Assert.Equal(Guid.Empty, request.InboxItemId);
        Assert.Equal("inbox-batch-1a2b3c4d", request.PlanTag);
        Assert.NotNull(request.Batch);
        Assert.Equal([first.Id, second.Id], request.Batch.Select(item => item.InboxItemId));
        Assert.Equal("Deploy the preview", request.Batch[1].Title);
        Assert.Equal("Once the pipeline exists.", request.Batch[1].BodyMd);
        Assert.Equal(["a/one"], request.Batch[0].RepoIds);
        Assert.Equal(["a/one"], request.RepoIds);
    }

    [Fact]
    public async Task The_panels_repositories_are_the_ones_the_answer_may_name()
    {
        var (store, first, second) = Batch();
        var drafter = new FakePlanDrafter();
        var target = new FakeBacklogTarget();
        var chosen = new Dictionary<Guid, IReadOnlyList<string>> { [second.Id] = ["b/two", "a/one"] };

        await Infer(store, target, drafter, [first.Id, second.Id], chosen);

        Assert.Equal(["b/two", "a/one"], Assert.Single(drafter.Requests).Batch![1].RepoIds);
        var read = Assert.Single(target.OrderReads);
        Assert.Equal(["a/one", "b/two"], read.AllowedRepoIds);
        Assert.Equal([first.Id, second.Id], read.ItemIds);
        Assert.Equal(drafter.Answer, read.Plan);
    }

    [Fact]
    public async Task Each_edge_read_back_is_an_inferred_dependency_on_the_other_item()
    {
        var (store, first, second) = Batch();
        var target = new FakeBacklogTarget
        {
            DraftedOrder = Result.Success<IReadOnlyList<InboxBatchEdge>>([new InboxBatchEdge(second.Id, first.Id)]),
        };

        var result = await Infer(store, target, new FakePlanDrafter(), [first.Id, second.Id]);

        var dependency = Assert.Single(result.Value);
        Assert.Equal(second.Id, dependency.From);
        Assert.Equal(DependencyTargetKind.Item, dependency.To.Kind);
        Assert.Equal(first.Id, dependency.To.Id);
        Assert.Equal("Set up the pipeline", dependency.To.Title);
        Assert.Equal(DependencyTier.Inferred, dependency.Tier);
        Assert.Equal(InferBatchOrderQueryHandler.Reason, dependency.Reason);
        Assert.Empty(store.ItemWrites);
    }

    [Fact]
    public async Task An_answer_the_target_refuses_adds_nothing()
    {
        var (store, first, second) = Batch();
        var target = new FakeBacklogTarget
        {
            DraftedOrder = Result.Failure<IReadOnlyList<InboxBatchEdge>>(InboxErrors.OrderUnknownRepository("made/up")),
        };

        var result = await Infer(store, target, new FakePlanDrafter(), [first.Id, second.Id]);

        Assert.Equal("inbox.order.unknown_repository", result.Error.Code);
        Assert.Contains("made/up", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_drafter_failure_comes_back_as_it_is()
    {
        var (store, first, second) = Batch();
        var target = new FakeBacklogTarget();
        var drafter = new FakePlanDrafter { FailWith = InboxErrors.PlanFailed("Azure Foundry did not answer before the request timed out.") };

        var result = await Infer(store, target, drafter, [first.Id, second.Id]);

        Assert.Equal("inbox.plan.failed", result.Error.Code);
        Assert.Empty(target.OrderReads);
    }

    [Fact]
    public async Task One_routable_item_has_nothing_to_order_and_the_drafter_is_not_asked()
    {
        var (store, first, _) = Batch();
        var routed = Items.Manual("Already routed");
        routed.RouteToBacklog([Guid.CreateVersion7()], [], Items.Noon);
        store.Seed(routed);
        var drafter = new FakePlanDrafter();

        var result = await Infer(store, new FakeBacklogTarget(), drafter, [first.Id, routed.Id]);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
        Assert.Empty(drafter.Requests);
    }

    [Fact]
    public async Task Create_plan_still_asks_about_one_item_with_no_batch()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual();
        store.Seed(item);
        var drafter = new FakePlanDrafter();

        await new CreatePlanCommandHandler(store, new FakeBacklogTarget(), new FakeTimeProvider(Items.Noon), drafter)
            .Handle(new CreatePlanCommand(item.Id), TestContext.Current.CancellationToken);

        var request = Assert.Single(drafter.Requests);
        Assert.Equal(item.Id, request.InboxItemId);
        Assert.Null(request.Batch);
    }

    private static (InMemoryInboxStore Store, InboxItem First, InboxItem Second) Batch()
    {
        var store = new InMemoryInboxStore();
        var first = new InboxItem(Guid.CreateVersion7(), "Set up the pipeline", string.Empty, null, Items.Noon, Items.Noon,
            ContentKind.Text, new InboxSource(InboxEnumMap.ManualChannel, null), replicaBacked: false);
        first.SetRepoIds(["a/one"]);
        var second = new InboxItem(Guid.CreateVersion7(), "Deploy the preview", "Once the pipeline exists.", null, Items.Noon, Items.Noon,
            ContentKind.Text, new InboxSource(InboxEnumMap.ManualChannel, null), replicaBacked: false);
        store.Seed(first);
        store.Seed(second);
        return (store, first, second);
    }

    private static Task<Result<IReadOnlyList<ProposedDependency>>> Infer(
        InMemoryInboxStore store,
        FakeBacklogTarget target,
        IInboxPlanDrafter? drafter,
        IReadOnlyList<Guid> ids,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>>? repositories = null) =>
        new InferBatchOrderQueryHandler(store, target, drafter)
            .Handle(new InferBatchOrderQuery(ids, Tag, repositories), TestContext.Current.CancellationToken);
}
