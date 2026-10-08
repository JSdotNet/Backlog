using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.DomainModels;

namespace Backlog.Modules.Inbox.Services;

/// <summary>
/// What both triage slices share: reading the context an item is compared
/// with, and holding the advisor's answer to it. Here rather than in either
/// slice because a slice never calls another (Features/README.md).
/// <para>
/// The context is exactly what local ADR 0023 §1 lets leave the machine: the
/// open backlog tasks, the other unprocessed items, the repositories configured
/// in Settings and the reader's lists. A decided item, a closed or archived
/// task and a task's body are never in it.
/// </para>
/// <para>
/// The answer is a proposal, and a proposal naming something the context does
/// not hold is not one the reader can take. So whatever the advisor said —
/// a model, a stand-in — is held to the context here, once, and every adapter
/// need only translate: an unknown id, a repository Settings does not list, a
/// list that does not exist, an entry type the grammar has no word for are
/// dropped rather than shown.
/// </para>
/// </summary>
internal static class InboxTriageContext
{
    /// <summary>Still waiting for its first decision: unprocessed, never
    /// routed, not deleted. Deferred and triaged items are decided.</summary>
    public static bool IsUnprocessed(InboxItem item) =>
        !item.Deleted && item.Status == InboxStatus.Unprocessed && item.Routing is null;

    /// <summary>Reads the context, leaving <paramref name="exclude"/> out of
    /// the other items — the item being advised on, or the pass's own items.</summary>
    public static async Task<InboxTriageContextDto> ReadAsync(
        IReadOnlyList<InboxItem> all,
        IInboxOrganizerRepository organizer,
        IInboxTaskReferences? taskReferences,
        IReadOnlyCollection<Guid> exclude,
        IReadOnlyList<string>? repositories,
        CancellationToken cancellationToken)
    {
        var tasks = taskReferences is null
            ? []
            : await taskReferences.OpenTasksAsync(cancellationToken).ConfigureAwait(false);
        var lists = await organizer.ListListsAsync(cancellationToken).ConfigureAwait(false);

        return new InboxTriageContextDto(
            [.. tasks
                .Where(task => task.IsOpen)
                .Select(task => new InboxTriageTaskDto(task.Id, task.Title, task.RepoIds, task.Status ?? "open"))],
            [.. all
                .Where(item => IsUnprocessed(item) && !exclude.Contains(item.Id))
                .Select(item => InboxTriageItemDto.From(item.ToDto()))],
            [.. (repositories ?? [])
                .Where(repository => !string.IsNullOrWhiteSpace(repository))
                .Select(repository => repository.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)],
            [.. lists.OrderBy(list => list.Order).Select(list => new InboxTriageListDto(list.Id, list.Name))]);
    }

    /// <summary>The cards for <paramref name="item"/>, held to the context: a
    /// duplicate must name an open task or another unprocessed item, never the
    /// item itself; a plan must hold the item and at least one other item the
    /// context knows; a repository must be one Settings lists or the item
    /// already carries.</summary>
    public static InboxTriageAdviceDto Hold(InboxTriageAdviceDto advice, InboxTriageItemDto item, InboxTriageContextDto context)
    {
        var duplicate = advice.Duplicate is { } named ? HoldDuplicate(named, item, context) : null;

        InboxTriagePlanGroupingDto? plan = null;
        if (advice.Plan is { } grouping && !string.IsNullOrWhiteSpace(grouping.Title))
        {
            var known = context.OtherItems.Select(other => other.Id).Append(item.Id).ToHashSet();
            var members = grouping.ItemIds.Where(known.Contains).Distinct().ToList();
            if (!members.Contains(item.Id)) members.Insert(0, item.Id);

            if (members.Count >= 2) plan = grouping with { Title = grouping.Title.Trim(), ItemIds = members };
        }

        return new InboxTriageAdviceDto(item.Id, duplicate, plan, Repositories(advice.Repositories, context, item.RepoIds));
    }

