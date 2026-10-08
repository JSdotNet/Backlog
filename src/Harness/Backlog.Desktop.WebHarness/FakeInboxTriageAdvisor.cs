using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Desktop.WebHarness;

/// <summary>
/// A deterministic stand-in for the Foundry triage advisor, so every AI
/// triage path — a duplicate card, a plan card, a pass with every kind of
/// proposal, a low-confidence one among them — can be seen and driven without
/// Azure. The harness registers it in place of <c>AzureFoundryInboxTriageAdvisor</c>
/// when <see cref="ConfigurationKey"/> is true, which its Development settings
/// make the default.
/// <para>
/// The rules, in the order they are tried:
/// </para>
/// <list type="number">
/// <item><b>Duplicate.</b> An item whose title shares <see cref="SharedWords"/>
/// words with an open task repeats that task; failing that, one that shares
/// them with another unprocessed inbox item repeats that item. A word is three
/// or more letters or digits, compared without case, and a handful of filler
/// words (<see cref="Filler"/>) do not count.</item>
/// <item><b>Plan.</b> Items sharing a tag group into one plan named for the
/// tag.</item>
/// <item><b>List.</b> An item with a tag that names one of the reader's lists
/// is filed there.</item>
/// <item><b>Route.</b> An item with repositories is routed as a <c>prompt</c>;
/// one with notes or a link but no repository as a <c>task</c>.</item>
/// <item><b>Archive.</b> A bare title of two words or fewer is archived.</item>
/// <item>Anything else is unplaced.</item>
/// </list>
/// <para>
/// Every proposal of a pass is confident (0.9) but one: the first route — or,
/// in a pass with no route, the first proposal of any kind — reads
/// <see cref="LowConfidence"/>, so the "starts unaccepted" path is always on
/// screen.
/// </para>
/// </summary>
public sealed class FakeInboxTriageAdvisor : IInboxTriageAdvisor
{
    /// <summary>The configuration value that swaps this advisor in.</summary>
    public const string ConfigurationKey = "Inbox:FakeTriageAdvisor";

    /// <summary>How many words a title must share to read as a duplicate.</summary>
    public const int SharedWords = 3;

    /// <summary>The one proposal per pass the reader sees unaccepted.</summary>
    public const double LowConfidence = 0.4;

    /// <summary>Every other proposal's confidence.</summary>
    public const double HighConfidence = 0.9;

    /// <summary>Words too common to say two titles are about the same thing.</summary>
    public static readonly IReadOnlySet<string> Filler =
        new HashSet<string>(["the", "and", "for", "with", "from", "into", "that", "this", "when", "then"], StringComparer.OrdinalIgnoreCase);

    public bool IsAvailable => true;

    public Task<Result<InboxTriageAdviceDto>> AdviseAsync(
        InboxTriageItemDto item,
        InboxTriageContextDto context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(context);

        var duplicate = DuplicateOf(item, context.OpenTasks, context.OtherItems);

        InboxTriagePlanGroupingDto? plan = null;
        if (SharedTag(item, context.OtherItems) is { } tag)
        {
            var members = context.OtherItems.Where(other => HasTag(other, tag)).Select(other => other.Id).Prepend(item.Id).ToList();
            plan = new InboxTriagePlanGroupingDto(PlanTitle(tag), members, $"These captures are all tagged {tag}.");
        }

        var repositories = item.RepoIds.Count > 0
            ? item.RepoIds
            : duplicate is { TargetKind: InboxTriageTargetKind.Task } task
                ? context.OpenTasks.First(open => open.Id == task.TargetId).RepoIds
                : [];

        return Task.FromResult(Result.Success(new InboxTriageAdviceDto(item.Id, duplicate, plan, repositories)));
    }

