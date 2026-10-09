using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

namespace Backlog.Desktop.UI.Inbox;

/// <summary>
/// The AI triage pass under review (features.md#ai-triage-pass, artboard D):
/// the advisor's proposals and what the reader made of them before pressing
/// Apply — which are accepted, what a duplicate pair is to become, a plan's
/// title and the members left out of it.
/// <para>
/// View state and its derivations, held by <see cref="InboxDesktopState"/>
/// rather than the screen for the reason <see cref="InboxRouteDraft"/> is: the
/// shell re-mounts the pane when it moves slots, and a review half-way through
/// would be lost with it. The counts, the order and the decisions Apply runs
/// are worked out here so they can be asserted without a DOM.
/// </para>
/// <para>
/// Built from the pass and the snapshot it was asked against, and held to every
/// snapshot after it (<see cref="Prune"/>): whatever is no longer waiting for a
/// decision — routed, archived, deferred or gone — loses its proposal, a list
/// that is gone loses its filings, and a filing into the list an item already
/// sits in is no decision at all. An item still waiting that no proposal covers
/// any more is left for the reader, so nothing on screen names what Apply could
/// not do and nothing waiting drops out of sight.
/// </para>
/// </summary>
public sealed class InboxTriagePassDraft
{
    private readonly Dictionary<Guid, InboxItemDto> _items;
    private readonly List<InboxPassPlan> _plans;
    private readonly List<InboxPassDuplicate> _duplicates;
    private readonly List<InboxPassProposal> _routes;
    private readonly List<InboxPassProposal> _filings;
    private readonly List<InboxPassProposal> _archives;
    private readonly List<Guid> _unplaced;
    private readonly Dictionary<Guid, string> _listNames;

    /// <summary>The plan each plan decision of the latest <see cref="Decisions"/>
    /// came from, by the decision itself, so an applied plan is taken off by
    /// identity — two plans can hold the same members once one is edited.</summary>
    private readonly Dictionary<InboxTriageDecision, InboxPassPlan> _issued = new(ReferenceEqualityComparer.Instance);

    public InboxTriagePassDraft(InboxTriagePassDto pass, IReadOnlyList<InboxItemDto> items, IReadOnlyList<InboxListDto> lists)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(lists);

