namespace Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

/// <summary>
/// One inbox item as the triage advisor reads it — exactly the facts local
/// ADR 0023 §1 lets leave the machine: its title, link and notes, its kind,
/// source and person, its tags and its repositories. Attachments, paths and
/// settings are not here, so no adapter can send them.
/// </summary>
/// <param name="Source">The capture channel the item came through
/// (<c>manual</c>, <c>mobile</c>, a capture source's slug).</param>
/// <param name="Person">Who shared it, when a person did.</param>
public sealed record InboxTriageItemDto(
    Guid Id,
    string Title,
    string BodyMd,
    string? SourceUrl,
    string KindSlug,
    string Source,
    string? Person,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> RepoIds)
{
    /// <summary>The item's triage view, from the pane's DTO.</summary>
    public static InboxTriageItemDto From(InboxItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new InboxTriageItemDto(
            item.Id,
            item.Title,
            item.BodyMd,
            item.SourceUrl,
            item.KindSlug,
            item.Channel,
            item.Person,
            [.. item.Tags.Select(tag => tag.Name)],
            item.RepoIds);
    }
}

/// <summary>An open backlog task as the advisor matches against it: its id,
/// title, repositories and status — never its body (local ADR 0023 §1).</summary>
public sealed record InboxTriageTaskDto(Guid Id, string Title, IReadOnlyList<string> RepoIds, string Status);

/// <summary>An inbox list an item can be filed in, by its id and its name.</summary>
public sealed record InboxTriageListDto(Guid Id, string Name);

/// <summary>
/// What the item, or the pass, is compared with: the open backlog tasks, the
/// other unprocessed inbox items, the repositories configured in Settings and
/// the reader's inbox lists. A closed or archived task and a decided item are
/// never in it.
/// </summary>
public sealed record InboxTriageContextDto(
    IReadOnlyList<InboxTriageTaskDto> OpenTasks,
    IReadOnlyList<InboxTriageItemDto> OtherItems,
    IReadOnlyList<string> Repositories,
    IReadOnlyList<InboxTriageListDto> Lists)
{
    public static readonly InboxTriageContextDto Empty = new([], [], [], []);
}

/// <summary>What a duplicate is a duplicate of: a backlog task, or another
/// inbox item.</summary>
public enum InboxTriageTargetKind
{
    Task,
    InboxItem,
}

/// <summary>What the pass suggests doing with a duplicate pair.</summary>
public enum InboxDuplicateAction
{
    /// <summary>Merge the capture into the backlog task it repeats.</summary>
    MergeIntoTask,

    /// <summary>Keep one of the two and attach the other to it.</summary>
    KeepOneAttachOther,

    /// <summary>They only look alike: keep both.</summary>
    KeepBoth,
}

/// <summary>The duplicate card: what the item repeats, the title it is known
/// by, and one sentence saying why.</summary>
public sealed record InboxTriageDuplicateDto(
    InboxTriageTargetKind TargetKind,
    Guid TargetId,
    string TargetTitle,
    string Reason);

/// <summary>The plan card: a plan title, the items that belong in it — the
/// item itself among them — and why.</summary>
public sealed record InboxTriagePlanGroupingDto(
    string Title,
    IReadOnlyList<Guid> ItemIds,
    string Reason);

/// <summary>
/// The advisor's answer about one item opened in triage: at most one duplicate,
/// at most one plan grouping, and the repositories it would route to. Either
/// card may be absent; <see cref="None"/> is an answer with neither.
/// </summary>
public sealed record InboxTriageAdviceDto(
    Guid ItemId,
    InboxTriageDuplicateDto? Duplicate,
    InboxTriagePlanGroupingDto? Plan,
    IReadOnlyList<string> Repositories)
{
    public static InboxTriageAdviceDto None(Guid itemId) => new(itemId, null, null, []);

    /// <summary>True when there is a card to show.</summary>
    public bool HasCards => Duplicate is not null || Plan is not null;
}

/// <summary>A plan the pass proposes: its title, the items in it, the
/// repositories it goes to, why, and how sure the advisor is (0 to 1).</summary>
public sealed record InboxTriagePlanProposalDto(
    string Title,
    IReadOnlyList<Guid> ItemIds,
    IReadOnlyList<string> Repositories,
    string Reason,
    double Confidence);

/// <summary>A duplicate pair the pass found: the item, what it repeats, and
/// what to do about it.</summary>
public sealed record InboxTriageDuplicatePairDto(
    Guid ItemId,
    InboxTriageTargetKind TargetKind,
    Guid TargetId,
    InboxDuplicateAction Action,
    string Reason,
    double Confidence);

/// <summary>One item routed on its own: the entry type it becomes
/// (<c>prompt</c>, <c>task</c> or <c>test</c>), its repositories, and why.</summary>
public sealed record InboxTriageRouteProposalDto(
    Guid ItemId,
    string EntryType,
    IReadOnlyList<string> Repositories,
    string Reason,
    double Confidence);

/// <summary>An item filed in one of the reader's lists.</summary>
public sealed record InboxTriageListFilingDto(Guid ItemId, Guid ListId, string Reason, double Confidence);

/// <summary>An item the pass would archive.</summary>
public sealed record InboxTriageArchiveDto(Guid ItemId, string Reason, double Confidence);

/// <summary>
/// The AI triage pass: a decision proposed for every item it could place, and
/// the ids of the ones it could not. Nothing in it is applied until the reader
/// presses Apply (local ADR 0023 §5); a proposal below
/// <see cref="AcceptedFrom"/> starts unaccepted.
/// </summary>
public sealed record InboxTriagePassDto(
    IReadOnlyList<InboxTriagePlanProposalDto> Plans,
    IReadOnlyList<InboxTriageDuplicatePairDto> Duplicates,
    IReadOnlyList<InboxTriageRouteProposalDto> Routes,
    IReadOnlyList<InboxTriageListFilingDto> Filings,
    IReadOnlyList<InboxTriageArchiveDto> Archives,
    IReadOnlyList<Guid> Unplaced)
{
    /// <summary>The confidence from which a proposal starts accepted.</summary>
    public const double AcceptedFrom = 0.6;

    public static readonly InboxTriagePassDto Nothing = new([], [], [], [], [], []);

    /// <summary>The entry types a single route may name.</summary>
    public static readonly IReadOnlyList<string> EntryTypes = ["prompt", "task", "test"];
}
