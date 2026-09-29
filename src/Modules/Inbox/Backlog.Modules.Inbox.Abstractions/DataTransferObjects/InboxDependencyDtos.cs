namespace Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

/// <summary>What a proposed dependency waits on: another item of the same
/// batch, or a task already in the backlog.</summary>
public enum DependencyTargetKind
{
    /// <summary>Another item routed in the same batch.</summary>
    Item,

    /// <summary>An open task the backlog already holds.</summary>
    Task
}

/// <summary>
/// How sure the Inbox is of a dependency it proposes. One tier today:
/// <see cref="Stated"/>, where the item's own text names the other thing — its
/// link, its title, its issue. A guess from similar wording would be a second
/// tier, and is not built.
/// </summary>
public enum DependencyTier
{
    /// <summary>The item's text names the thing it waits on.</summary>
    Stated
}

/// <summary>
/// The thing a proposed dependency waits on, as the panel names it and as the
/// route writes it.
/// </summary>
/// <param name="Kind">An item of the batch, or a task in the backlog.</param>
/// <param name="Id">The item's id, or the task's.</param>
/// <param name="Title">What the panel calls it: "<c>A</c> after <c>Title</c>".</param>
/// <param name="After">For a task, the value its <c>after:</c> token is
/// written with — see <see cref="ForTask"/>. Null for an item: which of its
/// entries the token names is only known once the batch's document is
/// written.</param>
public sealed record DependencyTarget(
    DependencyTargetKind Kind,
    Guid Id,
    string Title,
    string? After = null)
{
    public static DependencyTarget ForItem(Guid id, string title) => new(DependencyTargetKind.Item, id, title);

    /// <summary>
    /// A task as a target, with the value its <c>after:</c> token is written
    /// with: the <c>id:</c> it was imported under when an Inbox batch imported
    /// it — the item's guid, or <c>guid/repo</c> for an item with several
    /// repositories — and otherwise the task's own id.
    /// <para>
    /// Only the Inbox's own shape, because only that shape is unique by
    /// construction. Tasks resolves an <c>after:</c> against the stored
    /// <c>id:</c>s (ADR 0007), but a hand-written slug such as <c>setup</c> that
    /// two plans both carry is ambiguous, and Tasks leaves an ambiguous value as
    /// written — the new entry would wait on nothing for good while the task it
    /// meant is live. The real id always resolves. The imported id is kept where
    /// it is safe so the entry reads <c>after:&lt;item id&gt;</c>, the name an
    /// item routed in an earlier batch goes by.
    /// </para>
    /// </summary>
    public static DependencyTarget ForTask(InboxTaskReferenceDto task)
    {
        ArgumentNullException.ThrowIfNull(task);

        var imported = task.ImportItemId?.Trim();

        return new(
            DependencyTargetKind.Task,
            task.Id,
            task.Title,
            IsInboxImportId(imported) ? imported! : task.Id.ToString("D"));
    }

    /// <summary>Whether an imported <c>id:</c> is one an Inbox batch wrote: a
    /// guid in <c>D</c> format, alone or followed by <c>/</c> and a repository.</summary>
    private static bool IsInboxImportId(string? importItemId)
    {
        const int GuidLength = 36;

        if (importItemId is null || importItemId.Length < GuidLength) return false;
        if (!Guid.TryParseExact(importItemId.AsSpan(0, GuidLength), "D", out _)) return false;

        return importItemId.Length == GuidLength || (importItemId[GuidLength] == '/' && importItemId.Length > GuidLength + 1);
    }
}

/// <summary>
/// One dependency the Inbox can see between an item it is about to route and
/// something else: <paramref name="From"/> should come after
/// <paramref name="To"/>.
/// <para>
/// A proposal and nothing more, like a suggestion chip: the person turns it on
/// or off before the batch goes, and only the ones left on become
/// <c>after:</c> tokens. Stored nowhere — a dependency is a Tasks fact, and the
/// Inbox only ever proposes one.
/// </para>
/// </summary>
/// <param name="From">The batch item that waits.</param>
/// <param name="To">What it waits on.</param>
/// <param name="Reason">The text in <paramref name="From"/>'s title or notes that
/// named <paramref name="To"/>, exactly as written, so the person can see why
/// it was proposed.</param>
/// <param name="Tier">How sure the proposal is.</param>
public sealed record ProposedDependency(
    Guid From,
    DependencyTarget To,
    string Reason,
    DependencyTier Tier = DependencyTier.Stated);

/// <summary>One item of a proposed batch, as the panel lists it.</summary>
/// <param name="RepoIds">The repositories the item is assigned today, which the
/// panel offers as the starting choice.</param>
public sealed record InboxBatchProposalItemDto(
    Guid Id,
    string Title,
    IReadOnlyList<string> RepoIds);

/// <summary>
/// What routing a batch would do, asked before it is done: the plan tag the
/// import will write, the items that can go, the dependencies the Inbox can see
/// between them and to the backlog, and the items that cannot go and why.
/// </summary>
/// <param name="PlanTag">The batch's plan tag, sigil included — minted here, so
/// the panel shows the tag the import will write, and handed back on the route.</param>
/// <param name="Items">The routable items, in the order asked.</param>
/// <param name="Dependencies">Every stated dependency, grouped by the item that
/// waits, in the order asked.</param>
/// <param name="Refused">The items that cannot be routed — gone, already routed,
/// archived — each with the reason, in the order asked.</param>
/// <param name="Deferred">For a list, how many of its items are deferred and so
/// stay behind; null for a selection.</param>
public sealed record InboxBatchProposalDto(
    string PlanTag,
    IReadOnlyList<InboxBatchProposalItemDto> Items,
    IReadOnlyList<ProposedDependency> Dependencies,
    IReadOnlyList<InboxBatchFailureDto> Refused,
    int? Deferred = null);

/// <summary>
/// What the person decided in the panel before the batch goes. Every part is
/// optional: a batch routed without them is the batch as it was before there
/// was a panel.
/// </summary>
/// <param name="PlanTag">The tag the proposal minted, sigil included. Null mints
/// a new one.</param>
/// <param name="Repositories">Per item, the repositories to route it to instead
/// of the ones it is assigned. An item not named keeps its own; an empty list
/// is one entry with no repository.</param>
/// <param name="Dependencies">The dependencies left on. Checked again for a loop
/// before anything is sent.</param>
public sealed record InboxBatchRouteChoicesDto(
    string? PlanTag = null,
    IReadOnlyDictionary<Guid, IReadOnlyList<string>>? Repositories = null,
    IReadOnlyList<ProposedDependency>? Dependencies = null);
