using System.Text.RegularExpressions;

using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Inbox;

/// <summary>
/// Answers the Inbox's <see cref="IInboxTaskReferences"/> port over Tasks'
/// published <see cref="ITaskItems"/>: each task by what an inbox item's text
/// could name it with, and where it came from — the join the Inbox's relations
/// and its batch proposal both read, kept here so no Inbox source names Tasks.
/// <para>
/// Here beside <see cref="InboxBacklogTagSource"/> and for its reason: the Inbox
/// may not see Tasks, and this adapter may see both. The translation it makes
/// is from projections to links. A projection is <c>(repository, external id,
/// target type)</c> — Tasks keeps it as data so the module need not know what
/// GitHub is — and for an <c>issue</c> or a <c>pull-request</c> on an
/// <c>owner/name</c> repository the external id is the number, so the link is
/// the one GitHub gives it. The source link is the <c>Source:</c> line
/// <see cref="InboxBacklogTarget.Compose"/> writes under a routed item's notes.
/// A session projection is not a link anyone writes, and is left out.
/// </para>
/// <para>
/// Two reads, one translation. <see cref="OpenTasksAsync"/> leaves out done and
/// archived work, which nothing waits on; <see cref="AllTasksAsync"/> leaves out
/// only archived work, which the backlog has put away, and says of the rest
/// whether it is still open. The source item's id is Tasks' own string; one that
/// is not a guid — the sync service writes <c>desktop</c> on its acknowledgement
/// entries — names no Inbox item and is left out.
/// </para>
/// </summary>
internal sealed partial class InboxTaskReferences(ITaskItems tasks) : IInboxTaskReferences
{
    public async Task<IReadOnlyList<InboxTaskReferenceDto>> OpenTasksAsync(CancellationToken cancellationToken = default)
    {
        var entries = await tasks.ListAsync(cancellationToken).ConfigureAwait(false);

        return [.. entries
            .Where(entry => entry.Status is not (EntryStatus.Done or EntryStatus.Archived))
            .Select(Reference)];
    }

    public async Task<IReadOnlyList<InboxTaskReferenceDto>> AllTasksAsync(CancellationToken cancellationToken = default)
    {
        var entries = await tasks.ListAsync(cancellationToken).ConfigureAwait(false);

        return [.. entries
            .Where(entry => entry.Status is not EntryStatus.Archived)
            .Select(Reference)];
    }

    private static InboxTaskReferenceDto Reference(TaskItemDto entry)
    {
        var issues = Numbered(entry, EntryProjectionDto.IssueTargetType);
        var pulls = Numbered(entry, EntryProjectionDto.PullRequestTargetType);

        var sources = SourceLine().Matches(entry.Body ?? string.Empty).Select(match => match.Groups["url"].Value).ToList();

        var links = issues.Select(issue => $"https://github.com/{issue.Repo}/issues/{issue.Number}")
            .Concat(pulls.Select(pull => $"https://github.com/{pull.Repo}/pull/{pull.Number}"))
            .Concat(sources)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new InboxTaskReferenceDto(
            entry.Id,
            string.IsNullOrWhiteSpace(entry.ImportItemId) ? null : entry.ImportItemId,
            entry.Title,
            entry.RepoIds ?? [],
            issues,
            links,
            Guid.TryParse(entry.SourceInboxId, out var source) ? source : null,
            sources.FirstOrDefault(),
            entry.Status is not (EntryStatus.Done or EntryStatus.Archived),
            StatusWord(entry.Status));
    }

    /// <summary>The status as the entry grammar spells it, without its
    /// <c>!</c>: what the triage advisor reads beside a task's title.</summary>
    internal static string StatusWord(EntryStatus status) => status switch
    {
        EntryStatus.Draft => "draft",
        EntryStatus.Ready => "ready",
        EntryStatus.InProgress => "in-progress",
        EntryStatus.Done => "done",
        EntryStatus.Archived => "archived",
        _ => status.ToString().ToLowerInvariant(),
    };

    /// <summary>The entry's projections of one type on a GitHub repository,
    /// with the number they carry — a projection whose repository is not
    /// <c>owner/name</c>, or whose id is not a number, is not one a link can be
    /// made of, and is left out rather than drawn as a link to nowhere.</summary>
    private static List<InboxIssueReferenceDto> Numbered(TaskItemDto entry, string targetType) =>
        [.. entry.Projections
            .Where(projection => string.Equals(projection.TargetType, targetType, StringComparison.OrdinalIgnoreCase)
                && projection.RepoId.Split('/').Length == 2)
            .Select(projection => int.TryParse(projection.ExternalId, out var number) && number > 0
                ? new InboxIssueReferenceDto(projection.RepoId, number)
                : null)
            .OfType<InboxIssueReferenceDto>()
            .Distinct()];

    /// <summary>The <c>Source: &lt;url&gt;</c> line a routed item's entry
    /// carries, on a line of its own.</summary>
    [GeneratedRegex(@"^Source:[ \t]*(?<url>https?://\S+)[ \t]*\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex SourceLine();
}
