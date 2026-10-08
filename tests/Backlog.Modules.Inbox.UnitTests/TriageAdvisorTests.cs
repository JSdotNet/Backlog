using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.Modules.Inbox.Features.AdviseTriage;
using Backlog.Modules.Inbox.Features.ProposeTriagePass;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// The AI triage of local ADR 0023, as the module asks it: only of an advisor
/// that can run, with exactly the context §1 allows — the open tasks, the
/// other unprocessed items, the configured repositories and the lists — and
/// with the answer held to that context, so nothing the reader is shown names
/// what they cannot find.
/// </summary>
public sealed class TriageAdvisorTests
{
    private static readonly string[] Repositories = ["acme/web", "acme/api"];

    // --- The cards ----------------------------------------------------------------

    [Fact]
    public async Task Without_an_advisor_the_cards_are_not_configured()
    {
        var (store, item, _) = Inbox();

        var result = await Advise(store, advisor: null, item.Id);

        Assert.Equal("inbox.triage.not_configured", result.Error.Code);
    }

    [Fact]
    public async Task An_advisor_that_cannot_run_is_not_asked()
    {
        var (store, item, _) = Inbox();
        var advisor = new FakeTriageAdvisor { IsAvailable = false };

        var result = await Advise(store, advisor, item.Id);

        Assert.Equal("inbox.triage.not_configured", result.Error.Code);
        Assert.Empty(advisor.AdviceRequests);
    }

    [Fact]
    public async Task An_unknown_item_is_not_found_and_a_decided_one_is_refused()
    {
        var (store, _, _) = Inbox();
        var decided = Items.Manual("Already archived");
        decided.Archive(Items.Noon);
        store.Seed(decided);
        var advisor = new FakeTriageAdvisor();

        Assert.Equal(InboxErrors.ItemNotFound.Code, (await Advise(store, advisor, Guid.NewGuid())).Error.Code);
        Assert.Equal(InboxErrors.ItemAlreadyDecided.Code, (await Advise(store, advisor, decided.Id)).Error.Code);
        Assert.Empty(advisor.AdviceRequests);
    }

    [Fact]
    public async Task The_context_carries_the_open_tasks_the_other_unprocessed_items_the_repositories_and_the_lists()
    {
        var (store, item, other) = Inbox();
        var deferred = Items.Manual("Deferred one");
        deferred.Defer(null, Items.Noon);
        store.Seed(deferred);
        var list = InboxList.Create("Reading", null, 0, Items.Noon);
        store.Seed(list);
        var open = Task("Fix the login page", isOpen: true, status: "ready");
        var done = Task("Ship the old thing", isOpen: false, status: "done");
        var advisor = new FakeTriageAdvisor();

        await Advise(store, advisor, item.Id, new FakeTaskReferences(open, done));

        var (asked, context) = Assert.Single(advisor.AdviceRequests);
        Assert.Equal(item.Id, asked.Id);
        Assert.Equal(item.Title, asked.Title);
        var task = Assert.Single(context.OpenTasks);
        Assert.Equal((open.Id, "Fix the login page", "ready"), (task.Id, task.Title, task.Status));
        Assert.Equal([other.Id], context.OtherItems.Select(o => o.Id));
        Assert.Equal(Repositories, context.Repositories);
        Assert.Equal((list.Id, "Reading"), (Assert.Single(context.Lists).Id, context.Lists[0].Name));
    }

    [Fact]
    public async Task A_duplicate_of_an_open_task_comes_back_under_the_tasks_own_title()
    {
        var (store, item, _) = Inbox();
        var open = Task("Fix the login page", isOpen: true);
        var advisor = new FakeTriageAdvisor
        {
            Advice = new InboxTriageAdviceDto(
                Guid.Empty,
                new InboxTriageDuplicateDto(InboxTriageTargetKind.Task, open.Id, "whatever the model said", "Same fix."),
                null,
                ["ACME/web", "evil/repo"]),
        };

        var advice = (await Advise(store, advisor, item.Id, new FakeTaskReferences(open))).Value;

        Assert.Equal(item.Id, advice.ItemId);
        Assert.Equal("Fix the login page", advice.Duplicate?.TargetTitle);
        Assert.Equal(["acme/web"], advice.Repositories);
    }

    [Fact]
    public async Task A_duplicate_naming_the_item_itself_or_something_unknown_is_dropped()
    {
        var (store, item, _) = Inbox();

        foreach (var (kind, id) in new[]
        {
            (InboxTriageTargetKind.InboxItem, item.Id),
            (InboxTriageTargetKind.Task, Guid.NewGuid()),
            (InboxTriageTargetKind.InboxItem, Guid.NewGuid()),
        })
        {
            var advisor = new FakeTriageAdvisor
            {
                Advice = new InboxTriageAdviceDto(Guid.Empty, new InboxTriageDuplicateDto(kind, id, "x", "y"), null, []),
            };

            var advice = (await Advise(store, advisor, item.Id)).Value;

            Assert.Null(advice.Duplicate);
        }
    }