    /// <summary>The pass, held to its items and the context. Each item is
    /// placed once — the first proposal naming it, in the order plans,
    /// duplicates, routes, filings, archives — and every item no kept
    /// proposal placed is unplaced, whatever the advisor said about it.</summary>
    public static InboxTriagePassDto Hold(InboxTriagePassDto pass, IReadOnlyList<InboxTriageItemDto> items, InboxTriageContextDto context)
    {
        var batch = items.Select(item => item.Id).ToList();
        var inBatch = batch.ToHashSet();
        var repoIdsOf = items.ToDictionary(item => item.Id, item => item.RepoIds);
        var placed = new HashSet<Guid>();

        var plans = new List<InboxTriagePlanProposalDto>();
        foreach (var plan in pass.Plans)
        {
            if (string.IsNullOrWhiteSpace(plan.Title)) continue;

            var members = plan.ItemIds.Where(id => inBatch.Contains(id) && !placed.Contains(id)).Distinct().ToList();
            if (members.Count < 2) continue;

            placed.UnionWith(members);
            plans.Add(plan with
            {
                Title = plan.Title.Trim(),
                ItemIds = members,
                Repositories = Repositories(plan.Repositories, context, [.. members.SelectMany(id => repoIdsOf[id])]),
                Confidence = Clamp(plan.Confidence),
            });
        }

        var tasks = context.OpenTasks.Select(task => task.Id).ToHashSet();
        var otherItems = context.OtherItems.Select(item => item.Id).ToHashSet();

        var duplicates = new List<InboxTriageDuplicatePairDto>();
        foreach (var pair in pass.Duplicates)
        {
            if (!inBatch.Contains(pair.ItemId) || placed.Contains(pair.ItemId) || pair.TargetId == pair.ItemId) continue;

            var targetKnown = pair.TargetKind switch
            {
                InboxTriageTargetKind.Task => tasks.Contains(pair.TargetId),
                InboxTriageTargetKind.InboxItem => inBatch.Contains(pair.TargetId) || otherItems.Contains(pair.TargetId),
                _ => false,
            };
            if (!targetKnown || !Enum.IsDefined(pair.Action)) continue;

            // The other half of a pair inside the batch must still be free:
            // an item already in a plan is not also kept-or-attached.
            var targetInBatch = pair.TargetKind == InboxTriageTargetKind.InboxItem && inBatch.Contains(pair.TargetId);
            if (targetInBatch && placed.Contains(pair.TargetId)) continue;

            // Only a task can be merged into.
            if (pair.Action == InboxDuplicateAction.MergeIntoTask && pair.TargetKind != InboxTriageTargetKind.Task) continue;

            placed.Add(pair.ItemId);

            // The other half of a pair inside the batch is decided by the same
            // proposal, so nothing else may place it.
            if (targetInBatch) placed.Add(pair.TargetId);

            duplicates.Add(pair with { Confidence = Clamp(pair.Confidence) });
        }

        var routes = new List<InboxTriageRouteProposalDto>();
        foreach (var route in pass.Routes)
        {
            if (!inBatch.Contains(route.ItemId) || placed.Contains(route.ItemId)) continue;

            var type = route.EntryType?.Trim().ToLowerInvariant();
            if (type is null || !InboxTriagePassDto.EntryTypes.Contains(type)) continue;

            placed.Add(route.ItemId);
            routes.Add(route with
            {
                EntryType = type,
                Repositories = Repositories(route.Repositories, context, repoIdsOf[route.ItemId]),
                Confidence = Clamp(route.Confidence),
            });
        }

        var lists = context.Lists.Select(list => list.Id).ToHashSet();
        var filings = new List<InboxTriageListFilingDto>();
        foreach (var filing in pass.Filings)
        {
            if (!inBatch.Contains(filing.ItemId) || placed.Contains(filing.ItemId) || !lists.Contains(filing.ListId)) continue;

            placed.Add(filing.ItemId);
            filings.Add(filing with { Confidence = Clamp(filing.Confidence) });
        }

        var archives = new List<InboxTriageArchiveDto>();
        foreach (var archive in pass.Archives)
        {
            if (!inBatch.Contains(archive.ItemId) || placed.Contains(archive.ItemId)) continue;

            placed.Add(archive.ItemId);
            archives.Add(archive with { Confidence = Clamp(archive.Confidence) });
        }

        return new InboxTriagePassDto(plans, duplicates, routes, filings, archives, [.. batch.Where(id => !placed.Contains(id))]);
    }

    private static InboxTriageDuplicateDto? HoldDuplicate(InboxTriageDuplicateDto duplicate, InboxTriageItemDto item, InboxTriageContextDto context)
    {
        if (duplicate.TargetId == item.Id) return null;

        var title = duplicate.TargetKind switch
        {
            InboxTriageTargetKind.Task => context.OpenTasks.FirstOrDefault(task => task.Id == duplicate.TargetId)?.Title,
            InboxTriageTargetKind.InboxItem => context.OtherItems.FirstOrDefault(other => other.Id == duplicate.TargetId)?.Title,
            _ => null,
        };

        // The title is the context's, not the advisor's: the card names what
        // the reader will find when they follow it.
        return title is null ? null : duplicate with { TargetTitle = title };
    }

    /// <summary>The repositories that may stand: the ones Settings lists, and
    /// the ones the items already carry, in Settings' spelling where it has
    /// one.</summary>
    private static List<string> Repositories(IReadOnlyList<string>? proposed, InboxTriageContextDto context, IEnumerable<string> own)
    {
        var allowed = context.Repositories
            .Concat(own)
            .GroupBy(repository => repository, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return [.. (proposed ?? [])
            .Where(repository => !string.IsNullOrWhiteSpace(repository))
            .Select(repository => allowed.GetValueOrDefault(repository.Trim()))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static double Clamp(double confidence) =>
        double.IsNaN(confidence) ? 0 : Math.Clamp(confidence, 0, 1);
}