        _items = items.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());
        _listNames = lists.GroupBy(list => list.Id).ToDictionary(group => group.Key, group => group.First().Name);

        _plans = [.. pass.Plans.Select(plan => new InboxPassPlan(plan, [.. plan.ItemIds.Distinct()]))];
        _duplicates = [.. pass.Duplicates.Select(pair => new InboxPassDuplicate(pair))];
        _routes = [.. pass.Routes.Select(route => new InboxPassProposal(route.ItemId, route.Reason, route.Confidence)
        {
            EntryType = route.EntryType,
            Repositories = route.Repositories,
        })];
        _filings = [.. pass.Filings.Select(filing => new InboxPassProposal(filing.ItemId, filing.Reason, filing.Confidence) { ListId = filing.ListId })];
        _archives = [.. pass.Archives.Select(archive => new InboxPassProposal(archive.ItemId, archive.Reason, archive.Confidence))];
        _unplaced = [.. pass.Unplaced.Distinct()];

        // The pass's own items are the ones it read; the ones it was not asked
        // about are not added to what is left for the reader.
        HoldTo(items, slice: null);
    }

    public IReadOnlyList<InboxPassPlan> Plans => _plans;

    public IReadOnlyList<InboxPassDuplicate> Duplicates => _duplicates;

    public IReadOnlyList<InboxPassProposal> Routes => _routes;

    public IReadOnlyList<InboxPassProposal> Filings => _filings;

    public IReadOnlyList<InboxPassProposal> Archives => _archives;

    /// <summary>The filings by list, the lists in the order the pass first
    /// names them — one "File n in list" row each.</summary>
    public IReadOnlyList<InboxPassFilingGroup> FilingGroups =>
        [.. _filings
            .GroupBy(filing => filing.ListId!.Value)
            .Select(group => new InboxPassFilingGroup(group.Key, _listNames[group.Key], [.. group]))];

    /// <summary>The items the pass could not place with confidence, and every
    /// item a proposal no longer covers, still waiting for the reader.</summary>
    public IReadOnlyList<Guid> Unplaced => _unplaced;

    /// <summary>Every proposal on screen; the items left for the reader are not
    /// proposals and are not counted.</summary>
    public int Total => _plans.Count + _duplicates.Count + _routes.Count + _filings.Count + _archives.Count;

    /// <summary>The proposals that will be applied: a plan accepted and still
    /// two items or more, a pair not left as both, and every other proposal
    /// accepted.</summary>
    public int AcceptedCount =>
        _plans.Count(plan => plan.IsAccepted)
        + _duplicates.Count(pair => pair.IsAccepted)
        + _routes.Count(route => route.Accepted)
        + _filings.Count(filing => filing.Accepted)
        + _archives.Count(archive => archive.Accepted);

    /// <summary>How many decisions Apply will run — one per accepted proposal.</summary>
    public int DecisionCount => AcceptedCount;

    /// <summary>Whether there is nothing at all to show: no proposal and no
    /// item left over.</summary>
    public bool IsEmpty => Total == 0 && _unplaced.Count == 0;

    /// <summary>An item as the latest snapshot the draft was held to has it.</summary>
    public InboxItemDto? Item(Guid id) => _items.GetValueOrDefault(id);

    /// <summary>What an item is called, for the screen and for a failure sentence.</summary>
    public string TitleOf(Guid id) =>
        _items.TryGetValue(id, out var item) && !string.IsNullOrWhiteSpace(item.Title) ? item.Title : "Untitled";

    public string? ListName(Guid listId) => _listNames.GetValueOrDefault(listId);

    /// <summary>Every item's title by id, for Apply's failure sentence.</summary>
    public IReadOnlyDictionary<Guid, string> Titles =>
        _items.Keys.ToDictionary(id => id, TitleOf);

    /// <summary>
    /// The accepted proposals as the decisions Apply runs, in the screen's
    /// order: plans, duplicates, single routes, filings by list, archives. A
    /// plan goes under the title as edited and without the members left out.
    /// </summary>
    public IReadOnlyList<InboxTriageDecision> Decisions()
    {
        var decisions = new List<InboxTriageDecision>();

        _issued.Clear();
        foreach (var plan in _plans.Where(plan => plan.IsAccepted))
        {
            var decision = new InboxTriagePlanDecision(plan.EffectiveTitle, plan.Kept, plan.Proposal.Repositories);
            _issued[decision] = plan;
            decisions.Add(decision);
        }

        decisions.AddRange(_duplicates
            .Where(pair => pair.IsAccepted)
            .Select(pair => new InboxTriageDuplicateDecision(
                TitleOf(pair.Proposal.ItemId),
                pair.Proposal.ItemId,
                pair.Proposal.TargetKind,
                pair.Proposal.TargetId,
                pair.Choice == InboxPassDuplicateChoice.Archive ? InboxTriageDuplicateChoice.Archive : InboxTriageDuplicateChoice.Merge)));

        decisions.AddRange(_routes
            .Where(route => route.Accepted)
            .Select(route => new InboxTriageRouteDecision(TitleOf(route.ItemId), route.ItemId, route.Repositories)));

        decisions.AddRange(FilingGroups
            .SelectMany(group => group.Items)
            .Where(filing => filing.Accepted)
            .Select(filing => new InboxTriageFilingDecision(TitleOf(filing.ItemId), filing.ItemId, filing.ListId!.Value)));

        decisions.AddRange(_archives
            .Where(archive => archive.Accepted)
            .Select(archive => new InboxTriageArchiveDecision(TitleOf(archive.ItemId), archive.ItemId)));

        return decisions;
    }

    /// <summary>
    /// Takes the proposals behind <paramref name="applied"/> off the draft
    /// after an Apply that stopped part way, so a second press runs only what
    /// is left, then holds the rest to <paramref name="items"/> as
    /// <see cref="Prune"/> does. A plan is matched by identity to the decision
    /// <see cref="Decisions"/> made for it; its members still waiting — the
    /// ones left out, and any the batch refused — are left for the reader.
    /// </summary>
    public void Remove(IEnumerable<InboxTriageDecision> applied, IReadOnlyList<InboxItemDto> items, IReadOnlyList<InboxItemDto>? slice = null)
    {
        ArgumentNullException.ThrowIfNull(applied);
        ArgumentNullException.ThrowIfNull(items);

        var done = new HashSet<Guid>();

        foreach (var decision in applied)
        {
            switch (decision)
            {
                case InboxTriagePlanDecision when _issued.TryGetValue(decision, out var plan):
                    _plans.Remove(plan);
                    _unplaced.AddRange(plan.Members.Where(id => !_unplaced.Contains(id)));
                    break;
                case InboxTriageDuplicateDecision pair:
                    done.Add(pair.ItemId);
                    break;
                case InboxTriageRouteDecision route:
                    done.Add(route.ItemId);
                    break;
                case InboxTriageFilingDecision filing:
                    done.Add(filing.ItemId);
                    break;
                case InboxTriageArchiveDecision archive:
                    done.Add(archive.ItemId);
                    break;
            }
        }

        _duplicates.RemoveAll(pair => done.Contains(pair.Proposal.ItemId));
        _routes.RemoveAll(route => done.Contains(route.ItemId));
        _filings.RemoveAll(filing => done.Contains(filing.ItemId));
        _archives.RemoveAll(archive => done.Contains(archive.ItemId));

        HoldTo(items, slice);
    }

    /// <summary>
    /// Holds the draft to a newer snapshot — after an Apply that stopped, or an
    /// undo taken while the review is open. Every proposal whose item is no
    /// longer waiting goes; a plan keeps the members still waiting and goes
    /// once it is down to one, that one left for the reader; a filing into the
    /// list its item already sits in goes. An item of <paramref name="slice"/>
    /// still waiting that no proposal covers — an undone decision brought it
    /// back — is left for the reader too.
    /// </summary>
    public void Prune(IReadOnlyList<InboxItemDto> items, IReadOnlyList<InboxItemDto>? slice = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        HoldTo(items, slice);
    }

    private void HoldTo(IReadOnlyList<InboxItemDto> items, IReadOnlyList<InboxItemDto>? slice)
    {
        foreach (var item in items) _items[item.Id] = item;
        var present = items.Select(item => item.Id).ToHashSet();
        foreach (var gone in _items.Keys.Where(id => !present.Contains(id)).ToList()) _items.Remove(gone);

        var orphans = new List<Guid>();

        foreach (var plan in _plans.ToList())
        {
            plan.KeepOnly(IsWaiting);

            // The module only proposes a plan of two or more; one that lost its
            // members to decisions since is no plan, and what is left of it is
            // the reader's to place.
            if (plan.Members.Count >= 2) continue;

            _plans.Remove(plan);
            orphans.AddRange(plan.Members);
        }

        // A pair whose other item has gone, or a filing whose list has, is
        // dropped; the item it was about is still waiting, and is the reader's.
        foreach (var pair in _duplicates.Where(pair => IsWaiting(pair.Proposal.ItemId)).ToList())
        {
            if (pair.Proposal.TargetKind == InboxTriageTargetKind.Task || _items.ContainsKey(pair.Proposal.TargetId)) continue;

            _duplicates.Remove(pair);
            orphans.Add(pair.Proposal.ItemId);
        }

        foreach (var filing in _filings.Where(filing => IsWaiting(filing.ItemId)).ToList())
        {
            var listGone = !_listNames.ContainsKey(filing.ListId!.Value);
            var alreadyThere = _items[filing.ItemId].ListId == filing.ListId;
            if (!listGone && !alreadyThere) continue;

            _filings.Remove(filing);
            orphans.Add(filing.ItemId);
        }

        _duplicates.RemoveAll(pair => !IsWaiting(pair.Proposal.ItemId));
        _routes.RemoveAll(route => !IsWaiting(route.ItemId));
        _filings.RemoveAll(filing => !IsWaiting(filing.ItemId));
        _archives.RemoveAll(archive => !IsWaiting(archive.ItemId));

        _unplaced.AddRange(orphans.Where(id => !_unplaced.Contains(id)));

        if (slice is not null)
        {
            var covered = Covered();
            _unplaced.AddRange(slice
                .Where(item => IsUndecided(item) && !covered.Contains(item.Id) && !_unplaced.Contains(item.Id))
                .Select(item => item.Id));
        }

        var stillCovered = Covered();
        _unplaced.RemoveAll(id => !IsWaiting(id) || stillCovered.Contains(id));
    }

    /// <summary>Every item a proposal speaks for: plan members, left out or not,
    /// both halves of a pair between two items, and every single-item proposal.</summary>
    private HashSet<Guid> Covered()
    {
        var covered = new HashSet<Guid>();
        foreach (var plan in _plans) covered.UnionWith(plan.Members);
        foreach (var pair in _duplicates)
        {
            covered.Add(pair.Proposal.ItemId);
            if (pair.Proposal.TargetKind == InboxTriageTargetKind.InboxItem) covered.Add(pair.Proposal.TargetId);
        }

        covered.UnionWith(_routes.Select(route => route.ItemId));
        covered.UnionWith(_filings.Select(filing => filing.ItemId));
        covered.UnionWith(_archives.Select(archive => archive.ItemId));
        return covered;
    }

    /// <summary>Whether the snapshot still has the item waiting for a first
    /// decision: unprocessed and never routed.</summary>
    private bool IsWaiting(Guid id) => _items.TryGetValue(id, out var item) && IsUndecided(item);

    private static bool IsUndecided(InboxItemDto item) => item.Status == InboxStatus.Unprocessed && item.Routing is null;

    /// <summary>Whether a proposal starts accepted.</summary>
    internal static bool StartsAccepted(double confidence) => confidence >= InboxTriagePassDto.AcceptedFrom;
}