    [Fact]
    public async Task A_plan_keeps_the_known_members_with_the_item_first_and_needs_two()
    {
        var (store, item, other) = Inbox();
        var advisor = new FakeTriageAdvisor
        {
            Advice = new InboxTriageAdviceDto(Guid.Empty, null, new InboxTriagePlanGroupingDto("Login work", [other.Id, Guid.NewGuid()], "Both are login."), []),
        };

        var advice = (await Advise(store, advisor, item.Id)).Value;

        Assert.Equal([item.Id, other.Id], advice.Plan?.ItemIds);

        advisor.Advice = new InboxTriageAdviceDto(Guid.Empty, null, new InboxTriagePlanGroupingDto("Alone", [Guid.NewGuid()], "No."), []);
        Assert.Null((await Advise(store, advisor, item.Id)).Value.Plan);
    }

    [Fact]
    public async Task A_failed_advisor_answer_is_passed_through()
    {
        var (store, item, _) = Inbox();
        var advisor = new FakeTriageAdvisor { FailWith = InboxErrors.TriageFailed("Azure Foundry returned 503: busy") };

        var result = await Advise(store, advisor, item.Id);

        Assert.Equal("inbox.triage.failed", result.Error.Code);
        Assert.Equal("Azure Foundry returned 503: busy", result.Error.Message);
    }

    // --- The pass -----------------------------------------------------------------

    [Fact]
    public async Task Without_an_advisor_the_pass_is_not_configured()
    {
        var (store, item, _) = Inbox();

        var result = await Pass(store, advisor: null, [item.Id]);

        Assert.Equal("inbox.triage.not_configured", result.Error.Code);
    }

    [Fact]
    public async Task The_pass_asks_about_the_unprocessed_items_only_and_compares_with_the_rest()
    {
        var (store, item, other) = Inbox();
        var third = Items.Manual("Third capture");
        store.Seed(third);
        var decided = Items.Manual("Decided");
        decided.Archive(Items.Noon);
        store.Seed(decided);
        var advisor = new FakeTriageAdvisor();

        await Pass(store, advisor, [item.Id, decided.Id, Guid.NewGuid(), other.Id]);

        var (items, context) = Assert.Single(advisor.PassRequests);
        Assert.Equal([item.Id, other.Id], items.Select(i => i.Id));
        Assert.Equal([third.Id], context.OtherItems.Select(o => o.Id));
    }

    [Fact]
    public async Task A_slice_with_nothing_unprocessed_is_an_empty_pass_without_a_call()
    {
        var (store, _, _) = Inbox();
        var decided = Items.Manual("Decided");
        decided.Archive(Items.Noon);
        store.Seed(decided);
        var advisor = new FakeTriageAdvisor();

        var result = await Pass(store, advisor, [decided.Id]);

        Assert.Same(InboxTriagePassDto.Nothing, result.Value);
        Assert.Empty(advisor.PassRequests);
    }

    [Fact]
    public async Task The_pass_is_held_to_its_items_and_places_each_item_once()
    {
        var (store, item, other) = Inbox();
        var third = Items.Manual("Third capture");
        var fourth = Items.Manual("Fourth capture");
        var fifth = Items.Manual("Fifth capture");
        store.Seed(third);
        store.Seed(fourth);
        store.Seed(fifth);
        var list = InboxList.Create("Reading", null, 0, Items.Noon);
        store.Seed(list);
        var open = Task("Fix the login page", isOpen: true);
        var stranger = Guid.NewGuid();
        var advisor = new FakeTriageAdvisor
        {
            Pass = new InboxTriagePassDto(
                Plans:
                [
                    new("Login", [item.Id, other.Id, stranger], ["acme/web", "evil/repo"], "Both login.", 1.7),
                    new("Too small", [third.Id], [], "One item.", 0.9),
                ],
                Duplicates:
                [
                    new(item.Id, InboxTriageTargetKind.Task, open.Id, InboxDuplicateAction.MergeIntoTask, "Already planned.", 0.9),
                    new(third.Id, InboxTriageTargetKind.InboxItem, fourth.Id, InboxDuplicateAction.MergeIntoTask, "Not a task.", 0.9),
                    new(third.Id, InboxTriageTargetKind.Task, open.Id, InboxDuplicateAction.MergeIntoTask, "Same.", 0.8),
                ],
                Routes:
                [
                    new(fourth.Id, "Prompt", ["acme/api"], "Code.", 0.5),
                    new(fifth.Id, "epic", [], "Unknown type.", 0.9),
                ],
                Filings: [new(fifth.Id, Guid.NewGuid(), "No such list.", 0.9)],
                Archives: [new(stranger, "Not in the pass.", 0.9)],
                Unplaced: []),
        };

        var pass = (await Pass(store, advisor, [item.Id, other.Id, third.Id, fourth.Id, fifth.Id], new FakeTaskReferences(open))).Value;

        var plan = Assert.Single(pass.Plans);
        Assert.Equal([item.Id, other.Id], plan.ItemIds);
        Assert.Equal(["acme/web"], plan.Repositories);
        Assert.Equal(1, plan.Confidence);

        Assert.Equal(third.Id, Assert.Single(pass.Duplicates).ItemId);

        var route = Assert.Single(pass.Routes);
        Assert.Equal((fourth.Id, "prompt"), (route.ItemId, route.EntryType));

        Assert.Empty(pass.Filings);
        Assert.Empty(pass.Archives);
        Assert.Equal([fifth.Id], pass.Unplaced);
    }