    public Task<Result<InboxTriagePassDto>> ProposePassAsync(
        IReadOnlyList<InboxTriageItemDto> items,
        InboxTriageContextDto context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(context);

        var placed = new HashSet<Guid>();
        var duplicates = new List<InboxTriageDuplicatePairDto>();
        var plans = new List<InboxTriagePlanProposalDto>();
        var filings = new List<InboxTriageListFilingDto>();
        var routes = new List<InboxTriageRouteProposalDto>();
        var archives = new List<InboxTriageArchiveDto>();

        foreach (var item in items)
        {
            if (placed.Contains(item.Id)) continue;

            var rest = items.Where(other => other.Id != item.Id && !placed.Contains(other.Id)).Concat(context.OtherItems).ToList();
            if (DuplicateOf(item, context.OpenTasks, rest) is not { } duplicate) continue;

            placed.Add(item.Id);
            if (duplicate.TargetKind == InboxTriageTargetKind.InboxItem) placed.Add(duplicate.TargetId);

            duplicates.Add(new InboxTriageDuplicatePairDto(
                item.Id,
                duplicate.TargetKind,
                duplicate.TargetId,
                duplicate.TargetKind == InboxTriageTargetKind.Task ? InboxDuplicateAction.MergeIntoTask : InboxDuplicateAction.KeepOneAttachOther,
                duplicate.Reason,
                HighConfidence));
        }

        foreach (var item in items)
        {
            if (placed.Contains(item.Id)) continue;

            var open = items.Where(other => !placed.Contains(other.Id)).ToList();
            if (SharedTag(item, open.Where(other => other.Id != item.Id)) is not { } tag) continue;

            var members = open.Where(other => HasTag(other, tag)).ToList();
            placed.UnionWith(members.Select(member => member.Id));
            plans.Add(new InboxTriagePlanProposalDto(
                PlanTitle(tag),
                [.. members.Select(member => member.Id)],
                [.. members.SelectMany(member => member.RepoIds).Distinct(StringComparer.OrdinalIgnoreCase)],
                $"These captures are all tagged {tag}.",
                HighConfidence));
        }

        foreach (var item in items.Where(item => !placed.Contains(item.Id)))
        {
            if (context.Lists.FirstOrDefault(list => item.Tags.Contains(list.Name, StringComparer.OrdinalIgnoreCase)) is { } list)
            {
                placed.Add(item.Id);
                filings.Add(new InboxTriageListFilingDto(item.Id, list.Id, $"It is tagged {list.Name}, which is one of your lists.", HighConfidence));
            }
            else if (item.RepoIds.Count > 0)
            {
                placed.Add(item.Id);
                routes.Add(new InboxTriageRouteProposalDto(item.Id, "prompt", item.RepoIds, "It names a repository, so an agent can carry it out there.", HighConfidence));
            }
            else if (!string.IsNullOrWhiteSpace(item.BodyMd) || !string.IsNullOrWhiteSpace(item.SourceUrl))
            {
                placed.Add(item.Id);
                routes.Add(new InboxTriageRouteProposalDto(item.Id, "task", [], "It has notes or a link but no repository, so it is a step for you.", HighConfidence));
            }
            else if (item.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 2)
            {
                placed.Add(item.Id);
                archives.Add(new InboxTriageArchiveDto(item.Id, "A bare title of two words is too little to act on.", HighConfidence));
            }
        }

        // One proposal per pass reads low, so the unaccepted path is on screen.
        if (routes.Count > 0) routes[0] = routes[0] with { Confidence = LowConfidence };
        else if (plans.Count > 0) plans[0] = plans[0] with { Confidence = LowConfidence };
        else if (duplicates.Count > 0) duplicates[0] = duplicates[0] with { Confidence = LowConfidence };
        else if (filings.Count > 0) filings[0] = filings[0] with { Confidence = LowConfidence };
        else if (archives.Count > 0) archives[0] = archives[0] with { Confidence = LowConfidence };

        var unplaced = items.Where(item => !placed.Contains(item.Id)).Select(item => item.Id).ToList();

        return Task.FromResult(Result.Success(new InboxTriagePassDto(plans, duplicates, routes, filings, archives, unplaced)));
    }

    /// <summary>The words of a title that count: three or more letters or
    /// digits, lower-cased, filler left out.</summary>
    public static IReadOnlySet<string> Words(string title) =>
        new HashSet<string>(
            new string([.. (title ?? string.Empty).Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ')])
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(word => word.Length >= 3 && !Filler.Contains(word)),
            StringComparer.Ordinal);

    private static InboxTriageDuplicateDto? DuplicateOf(
        InboxTriageItemDto item,
        IEnumerable<InboxTriageTaskDto> tasks,
        IEnumerable<InboxTriageItemDto> others)
    {
        var words = Words(item.Title);

        if (tasks.FirstOrDefault(task => Shared(words, task.Title) >= SharedWords) is { } task)
        {
            return new InboxTriageDuplicateDto(InboxTriageTargetKind.Task, task.Id, task.Title, $"It asks for the same work as the task \"{task.Title}\".");
        }

        if (others.FirstOrDefault(other => other.Id != item.Id && Shared(words, other.Title) >= SharedWords) is { } twin)
        {
            return new InboxTriageDuplicateDto(InboxTriageTargetKind.InboxItem, twin.Id, twin.Title, $"It is the same capture as \"{twin.Title}\".");
        }

        return null;
    }

    private static int Shared(IReadOnlySet<string> words, string title) => Words(title).Count(words.Contains);

    /// <summary>The first of the item's tags another item also carries.</summary>
    private static string? SharedTag(InboxTriageItemDto item, IEnumerable<InboxTriageItemDto> others)
    {
        var rest = others.ToList();
        return item.Tags.FirstOrDefault(tag => rest.Any(other => HasTag(other, tag)));
    }

    private static bool HasTag(InboxTriageItemDto item, string tag) =>
        item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);

    private static string PlanTitle(string tag) => $"Plan: {tag}";
}