/// <summary>
/// A plan under review: accepted or not, its title as the reader edits it, and
/// the members left out. A plan left with fewer than two members is no plan —
/// <see cref="Appliable"/> says so, and it is not applied however it is toggled.
/// </summary>
public sealed class InboxPassPlan
{
    private readonly HashSet<Guid> _leftOut = [];
    private readonly List<Guid> _members;

    internal InboxPassPlan(InboxTriagePlanProposalDto proposal, IReadOnlyList<Guid> members)
    {
        Proposal = proposal;
        _members = [.. members];
        Title = proposal.Title;
        Accepted = InboxTriagePassDraft.StartsAccepted(proposal.Confidence);
    }

    public InboxTriagePlanProposalDto Proposal { get; }

    /// <summary>Every member still waiting, left-out ones included, in the
    /// pass's order.</summary>
    public IReadOnlyList<Guid> Members => _members;

    /// <summary>The title as typed in the editor.</summary>
    public string Title { get; set; }

    public bool Accepted { get; set; }

    /// <summary>Whether the inline editor is open.</summary>
    public bool Editing { get; set; }

    /// <summary>The title the plan goes under: as typed, or the advisor's when
    /// the field was emptied.</summary>
    public string EffectiveTitle => string.IsNullOrWhiteSpace(Title) ? Proposal.Title : Title.Trim();

