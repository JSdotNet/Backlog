using Backlog.Desktop.WebHarness;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The harness's stand-in triage advisor: deterministic, so every AI triage
/// path can be reached without Azure — a duplicate for a title that shares
/// three words with an open task, one plan per shared tag, and one
/// low-confidence proposal in every pass.
/// </summary>
public sealed class FakeInboxTriageAdvisorTests
{
    private static readonly FakeInboxTriageAdvisor Advisor = new();

    [Fact]
    public void It_is_always_available()
    {
        Assert.True(Advisor.IsAvailable);
    }

    [Fact]
    public async Task A_title_sharing_three_words_with_an_open_task_is_its_duplicate()
    {
        var task = new InboxTriageTaskDto(Guid.NewGuid(), "Fix the login page redirect", ["acme/web"], "ready");
        var item = Item("Login page redirect loops forever");

        var advice = (await Advisor.AdviseAsync(item, Context(tasks: [task]), TestContext.Current.CancellationToken)).Value;

        Assert.NotNull(advice.Duplicate);
        Assert.Equal(InboxTriageTargetKind.Task, advice.Duplicate.TargetKind);
        Assert.Equal(task.Id, advice.Duplicate.TargetId);
        Assert.Equal(["acme/web"], advice.Repositories);
    }

    [Fact]
    public async Task Two_shared_words_are_not_enough_and_filler_does_not_count()
    {
        var task = new InboxTriageTaskDto(Guid.NewGuid(), "Fix the login page", [], "ready");
        var item = Item("The login page and the footer");

        var advice = (await Advisor.AdviseAsync(item, Context(tasks: [task]), TestContext.Current.CancellationToken)).Value;

        Assert.Null(advice.Duplicate);
    }

    [Fact]
    public async Task Failing_a_task_a_title_sharing_three_words_with_another_item_is_its_duplicate()
    {
        var other = Item("Export the weekly sales report");
        var item = Item("Weekly sales report export broken");

        var advice = (await Advisor.AdviseAsync(item, Context(others: [other]), TestContext.Current.CancellationToken)).Value;

        Assert.Equal(InboxTriageTargetKind.InboxItem, advice.Duplicate?.TargetKind);
        Assert.Equal(other.Id, advice.Duplicate?.TargetId);
    }

    [Fact]
    public async Task Items_sharing_a_tag_group_into_one_plan_holding_the_item()
    {
        var item = Item("Draft the onboarding email", tags: ["onboarding"]);
        var sibling = Item("Record the welcome video", tags: ["Onboarding"]);
        var stranger = Item("Renew the domain", tags: ["ops"]);

        var advice = (await Advisor.AdviseAsync(item, Context(others: [sibling, stranger]), TestContext.Current.CancellationToken)).Value;

        Assert.NotNull(advice.Plan);
        Assert.Equal([item.Id, sibling.Id], advice.Plan.ItemIds);
    }

    [Fact]
    public async Task An_item_with_nothing_in_common_gets_no_cards()
    {
        var advice = (await Advisor.AdviseAsync(Item("Call the dentist"), Context(), TestContext.Current.CancellationToken)).Value;

        Assert.False(advice.HasCards);
    }

    [Fact]
    public async Task A_pass_places_every_kind_and_marks_exactly_one_proposal_low()
    {
        var task = new InboxTriageTaskDto(Guid.NewGuid(), "Fix the login page redirect", [], "ready");
        var list = new InboxTriageListDto(Guid.NewGuid(), "Reading");
        var duplicate = Item("Login page redirect loops");
        var planA = Item("Draft the onboarding email", tags: ["onboarding"]);
        var planB = Item("Record the welcome video", tags: ["onboarding"]);
        var filed = Item("An essay on queues", tags: ["reading"]);
        var prompt = Item("Add a dark theme", repoIds: ["acme/web"]);
        var personal = Item("Book the venue for the offsite", bodyMd: "Forty people.");
        var archived = Item("lunch?");
        var unplaced = Item("Think about the roadmap again");
        IReadOnlyList<InboxTriageItemDto> items = [duplicate, planA, planB, filed, prompt, personal, archived, unplaced];

        var pass = (await Advisor.ProposePassAsync(items, Context(tasks: [task], lists: [list]), TestContext.Current.CancellationToken)).Value;

        var pair = Assert.Single(pass.Duplicates);
        Assert.Equal((duplicate.Id, task.Id, InboxDuplicateAction.MergeIntoTask), (pair.ItemId, pair.TargetId, pair.Action));
        Assert.Equal([planA.Id, planB.Id], Assert.Single(pass.Plans).ItemIds);
        Assert.Equal(list.Id, Assert.Single(pass.Filings).ListId);
        Assert.Equal(
            [(prompt.Id, "prompt"), (personal.Id, "task")],
            pass.Routes.Select(route => (route.ItemId, route.EntryType)));
        Assert.Equal(archived.Id, Assert.Single(pass.Archives).ItemId);
        Assert.Equal([unplaced.Id], pass.Unplaced);

        var confidences = pass.Plans.Select(p => p.Confidence)
            .Concat(pass.Duplicates.Select(p => p.Confidence))
            .Concat(pass.Routes.Select(p => p.Confidence))
            .Concat(pass.Filings.Select(p => p.Confidence))
            .Concat(pass.Archives.Select(p => p.Confidence))
            .ToList();
        Assert.Single(confidences, confidence => confidence == FakeInboxTriageAdvisor.LowConfidence);
        Assert.Equal(FakeInboxTriageAdvisor.LowConfidence, pass.Routes[0].Confidence);
    }

    [Fact]
    public async Task A_pass_with_no_route_marks_its_first_proposal_low()
    {
        var planA = Item("Draft the onboarding email", tags: ["onboarding"]);
        var planB = Item("Record the welcome video", tags: ["onboarding"]);

        var pass = (await Advisor.ProposePassAsync([planA, planB], Context(), TestContext.Current.CancellationToken)).Value;

        Assert.Equal(FakeInboxTriageAdvisor.LowConfidence, Assert.Single(pass.Plans).Confidence);
    }

    [Fact]
    public async Task The_same_pass_twice_gives_the_same_answer()
    {
        var items = new[] { Item("Add a dark theme", repoIds: ["acme/web"]), Item("lunch?") };

        var first = (await Advisor.ProposePassAsync(items, Context(), TestContext.Current.CancellationToken)).Value;
        var second = (await Advisor.ProposePassAsync(items, Context(), TestContext.Current.CancellationToken)).Value;

        Assert.Equal(first.Routes, second.Routes);
        Assert.Equal(first.Archives, second.Archives);
    }

    private static InboxTriageItemDto Item(
        string title,
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<string>? repoIds = null,
        string bodyMd = "") =>
        new(Guid.NewGuid(), title, bodyMd, null, "text", "manual", null, tags ?? [], repoIds ?? []);

    private static InboxTriageContextDto Context(
        IReadOnlyList<InboxTriageTaskDto>? tasks = null,
        IReadOnlyList<InboxTriageItemDto>? others = null,
        IReadOnlyList<InboxTriageListDto>? lists = null) =>
        new(tasks ?? [], others ?? [], ["acme/web"], lists ?? []);
}
