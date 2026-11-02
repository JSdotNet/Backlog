using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Features.FileFollowUp;

/// <summary>
/// Files the one prompt an Approve + follow-up answer asks for, after the gated entry
/// (<c>.devbook/domain/tasks/features.md#filing-a-follow-up-after-an-entry</c>).
/// <para>
/// The aggregate is built directly, the way Import builds one, rather than by splicing
/// the words into entry text and parsing it: the words are the body and nothing else,
/// so there is no text for the grammar to read fields out of.
/// </para>
/// <para>
/// No <c>import_plan_id</c> on purpose. The plan's tag is enough for the plan to list
/// the follow-up (<c>get_plan_items</c> reads an entry with no plan id by its tag), and a
/// re-import clears not-started entries by <c>import_plan_id</c> — so a follow-up that
/// carried one would be cleared by the next version of the plan, which never wrote it.
/// </para>
/// </summary>
internal sealed class FileFollowUpHandler(ITaskRepository entries, TimeProvider clock) : ITaskFollowUps
{
    /// <summary>One filing at a time in this process, so two deliveries of the same
    /// answer racing each other still find the first one's follow-up.</summary>
    private static readonly SemaphoreSlim Filing = new(1, 1);

    public static readonly Error EmptyWords = Error.Validation(
        "follow_up.empty_text",
        "A follow-up needs the person's words.");

    public static readonly Error NoGatedEntry = Error.Validation(
        "follow_up.no_gated_entry",
        "A follow-up needs the entry it waits on, named by id or by plan and item.");

    public static readonly Error GatedEntryNotFound = Error.NotFound(
        "follow_up.gated_entry_not_found",
        "No live entry matches the entry the follow-up would wait on.");

    public async Task<Result<Guid>> FileAfterAsync(TaskFollowUpRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Gated);

        if (string.IsNullOrWhiteSpace(request.Text)) return EmptyWords;
        if (!IsNamed(request.Gated)) return NoGatedEntry;

        await Filing.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Tombstones included: the key is the answer, not the follow-up being
            // live, so a person who deleted the follow-up does not get it back from a
            // late redelivery of the same answer.
            var all = await entries.ListChangedSinceAsync(DateTimeOffset.MinValue, cancellationToken).ConfigureAwait(false);

            if (all.FirstOrDefault(entry => entry.SourceNotificationId == request.NotificationId) is { } filed)
            {
                return filed.Id;
            }

            var live = all.Where(entry => entry.DeletedAt is null).ToList();

            if (Find(request.Gated, live) is not { } gated) return GatedEntryNotFound;

            var planTag = PlanTagOf(gated);
            var followUp = new TaskItem(
                Guid.NewGuid(),
                "Follow-up: " + gated.Title.Trim(),
                FollowUpBody.From(request.Text),
                EntryType.Prompt,
                EntryStatus.Ready,
                Priority.Medium,
                gated.RepoIds,
                planTag is null ? [] : [planTag],
                sourceInboxId: null,
                createdAt: clock.GetUtcNow(),
                recurrenceSourceId: null,
                sourceNotificationId: request.NotificationId);

            // At the end of the list, as Import ranks what it brings in.
            followUp.SetOrder(live.Count);
            followUp.SetDependsOn([gated.Id.ToString()]);

            await entries.SaveAsync(followUp, cancellationToken).ConfigureAwait(false);

            return followUp.Id;
        }
        finally
        {
            Filing.Release();
        }
    }

    private static bool IsNamed(GatedEntry gated) =>
        gated.EntryId is not null
        || (!string.IsNullOrWhiteSpace(gated.Plan) && !string.IsNullOrWhiteSpace(gated.Item));

    /// <summary>
    /// The gated entry: by id, or by plan and item the way a stored <c>after:</c> slug
    /// is resolved (<see cref="DependencyResolution"/>) — the item's <c>id:</c> under the
    /// plan's <c>import_plan_id</c>, else under the plan's tag on an entry that has no
    /// plan id. The run's plan-item marker writes the plan bare, so the stored id's
    /// <c>+</c> sigil is not compared.
    /// </summary>
    private static TaskItem? Find(GatedEntry gated, IReadOnlyList<TaskItem> live)
    {
        if (gated.EntryId is { } id) return live.FirstOrDefault(entry => entry.Id == id);

        var plan = Bare(gated.Plan!.Trim());
        var item = gated.Item!.Trim();

        var named = live
            .Where(entry => string.Equals(entry.ImportItemId, item, StringComparison.Ordinal))
            .ToList();

        return named.FirstOrDefault(entry => entry.ImportPlanId is { } owner && SamePlan(owner, plan))
            ?? named.FirstOrDefault(entry => entry.ImportPlanId is null && entry.Tags.Any(tag => tag.StartsWith('+') && SamePlan(tag, plan)));
    }

    /// <summary>
    /// The plan tag the gated entry is filed under, or null: its <c>import_plan_id</c>
    /// when an import gave it one — the tag the plan's entries carry — else the one
    /// <c>+tag</c> it wears, which is how a follow-up of a follow-up stays in the plan.
    /// An entry wearing none, or several, is outside any one plan.
    /// </summary>
    private static string? PlanTagOf(TaskItem gated)
    {
        if (!string.IsNullOrWhiteSpace(gated.ImportPlanId)) return gated.ImportPlanId;

        var planTags = gated.Tags.Where(tag => tag.StartsWith('+')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        return planTags.Count == 1 ? planTags[0] : null;
    }

    private static bool SamePlan(string stored, string plan) =>
        string.Equals(Bare(stored), plan, StringComparison.OrdinalIgnoreCase);

    private static string Bare(string tag) => tag.StartsWith('+') ? tag[1..] : tag;
}