    /// <summary>What its plan tag will look like.</summary>
    public string TagPreview => InboxPlanTag.Preview(EffectiveTitle);

    /// <summary>The members that go, in order.</summary>
    public IReadOnlyList<Guid> Kept => [.. Members.Where(id => !_leftOut.Contains(id))];

    public bool Appliable => Kept.Count >= 2;

    /// <summary>What Apply will do with it.</summary>
    public bool IsAccepted => Accepted && Appliable;

    public bool IsLeftOut(Guid id) => _leftOut.Contains(id);

    /// <summary>Drops the members no longer waiting, after a newer snapshot.</summary>
    internal void KeepOnly(Func<Guid, bool> waiting)
    {
        _members.RemoveAll(id => !waiting(id));
        _leftOut.RemoveWhere(id => !_members.Contains(id));
    }

    public void SetLeftOut(Guid id, bool leftOut)
    {
        if (!Members.Contains(id)) return;

        if (leftOut) _leftOut.Add(id);
        else _leftOut.Remove(id);
    }
}

/// <summary>What the reader chose for a duplicate pair on the review screen.</summary>
public enum InboxPassDuplicateChoice
{
    /// <summary>Into the task, or "keep the other, archive this one".</summary>
    Merge,

    /// <summary>They only look alike: nothing is done.</summary>
    KeepBoth,

