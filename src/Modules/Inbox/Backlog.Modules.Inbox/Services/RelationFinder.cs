using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.DomainModels;

namespace Backlog.Modules.Inbox.Services;

/// <summary>
/// The Relation Finder domain service: what one item already has to do with the
/// rest of the backlog — other Inbox items that look like the same capture, and
/// tasks that already carry it — each with the reason, in words.
/// <para>
/// Beside <see cref="InboxClassifier"/> and <see cref="DependencyProposal"/>, and
/// like them only ever an answer: nothing here acts on a relation, and nothing
/// is stored. Deterministic and pure, so the reason on screen is the whole
/// reason.
/// </para>
/// <para>
/// An <b>item</b> relates, strongest reason first, when it has —
/// </para>
/// <list type="number">
/// <item><b>the same source link</b> (<see cref="InboxUrl.Key"/>): "Same link";</item>
/// <item><b>a near-identical title</b> (<see cref="TitleSimilarity"/>): "Nearly
/// the same title";</item>
/// <item><b>the same site</b> (<see cref="InboxUrl.Site"/> — on GitHub and
/// GitLab, the same repository): "Same site".</item>
/// </list>
/// <para>
/// A near-identical title outranks a shared site: two issues in one repository
/// are neighbours, while the same words captured twice are the same thought.
/// </para>
/// <para>
/// A <b>task</b> relates, strongest first, when it —
/// </para>
/// <list type="number">
/// <item><b>was made from this item</b>: it carries the item's id as its source
/// or as its import id (<c>&lt;guid&gt;</c> or <c>&lt;guid&gt;/&lt;repo&gt;</c>),
/// or the item's own routing names it: "Routed from this item";</item>
/// <item><b>has the same source link</b>: "Same link";</item>
/// <item><b>was filed as an issue the item's text names</b>
/// (<see cref="IssueMention"/>): "Links issue owner/name#12".</item>
/// </list>
/// <para>
/// One relation per item or task, its strongest reason; the item itself is never
/// among them. Within a reason, the order they were handed in.
/// </para>
/// </summary>
internal static class RelationFinder
{
    internal const string SameLinkReason = "Same link";
    internal const string SameSiteReason = "Same site";
    internal const string SimilarTitleReason = "Nearly the same title";
    internal const string RoutedFromItemReason = "Routed from this item";

    /// <summary>Everything that relates to <paramref name="item"/>:
    /// <paramref name="others"/> are the Inbox's items in its order, deleted ones
    /// already gone; <paramref name="tasks"/> the backlog's tasks in its order,
    /// archived ones already left out.</summary>
    public static InboxRelationsDto Find(
        InboxItem item,
        IReadOnlyList<InboxItem> others,
        IReadOnlyList<InboxTaskReferenceDto> tasks)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(others);
        ArgumentNullException.ThrowIfNull(tasks);

        return new InboxRelationsDto(item.Id, RelatedItems(item, others), RelatedTasks(item, tasks));
    }

    private static List<InboxRelatedItemDto> RelatedItems(InboxItem item, IReadOnlyList<InboxItem> others)
    {
        var site = InboxUrl.Site(item.SourceUrl);
        var related = new List<(int Rank, int At, InboxRelatedItemDto Relation)>();
        var seen = new HashSet<Guid> { item.Id };

        for (var at = 0; at < others.Count; at++)
        {
            var other = others[at];
            if (other.Deleted || !seen.Add(other.Id)) continue;

            (int Rank, InboxRelationKind Kind, string Reason)? strongest =
                InboxUrl.Same(item.SourceUrl, other.SourceUrl) ? (0, InboxRelationKind.SameLink, SameLinkReason)
                : TitleSimilarity.IsNearIdentical(item.Title, other.Title) ? (1, InboxRelationKind.SimilarTitle, SimilarTitleReason)
                : site is not null && string.Equals(site, InboxUrl.Site(other.SourceUrl), StringComparison.Ordinal) ? (2, InboxRelationKind.SameSite, SameSiteReason)
                : null;

            if (strongest is { } found)
            {
                related.Add((found.Rank, at, new InboxRelatedItemDto(other.Id, other.Title, other.Status, found.Kind, found.Reason)));
            }
        }

        return [.. related.OrderBy(entry => entry.Rank).ThenBy(entry => entry.At).Select(entry => entry.Relation)];
    }

    private static List<InboxRelatedTaskDto> RelatedTasks(InboxItem item, IReadOnlyList<InboxTaskReferenceDto> tasks)
    {
        var text = item.Title + "\n" + item.BodyMd;
        var links = InboxUrl.Links(text);
        var routedTo = item.Routing?.TaskIds.ToHashSet() ?? [];
        var related = new List<(int Rank, int At, InboxRelatedTaskDto Relation)>();
        var seen = new HashSet<Guid>();

        for (var at = 0; at < tasks.Count; at++)
        {
            var task = tasks[at];
            if (!seen.Add(task.Id)) continue;

            (int Rank, InboxRelationKind Kind, string Reason)? strongest =
                IsRoutedFrom(task, item.Id) || routedTo.Contains(task.Id) ? (0, InboxRelationKind.RoutedFromItem, RoutedFromItemReason)
                : InboxUrl.Same(item.SourceUrl, task.SourceUrl) ? (1, InboxRelationKind.SameLink, SameLinkReason)
                : IssueMention.Find(text, links, task, item.RepoIds) is { } mention
                    ? (2, InboxRelationKind.SameIssue, $"Links issue {mention.Issue.Repo}#{mention.Issue.Number}")
                : null;

            if (strongest is { } found)
            {
                related.Add((found.Rank, at, new InboxRelatedTaskDto(task.Id, task.Title, task.IsOpen, found.Kind, found.Reason)));
            }
        }

        return [.. related.OrderBy(entry => entry.Rank).ThenBy(entry => entry.At).Select(entry => entry.Relation)];
    }

    /// <summary>Whether the task carries the item's id as its source, or as the
    /// import id an Inbox batch writes: the guid alone, or the guid, a slash and
    /// a repository.</summary>
    private static bool IsRoutedFrom(InboxTaskReferenceDto task, Guid itemId)
    {
        if (task.SourceInboxId == itemId) return true;

        var imported = task.ImportItemId?.Trim();
        if (string.IsNullOrEmpty(imported)) return false;

        var id = itemId.ToString("D");
        return string.Equals(imported, id, StringComparison.OrdinalIgnoreCase)
            || (imported.Length > id.Length + 1
                && imported[id.Length] == '/'
                && imported.StartsWith(id, StringComparison.OrdinalIgnoreCase));
    }
}
