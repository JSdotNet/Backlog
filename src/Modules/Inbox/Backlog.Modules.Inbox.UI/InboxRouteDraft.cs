using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

namespace Backlog.Desktop.UI.Inbox;

/// <summary>
/// A batch the person is deciding on in the "Before you route" panel, before
/// it goes: the module's proposal, and the choices made on top of it — which
/// dependencies are on, which repositories each item goes to.
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
    private readonly List<ProposedDependency> _dependencies;
    private readonly List<bool> _enabled;
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
        _dependencies = [.. proposal.Dependencies];
        _enabled = [.. proposal.Dependencies.Select(_ => true)];
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

    /// <summary>The proposal's dependencies, then the ones the drafter added
    /// when asked — see <see cref="AddInferred"/>.</summary>
    public IReadOnlyList<ProposedDependency> Dependencies => _dependencies;

    public bool IsEnabled(int index) => _enabled[index];

    public void SetEnabled(int index, bool on) => _enabled[index] = on;

    /// <summary>The dependencies left on, in the proposal's order.</summary>
    public IReadOnlyList<ProposedDependency> EnabledDependencies =>
        [.. Dependencies.Where((_, index) => _enabled[index])];

    // --- The drafter's order ---------------------------------------------------

    /// <summary>Whether the drafter's order is being asked for now. Confirm
    /// waits for it, so the batch never goes on half an answer.</summary>
    public bool Inferring { get; set; }

    /// <summary>What the last ask came to, in a sentence — how many it added,
    /// or why it added none. Null before the first ask.</summary>
    public string? InferenceNote { get; private set; }

    /// <summary>Whether the last ask failed, so the note reads as a warning.</summary>
    public bool InferenceFailed { get; private set; }

    /// <summary>
    /// Adds the drafter's dependencies after the proposal's, on — the person
    /// asked for them, and turning one off is still the deliberate act. One the
    /// panel already shows between the same two items is not added twice,
    /// whatever tier it came from: the item's own words already said it.
    /// </summary>
    /// <returns>How many were added.</returns>
    public int AddInferred(IReadOnlyList<ProposedDependency> inferred)
    {
        ArgumentNullException.ThrowIfNull(inferred);

        var added = 0;
        foreach (var dependency in inferred)
        {
            var known = _dependencies.Any(existing =>
                existing.From == dependency.From
                && existing.To.Kind == dependency.To.Kind
                && existing.To.Id == dependency.To.Id);
            if (known || !_titles.ContainsKey(dependency.From)) continue;

            _dependencies.Add(dependency);
            _enabled.Add(true);
            added++;
        }

        InferenceFailed = false;
        InferenceNote = added switch
        {
            0 => "The AI found no order beyond what the items already say.",
            1 => "The AI added 1 dependency. Turn it off if it is wrong.",
            _ => $"The AI added {added} dependencies. Turn off any that are wrong.",
        };

        return added;
    }

    /// <summary>The ask failed: the note says why, and nothing was added.</summary>
    public void InferenceRefused(string message)
    {
        InferenceFailed = true;
        InferenceNote = message;
    }

    /// <summary>The routable items in the order they will go: each after what
    /// it waits on, otherwise as asked. Re-read on every render, so a toggle
    /// moves the list at once.</summary>
    public IReadOnlyList<InboxBatchProposalItemDto> OrderedItems
    {
        get
        {
            var byId = Proposal.Items.ToDictionary(item => item.Id);
            return [.. InboxBatchOrder.Order(ItemIds, InboxBatchOrder.Edges(EnabledDependencies)).Select(id => byId[id])];
        }
    }

    /// <summary>Every loop the enabled dependencies make, named by titles —
    /// <c>A → B → A</c>. While there is one, nothing can be confirmed.</summary>
    public IReadOnlyList<string> Loops =>
        [.. InboxBatchOrder.Loops(ItemIds, InboxBatchOrder.Edges(EnabledDependencies)).Select(loop => InboxBatchOrder.Name(loop, id => _titles[id]))];

    public bool CanConfirm => Loops.Count == 0 && !Inferring;

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
    /// item, or one for an item with none — the route's own rule.</summary>
    public int TaskCount => Proposal.Items.Sum(item => Math.Max(1, RepositoriesOf(item.Id).Count));

    /// <summary>Everything decided here, as the route takes it.</summary>
    public InboxBatchRouteChoicesDto Choices() =>
        new(
            PlanTag,
            Proposal.Items.ToDictionary(item => item.Id, item => RepositoriesOf(item.Id)),
            EnabledDependencies);

    private IReadOnlyList<Guid> ItemIds => [.. Proposal.Items.Select(item => item.Id)];

    private static string Title(string title) => string.IsNullOrWhiteSpace(title) ? "Untitled" : title;
}
