using Backlog.Desktop.UI.Inbox;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The AI pass under review, without a DOM: what the draft keeps of the pass,
/// what it counts, the decisions it hands Apply and in which order, and what is
/// left of it after an Apply that stopped part way.
/// </summary>
public sealed class InboxTriagePassDraftTests
{
    private readonly FakeInboxItems _inbox = new();

    [Fact]
    public void Items_no_longer_waiting_and_unknown_ids_are_dropped_and_a_plan_of_one_or_a_list_gone_leaves_the_item_to_the_reader()
    {
        var a = _inbox.Seed("A");
        var b = _inbox.Seed("B");
        var routed = _inbox.Seed("Routed", status: InboxStatus.Triaged);
        var deferred = _inbox.Seed("Deferred", status: InboxStatus.Deferred);
        var c = _inbox.Seed("C");
        var list = _inbox.SeedList("Reading");

        var pass = InboxTriagePassDto.Nothing with
        {
            Plans = [new("Half gone", [a.Id, routed.Id], [], "Why.", 0.9)],
            Routes = [new(deferred.Id, "task", [], "Why.", 0.9), new(Guid.NewGuid(), "task", [], "Why.", 0.9)],
            Filings = [new(b.Id, Guid.NewGuid(), "No such list.", 0.9), new(c.Id, list.Id, "Why.", 0.9)],
        };

        var draft = new InboxTriagePassDraft(pass, _inbox.Items, _inbox.Lists);

        Assert.Empty(draft.Plans);
        Assert.Empty(draft.Routes);
        Assert.Equal([c.Id], draft.Filings.Select(filing => filing.ItemId));
        Assert.Equal([a.Id, b.Id], draft.Unplaced);
        Assert.Equal(1, draft.Total);
    }

    [Fact]
    public void The_decisions_come_in_the_screens_order_with_the_choices_made()
    {
        var p1 = _inbox.Seed("P1");
        var p2 = _inbox.Seed("P2");
        var p3 = _inbox.Seed("P3");
        var dup = _inbox.Seed("Dup");
        var route = _inbox.Seed("Route");
        var filed = _inbox.Seed("Filed");
        var archived = _inbox.Seed("Archived");
        var list = _inbox.SeedList("Reading");
        var task = Guid.NewGuid();

        var draft = new InboxTriagePassDraft(
            new InboxTriagePassDto(
                Plans: [new("Plan", [p1.Id, p2.Id, p3.Id], ["acme/web"], "Why.", 0.9)],
                Duplicates: [new(dup.Id, InboxTriageTargetKind.Task, task, InboxDuplicateAction.MergeIntoTask, "Why.", 0.9)],
                Routes: [new(route.Id, "prompt", ["acme/api"], "Why.", 0.9)],
                Filings: [new(filed.Id, list.Id, "Why.", 0.9)],
                Archives: [new(archived.Id, "Why.", 0.9)],
                Unplaced: []),
            _inbox.Items,
            _inbox.Lists);

        draft.Plans[0].Title = "  Renamed  ";
        draft.Plans[0].SetLeftOut(p2.Id, true);
        draft.Duplicates[0].Choice = InboxPassDuplicateChoice.Archive;

        var decisions = draft.Decisions();

        Assert.Equal(5, draft.DecisionCount);
        Assert.Collection(
            decisions,
            decision =>
            {
                var plan = Assert.IsType<InboxTriagePlanDecision>(decision);
                Assert.Equal("Renamed", plan.Title);
                Assert.Equal([p1.Id, p3.Id], plan.ItemIds);
                Assert.Equal(["acme/web"], plan.Repositories);
            },
            decision => Assert.Equal(new InboxTriageDuplicateDecision("Dup", dup.Id, InboxTriageTargetKind.Task, task, InboxTriageDuplicateChoice.Archive), decision),
            decision => Assert.Equal(route.Id, Assert.IsType<InboxTriageRouteDecision>(decision).ItemId),
            decision => Assert.Equal(new InboxTriageFilingDecision("Filed", filed.Id, list.Id), decision),
            decision => Assert.Equal(new InboxTriageArchiveDecision("Archived", archived.Id), decision));
    }

    [Fact]
    public void Removing_what_applied_matches_the_plan_by_identity_hands_its_waiting_members_to_the_reader_and_drops_what_no_longer_waits()
    {
        var p1 = _inbox.Seed("P1");
        var p2 = _inbox.Seed("P2");
        var p3 = _inbox.Seed("P3");
        var twin1 = _inbox.Seed("Twin 1");
        var twin2 = _inbox.Seed("Twin 2");
        var archived = _inbox.Seed("Archived");
        var routed = _inbox.Seed("Routed elsewhere");
        var draft = new InboxTriagePassDraft(
            InboxTriagePassDto.Nothing with
            {
                // Two plans that end up with the same members once both are
                // edited down: only the one Apply ran comes off.
                Plans =
                [
                    new("Plan", [p1.Id, p2.Id, p3.Id], [], "Why.", 0.9),
                    new("Other", [twin1.Id, twin2.Id], [], "Why.", 0.3),
                ],
                Archives = [new(archived.Id, "Why.", 0.9), new(routed.Id, "Why.", 0.9)],
            },
            _inbox.Items,
            _inbox.Lists);
        draft.Plans[0].SetLeftOut(p3.Id, true);
        var plan = draft.Decisions()[0];

        // After the reload: the plan's two members routed, and the item of one
        // archive proposal routed by some other hand meanwhile.
        IReadOnlyList<InboxItemDto> after =
            [.. _inbox.Items.Select(item => item.Id == p1.Id || item.Id == p2.Id || item.Id == routed.Id ? item with { Status = InboxStatus.Triaged } : item)];
        draft.Remove([plan], after);

        Assert.Equal(["Other"], draft.Plans.Select(left => left.Title));
        Assert.Equal([archived.Id], draft.Archives.Select(archive => archive.ItemId));
        Assert.Equal([p3.Id], draft.Unplaced);
    }

    [Fact]
    public void A_filing_into_the_list_the_item_already_sits_in_is_no_decision_and_the_item_is_the_readers()
    {
        var list = _inbox.SeedList("Reading");
        var there = _inbox.Seed("Already there", listId: list.Id);

        var draft = new InboxTriagePassDraft(
            InboxTriagePassDto.Nothing with { Filings = [new(there.Id, list.Id, "Why.", 0.9)] },
            _inbox.Items,
            _inbox.Lists);

        Assert.Empty(draft.Filings);
        Assert.Equal([there.Id], draft.Unplaced);
    }

    [Fact]
    public void An_item_an_undo_brought_back_into_the_slice_is_left_for_the_reader()
    {
        var archived = _inbox.Seed("Archive me");
        var back = _inbox.Seed("Came back", status: InboxStatus.Archived);
        var draft = new InboxTriagePassDraft(
            InboxTriagePassDto.Nothing with { Archives = [new(archived.Id, "Why.", 0.9)] },
            _inbox.Items,
            _inbox.Lists);

        IReadOnlyList<InboxItemDto> after = [.. _inbox.Items.Select(item => item.Id == back.Id ? item with { Status = InboxStatus.Unprocessed } : item)];
        draft.Prune(after, after);

        Assert.Single(draft.Archives);
        Assert.Equal([back.Id], draft.Unplaced);
    }
}
