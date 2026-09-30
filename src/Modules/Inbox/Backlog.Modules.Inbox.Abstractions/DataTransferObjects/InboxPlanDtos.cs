namespace Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

/// <summary>
/// What the module gives a plan drafter to work from. The same facts as
/// <see cref="InboxRouteRequestDto"/> plus the two a plan needs that a single
/// entry does not: what kind of capture it is, and the tag every entry of the
/// plan must carry so Tasks' import can recognise the plan on a re-import
/// (local ADR 0007's <c>import_plan_id</c>).
/// </summary>
/// <param name="PlanTag">A slug derived from the title with the item's id on
/// the end — lower-case, letters, digits and hyphens, at most forty characters
/// — so it is a legal <c>#tag</c> on the metadata line and names this item's
/// plan and no other's.</param>
/// <param name="Batch">
/// Set when the question is about a batch rather than one item: the items to
/// be ordered, each answered as one entry whose <c>id:</c> is the item's id and
/// whose <c>after:</c> tokens are the drafter's inference. The rest of the
/// request then describes the batch — <paramref name="InboxItemId"/> is
/// <see cref="Guid.Empty"/>, <paramref name="Title"/> names the batch,
/// <paramref name="RepoIds"/> is every repository its items go to, and
/// <paramref name="PlanTag"/> is the batch's tag without its sigil. Null — the
/// default — is the single-item request it always was.
/// </param>
public sealed record InboxPlanDraftRequestDto(
    Guid InboxItemId,
    string Title,
    string BodyMd,
    string? SourceUrl,
    string KindSlug,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> RepoIds,
    string PlanTag,
    IReadOnlyList<InboxPlanDraftBatchItemDto>? Batch = null);

/// <summary>One item of a batch the drafter is asked to order: the facts the
/// single-item request carries about its item, with the repositories this item
/// goes to — the only ones its entry may name.</summary>
public sealed record InboxPlanDraftBatchItemDto(
    Guid InboxItemId,
    string Title,
    string BodyMd,
    string? SourceUrl,
    string KindSlug,
    IReadOnlyList<string> RepoIds);

/// <summary>What the drafter answered: entry text in the Backlog import grammar,
/// ready for Tasks' import as it stands. The module checks only that it holds
/// at least one entry heading; whether it imports is Tasks' verdict.</summary>
public sealed record InboxPlanDraftDto(string PlanMarkdown);

/// <summary>What routing produced: the item that was routed and the entries the
/// Tasks context created for it. The pane raises this so the shell can refresh
/// the Tasks pane without knowing what changed there.</summary>
public sealed record InboxRoutedDto(Guid InboxItemId, IReadOnlyList<Guid> TaskIds);