    /// <summary>Archive the capture; offered only against a task.</summary>
    Archive,
}

/// <summary>A duplicate pair under review and the choice made on it. It
/// starts on the pass's suggestion when the pass was confident, and on Keep
/// both — nothing done — when it was not.</summary>
public sealed class InboxPassDuplicate
{
    internal InboxPassDuplicate(InboxTriageDuplicatePairDto proposal)
    {
        Proposal = proposal;
        Choice = !InboxTriagePassDraft.StartsAccepted(proposal.Confidence) || proposal.Action == InboxDuplicateAction.KeepBoth
            ? InboxPassDuplicateChoice.KeepBoth
            : InboxPassDuplicateChoice.Merge;
    }

    public InboxTriageDuplicatePairDto Proposal { get; }

    public InboxPassDuplicateChoice Choice { get; set; }

    /// <summary>Whether Apply will act on it: anything but Keep both.</summary>
    public bool IsAccepted => Choice != InboxPassDuplicateChoice.KeepBoth;

    /// <summary>The choices the radio group offers, in order: against a task,
    /// merge, keep both or archive; against another item, keep that one and
    /// archive this, or keep both.</summary>
    public IReadOnlyList<InboxPassDuplicateChoice> Choices =>
        Proposal.TargetKind == InboxTriageTargetKind.Task
            ? [InboxPassDuplicateChoice.Merge, InboxPassDuplicateChoice.KeepBoth, InboxPassDuplicateChoice.Archive]
            : [InboxPassDuplicateChoice.Merge, InboxPassDuplicateChoice.KeepBoth];
}

/// <summary>A single-item proposal under review — a route, a filing or an
/// archive — and whether it is accepted.</summary>
public sealed class InboxPassProposal
{
    internal InboxPassProposal(Guid itemId, string reason, double confidence)
    {
        ItemId = itemId;
        Reason = reason;
        Confidence = confidence;
        Accepted = InboxTriagePassDraft.StartsAccepted(confidence);
    }

    public Guid ItemId { get; }

    public string Reason { get; }

    public double Confidence { get; }

    public bool Accepted { get; set; }

    /// <summary>A route's entry type: <c>prompt</c>, <c>task</c> or <c>test</c>.</summary>
    public string? EntryType { get; init; }

    /// <summary>A route's repositories.</summary>
    public IReadOnlyList<string> Repositories { get; init; } = [];

    /// <summary>A filing's list.</summary>
    public Guid? ListId { get; init; }
}

/// <summary>The filings into one list, drawn as one row: "File n in list".</summary>
public sealed record InboxPassFilingGroup(Guid ListId, string ListName, IReadOnlyList<InboxPassProposal> Items)
{
    /// <summary>Whether every filing in it is accepted — what the row's toggle
    /// reads, and pressing it sets them all to the other way.</summary>
    public bool AllAccepted => Items.All(item => item.Accepted);

    public void SetAccepted(bool accepted)
    {
        foreach (var item in Items) item.Accepted = accepted;
    }
}