    [Fact]
    public async Task A_pair_whose_other_half_is_already_in_a_plan_is_dropped()
    {
        var (store, item, other) = Inbox();
        var third = Items.Manual("Third capture");
        store.Seed(third);
        var advisor = new FakeTriageAdvisor
        {
            Pass = InboxTriagePassDto.Nothing with
            {
                Plans = [new("Login", [item.Id, other.Id], [], "Both login.", 0.9)],
                Duplicates = [new(third.Id, InboxTriageTargetKind.InboxItem, item.Id, InboxDuplicateAction.KeepOneAttachOther, "Same.", 0.9)],
            },
        };

        var pass = (await Pass(store, advisor, [item.Id, other.Id, third.Id])).Value;

        Assert.Single(pass.Plans);
        Assert.Empty(pass.Duplicates);
        Assert.Equal([third.Id], pass.Unplaced);
    }

    [Fact]
    public async Task A_failed_pass_is_passed_through()
    {
        var (store, item, _) = Inbox();
        var advisor = new FakeTriageAdvisor { FailWith = InboxErrors.TriageFailed("timed out") };

        var result = await Pass(store, advisor, [item.Id]);

        Assert.Equal("inbox.triage.failed", result.Error.Code);
    }

    private static (InMemoryInboxStore Store, InboxItem Item, InboxItem Other) Inbox()
    {
        var store = new InMemoryInboxStore();
        var item = Items.Manual("Login page returns a server error");
        var other = Items.Manual("Login button misaligned");
        store.Seed(item);
        store.Seed(other);
        return (store, item, other);
    }

    private static InboxTaskReferenceDto Task(string title, bool isOpen, string? status = null) =>
        new(Guid.NewGuid(), null, title, ["acme/web"], [], [], IsOpen: isOpen, Status: status);

    private static Task<Result<InboxTriageAdviceDto>> Advise(
        InMemoryInboxStore store,
        IInboxTriageAdvisor? advisor,
        Guid id,
        IInboxTaskReferences? tasks = null) =>
        new AdviseTriageQueryHandler(store, store, tasks, advisor)
            .Handle(new AdviseTriageQuery(id, Repositories), TestContext.Current.CancellationToken);

    private static Task<Result<InboxTriagePassDto>> Pass(
        InMemoryInboxStore store,
        IInboxTriageAdvisor? advisor,
        IReadOnlyList<Guid> ids,
        IInboxTaskReferences? tasks = null) =>
        new ProposeTriagePassQueryHandler(store, store, tasks, advisor)
            .Handle(new ProposeTriagePassQuery(ids, Repositories), TestContext.Current.CancellationToken);
}

/// <summary>A triage advisor that answers whatever the test set, and
/// remembers what it was asked.</summary>
internal sealed class FakeTriageAdvisor : IInboxTriageAdvisor
{
    public bool IsAvailable { get; set; } = true;

    public InboxTriageAdviceDto? Advice { get; set; }

    public InboxTriagePassDto Pass { get; set; } = InboxTriagePassDto.Nothing;

    public Error? FailWith { get; set; }

    public List<(InboxTriageItemDto Item, InboxTriageContextDto Context)> AdviceRequests { get; } = [];

    public List<(IReadOnlyList<InboxTriageItemDto> Items, InboxTriageContextDto Context)> PassRequests { get; } = [];

    public Task<Result<InboxTriageAdviceDto>> AdviseAsync(InboxTriageItemDto item, InboxTriageContextDto context, CancellationToken cancellationToken = default)
    {
        AdviceRequests.Add((item, context));

        return Task.FromResult(FailWith is { } error
            ? Result.Failure<InboxTriageAdviceDto>(error)
            : Result.Success(Advice ?? InboxTriageAdviceDto.None(item.Id)));
    }

    public Task<Result<InboxTriagePassDto>> ProposePassAsync(IReadOnlyList<InboxTriageItemDto> items, InboxTriageContextDto context, CancellationToken cancellationToken = default)
    {
        PassRequests.Add((items, context));

        return Task.FromResult(FailWith is { } error
            ? Result.Failure<InboxTriagePassDto>(error)
            : Result.Success(Pass));
    }
}
