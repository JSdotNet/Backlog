using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Abstractions.Services;

/// <summary>
/// Which entry a follow-up waits on — the gated entry, the one a delivery run was working
/// (<c>.devbook/domain/sessions/domain.md#delivery-run-reference</c>). A run names it either
/// by id (the entry marker) or by its plan and item (the plan-item marker); exactly one of
/// the two is set.
/// </summary>
/// <param name="EntryId">The gated entry's id, or null when it is named by plan and item.</param>
/// <param name="Plan">The bare plan tag (the entry's <c>import_plan_id</c>), or null.</param>
/// <param name="Item">The item id inside that plan (the entry's <c>import_item_id</c>), or null.</param>
public sealed record GatedEntry(Guid? EntryId, string? Plan = null, string? Item = null)
{
    /// <summary>The gated entry, named by its id.</summary>
    public static GatedEntry ById(Guid entryId) => new(entryId);

    /// <summary>The gated entry, named by its plan and the item inside it.</summary>
    public static GatedEntry ByPlanItem(string plan, string item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(item);

        return new(null, plan, item);
    }
}

/// <summary>
/// What filing a follow-up needs (<c>.devbook/domain/tasks/features.md#filing-a-follow-up-after-an-entry</c>).
/// </summary>
/// <param name="Gated">The entry the follow-up waits on.</param>
/// <param name="Text">The person's words, kept as plain body text — never parsed as
/// sub-items, headings, fences or tags.</param>
/// <param name="NotificationId">The notification whose answer asked for it: the
/// follow-up's provenance (<c>source_notification_id</c>) and the idempotency key.</param>
public sealed record TaskFollowUpRequest(GatedEntry Gated, string Text, Guid NotificationId);

/// <summary>
/// Files the one prompt entry an Approve + follow-up answer asks for, after the gated entry
/// (<c>.devbook/domain/tasks/requirements.md#filing-a-follow-up-after-an-entry</c>).
/// <para>
/// A port of its own rather than an <see cref="ITaskItems"/> member, so the many hosts and
/// test doubles of that port stay untouched. Called by the Sessions adapter, which sees
/// this project and never the Tasks module itself.
/// </para>
/// </summary>
public interface ITaskFollowUps
{
    /// <summary>
    /// Files the follow-up: a <c>ready</c> prompt <c>after:</c> the gated entry, with its
    /// plan tag and repositories, no <c>import_plan_id</c>, titled "Follow-up: " and the
    /// gated entry's title, the words as its body.
    /// <para>
    /// Idempotent on <see cref="TaskFollowUpRequest.NotificationId"/>: a second call answers
    /// the follow-up already filed for it and files nothing. Fails with
    /// <see cref="ErrorType.NotFound"/> when no live entry matches
    /// <see cref="TaskFollowUpRequest.Gated"/>, and with <see cref="ErrorType.Validation"/>
    /// for empty words or a gated entry named neither way. A store that cannot be reached
    /// throws, so the caller's delivery is retried.
    /// </para>
    /// </summary>
    /// <returns>The follow-up's id — the existing one on a repeat.</returns>
    Task<Result<Guid>> FileAfterAsync(TaskFollowUpRequest request, CancellationToken cancellationToken = default);
}
