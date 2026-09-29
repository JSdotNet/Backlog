using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

namespace Backlog.Desktop.UI.Inbox;

/// <summary>
/// A batch the person is deciding on in the "Before you route" panel, before
/// it goes: the module's proposal, and the choices made on top of it — which
/// dependencies are on, which repositories each item goes to, which duplicate
/// groups are merged.
/// <para>
/// View state and its derivations, held by <see cref="InboxDesktopState"/>
/// rather than the panel for the reason the selection is: the shell re-mounts
/// the pane when it moves slots. Everything a render needs — the order, the
/// loops, the count — is computed here from the same
/// <see cref="InboxBatchOrder"/> the route checks with, so it can be asserted
/// without a DOM and cannot disagree with the command.
/// </para>
/// </summary>
public sealed class InboxRouteDraft
{
    private readonly bool[] _enabled;
    private readonly bool[] _merged;
    private readonly Dictionary<Guid, IReadOnlyList<string>> _repositories;
    private readonly Dictionary<Guid, string> _titles;

    public InboxRouteDraft(InboxBatchProposalDto proposal, Guid? listId, IReadOnlyList<InboxItemDto> picked)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(picked);

        Proposal = proposal;
        ListId = listId;
        Picked = picked;

        // Every proposal starts on: each one is something an item's own text
        // said, and turning one off is the deliberate act.
        _enabled = [.. proposal.Dependencies.Select(_ => true)];

        // Every merge starts off, the opposite of a dependency: a hint is only
        // ever said, and archiving an item is the deliberate act.
        _merged = [.. proposal.Hints.Select(_ => false)];
        _repositories = proposal.Items.ToDictionary(item => item.Id, item => item.RepoIds);
        _titles = proposal.Items.ToDictionary(item => item.Id, item => Title(item.Title));
    }

    public InboxBatchProposalDto Proposal { get; }

    /// <summary>The list the batch is, or null for a selection.</summary>
    public Guid? ListId { get; }

    /// <summary>The items as they were picked, refused ones included, so the
    /// route can still name the ones that stay behind.</summary>
    public IReadOnlyList<InboxItemDto> Picked { get; }

    public string PlanTag => Proposal.PlanTag;

    public IReadOnlyList<ProposedDependency> Dependencies => Proposal.Dependencies;

    public bool IsEnabled(int index) => _enabled[index];

    public void SetEnabled(int index, bool on) => _enabled[index] = on;

    /// <summary>The dependencies left on, in the proposal's order.</summary>
    public IReadOnlyList<ProposedDependency> EnabledDependencies =>
        [.. Dependencies.Where((_, index) => _enabled[index])];

    /// <summary>The routable items in the order they will go: each after what
    /// it waits on, otherwise as asked. Re-read on every render, so a toggle
    /// moves the list at once. An item a merge folds into another is not going,
    /// and is not in it.</summary>
    public IReadOnlyList<InboxBatchProposalItemDto> OrderedItems
    {
        get
        {
            var byId = Proposal.Items.ToDictionary(item => item.Id);
            return [.. InboxBatchOrder.Order(ItemIds, InboxBatchOrder.Edges(CarriedDependencies)).Select(id => byId[id])];
        }
    }

    /// <summary>The routable items that will be sent, in the order asked: every
    /// one but those a merge folds into another.</summary>
    public IReadOnlyList<InboxBatchProposalItemDto> GoingItems
    {
        get
        {
            var mergedAway = MergedAway;
            return [.. Proposal.Items.Where(item => !mergedAway.Contains(item.Id))];
        }
    }

    // --- Hints -----------------------------------------------------------------

    /// <summary>The proposal's ordering hints — said, never written, never part
    /// of the order or of a loop.</summary>
    public IReadOnlyList<OrderingHint> Hints => Proposal.Hints;

    /// <summary>Whether the hint at <paramref name="index"/> is a duplicate group
    /// the person has chosen to merge. Always false for any other kind.</summary>
    public bool IsMerged(int index) => _merged[index];

    /// <summary>Turns a duplicate group's merge on or off. A hint of any other
    /// kind cannot act, and is left as it is.</summary>
    public void SetMerged(int index, bool on)
    {
        if (Hints[index].Kind == OrderingHintKind.Duplicate) _merged[index] = on;
    }

    /// <summary>The items the merges turned on fold into another: every item of
    /// a merged group but its first. An item two merges name is folded once.</summary>
    public IReadOnlySet<Guid> MergedAway => KeptFor().Keys.ToHashSet();

    /// <summary>The merges turned on, per kept item the ones folded into it —
    /// the first merge to name an item claims it, and a kept item another merge
    /// already folded keeps nothing.</summary>
    private Dictionary<Guid, IReadOnlyList<Guid>> MergeMap()
    {
        var merges = new Dictionary<Guid, IReadOnlyList<Guid>>();
        var folded = new HashSet<Guid>();

        for (var index = 0; index < Hints.Count; index++)
        {
            var hint = Hints[index];
            if (!_merged[index] || hint.Items.Count < 2 || folded.Contains(hint.Items[0]) || merges.ContainsKey(hint.Items[0])) continue;

            var duplicates = hint.Items.Skip(1).Where(id => !merges.ContainsKey(id) && folded.Add(id)).ToList();
            if (duplicates.Count > 0) merges[hint.Items[0]] = duplicates;
        }

        return merges;
    }

    /// <summary>Each folded item, and the item it is folded into.</summary>
    private Dictionary<Guid, Guid> KeptFor() =>
        MergeMap().SelectMany(merge => merge.Value.Select(duplicate => (duplicate, merge.Key)))
            .ToDictionary(pair => pair.duplicate, pair => pair.Key);

    /// <summary>The dependencies left on with the merges applied — a merged
    /// item's carried onto the one kept, as the route will carry them — which
    /// is what the order and the loops are read from.</summary>
    private IReadOnlyList<ProposedDependency> CarriedDependencies => InboxBatchOrder.Carry(EnabledDependencies, KeptFor());

    /// <summary>"Keep <c>A</c>, archive <c>B</c>": what a merge would do, the
    /// way its switch says it.</summary>
    public string DescribeMerge(OrderingHint hint)
    {
        ArgumentNullException.ThrowIfNull(hint);

        var titles = hint.Items.Select(TitleOf).ToList();
        return titles.Count == 0
            ? hint.Reason
            : $"Keep “{titles[0]}”, archive {string.Join(", ", titles.Skip(1).Select(title => $"“{title}”"))}";
    }

    /// <summary>One hint as the panel says it: what was noticed, and about
    /// which items.</summary>
    public string DescribeHint(OrderingHint hint)
    {
        ArgumentNullException.ThrowIfNull(hint);

        var titles = hint.Items.Select(TitleOf).ToList();
        return hint.Kind switch
        {
            OrderingHintKind.CaptureOrder => $"{hint.Reason}: {string.Join(InboxBatchOrder.Arrow, titles)}",
            OrderingHintKind.SetupFirst when titles.Count > 0 => $"“{titles[0]}” {LowerFirst(hint.Reason)}",
            OrderingHintKind.Duplicate => $"{hint.Reason}: {string.Join(", ", titles)}",
            _ => $"{hint.Reason} — {string.Join(", ", titles)}",
        };
    }

    private string TitleOf(Guid id) => _titles.TryGetValue(id, out var title) ? title : "An item";

    private static string LowerFirst(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToLowerInvariant(text[0]) + text[1..];

    /// <summary>Every loop the enabled dependencies make once the merges are
    /// applied, named by titles — <c>A → B → A</c>. While there is one, nothing
    /// can be confirmed.</summary>
    public IReadOnlyList<string> Loops =>
        [.. InboxBatchOrder.Loops(ItemIds, InboxBatchOrder.Edges(CarriedDependencies)).Select(loop => InboxBatchOrder.Name(loop, id => _titles[id]))];

    public bool CanConfirm => Loops.Count == 0;

    /// <summary>"<c>A</c> after <c>B</c>": the dependency the way the panel
    /// says it.</summary>
    public string Describe(ProposedDependency dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);

        var from = _titles.TryGetValue(dependency.From, out var title) ? title : "An item";
        return $"{from} after {Title(dependency.To.Title)}";
    }

    // --- Repositories ----------------------------------------------------------

    /// <summary>Whether every item goes to the same repositories — the
    /// "one for all" choice — rather than each to its own.</summary>
    public bool OneForAll { get; private set; }

    /// <summary>The repositories every item goes to while <see cref="OneForAll"/>.</summary>
    public IReadOnlyList<string> SharedRepositories { get; private set; } = [];

    /// <summary>Turns "one for all" on or off. On, it starts from the first
    /// item that has repositories, so the choice opens on something the person
    /// already picked once; off, each item's own choice is where it was.</summary>
    public void SetOneForAll(bool on)
    {
        if (on && !OneForAll)
        {
            SharedRepositories = Proposal.Items.Select(item => _repositories[item.Id]).FirstOrDefault(repos => repos.Count > 0) ?? [];
        }

        OneForAll = on;
    }

    public void SetSharedRepositories(IReadOnlyList<string> repoIds) => SharedRepositories = repoIds ?? [];

    /// <summary>The repositories the item's own choice names, whatever
    /// <see cref="OneForAll"/> says.</summary>
    public IReadOnlyList<string> OwnRepositories(Guid id) => _repositories.TryGetValue(id, out var repos) ? repos : [];

    public void SetRepositories(Guid id, IReadOnlyList<string> repoIds)
    {
        if (_repositories.ContainsKey(id)) _repositories[id] = repoIds ?? [];
    }

    /// <summary>Where the item's entries will go.</summary>
    public IReadOnlyList<string> RepositoriesOf(Guid id) => OneForAll ? SharedRepositories : OwnRepositories(id);

    /// <summary>How many tasks the batch will make: one per repository per
    /// item, or one for an item with none — the route's own rule — for the
    /// items that will be sent.</summary>
    public int TaskCount => GoingItems.Sum(item => Math.Max(1, RepositoriesOf(item.Id).Count));

    /// <summary>Everything decided here, as the route takes it. The merges are
    /// named only when one is on, each as the kept item and the ones folded into
    /// it; a batch without one goes as it did before there were merges.</summary>
    public InboxBatchRouteChoicesDto Choices()
    {
        var merges = MergeMap();

        return new(
            PlanTag,
            Proposal.Items.ToDictionary(item => item.Id, item => RepositoriesOf(item.Id)),
            EnabledDependencies,
            merges.Count > 0 ? merges : null);
    }

    /// <summary>The ids the order and the loops are asked about: the items
    /// that will be sent.</summary>
    private IReadOnlyList<Guid> ItemIds => [.. GoingItems.Select(item => item.Id)];

    private static string Title(string title) => string.IsNullOrWhiteSpace(title) ? "Untitled" : title;
}
