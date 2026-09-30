using System.Text.RegularExpressions;

using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.DomainModels;

namespace Backlog.Modules.Inbox.Services;

/// <summary>
/// The Dependency Proposal domain service: the dependencies a batch's items
/// <em>state</em> — between each other, and on open tasks the backlog already
/// holds — each with the text that stated it.
/// <para>
/// Only ever proposals, as <see cref="InboxClassifier"/>'s suggestions are. A
/// dependency is a Tasks fact; this reads text and says what it names, the
/// person turns each one on or off in the panel, and nothing here is stored.
/// Deterministic and pure, so the reason on screen is the whole reason.
/// </para>
/// <para>
/// Tier one only (<see cref="DependencyTier.Stated"/>): item <c>A</c> comes after
/// <c>B</c> when <c>A</c>'s title or notes carry, in this order of rules —
/// </para>
/// <list type="number">
/// <item><b>B's source link</b>, for <c>B</c> in the batch. The reason is the
/// link as <c>A</c> wrote it.</item>
/// <item><b>B's title</b>, for <c>B</c> in the batch — whole words, any case,
/// and only a title of at least <see cref="MinTitleWords"/> words and
/// <see cref="MinTitleLength"/> characters, because "Fix" or "Notes" would match
/// half the inbox. The reason is the text that matched.</item>
/// <item><b>An issue an open task was filed as</b>: <c>#123</c> when the task's
/// repository is one of <c>A</c>'s, or the issue's GitHub URL wherever it
/// lives. The reason is the text that matched.</item>
/// <item><b>An open task's link</b>: its issue or pull request URL, or the
/// source link it was captured from. The reason is the link as written.</item>
/// </list>
/// <para>
/// One dependency per pair; the first rule to match gives the reason. An item
/// never waits on itself, and two items with the same title do not wait on each
/// other for it — the same words are the same thought captured twice, not one
/// naming the other. The same goes for any two items that are directly
/// duplicates (<see cref="DuplicateReason"/>): the same link or nearly the same
/// title is one capture made twice, which the panel offers to merge rather than
/// chain. Only the pair itself: two items that are each a duplicate of a third
/// are not, for that, duplicates of each other, and may still wait on one another.
/// </para>
/// <para>
/// Beneath the tier-one rules sit the <see cref="Hints"/> — same list, same
/// repository, capture order, setup first, duplicates — which are said and never
/// proposed: they are <see cref="OrderingHint"/>s, a type no route or order reads.
/// </para>
/// </summary>
internal static partial class DependencyProposal
{
    /// <summary>The fewest words a title needs before an item naming it counts.</summary>
    internal const int MinTitleWords = 2;

    /// <summary>The fewest characters a title needs, for the same reason.</summary>
    internal const int MinTitleLength = 8;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>Every stated dependency in <paramref name="batch"/>, grouped by
    /// the item that waits in the batch's order, and within one item by rule,
    /// then by the order the batch or the backlog lists its targets.
    /// <paramref name="tasks"/> are the backlog's open tasks; done and archived
    /// ones are not asked about and so are never a target.</summary>
    public static IReadOnlyList<ProposedDependency> Propose(
        IReadOnlyList<InboxItem> batch,
        IReadOnlyList<InboxTaskReferenceDto> tasks)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(tasks);

        var proposed = new List<ProposedDependency>();

        foreach (var item in batch)
        {
            var text = item.Title + "\n" + item.BodyMd;
            var links = InboxUrl.Links(text);
            var offered = new HashSet<(DependencyTargetKind, Guid)>();

            foreach (var other in batch.Where(other => other.Id != item.Id))
            {
                if (other.SourceUrl is { } source && InboxUrl.Written(links, source) is { } written)
                {
                    Offer(DependencyTarget.ForItem(other.Id, other.Title), written);
                }
            }

            foreach (var other in batch.Where(other => other.Id != item.Id))
            {
                if (string.Equals(other.Title, item.Title, StringComparison.OrdinalIgnoreCase)) continue;
                if (NamedTitle(text, other.Title) is { } named) Offer(DependencyTarget.ForItem(other.Id, other.Title), named);
            }

            foreach (var task in tasks)
            {
                if (IssueMention.Find(text, links, task, item.RepoIds) is { } issue) Offer(DependencyTarget.ForTask(task), issue.Written);
            }

            foreach (var task in tasks)
            {
                if (task.Links.Select(link => InboxUrl.Written(links, link)).FirstOrDefault(written => written is not null) is { } written)
                {
                    Offer(DependencyTarget.ForTask(task), written);
                }
            }

            void Offer(DependencyTarget to, string reason)
            {
                if (to.Kind == DependencyTargetKind.Item && to.Id == item.Id) return;
                if (to.Kind == DependencyTargetKind.Item && batch.FirstOrDefault(other => other.Id == to.Id) is { } target
                    && DuplicateReason(item, target) is not null) return;
                if (!offered.Add((to.Kind, to.Id))) return;

                proposed.Add(new ProposedDependency(item.Id, to, reason, DependencyTier.Stated));
            }
        }

        return proposed;
    }

    // --- Duplicates ------------------------------------------------------------

    /// <summary>Why two items of a batch are the same capture, or null when they
    /// are not: the same source link first, then nearly the same title.</summary>
    internal static string? DuplicateReason(InboxItem a, InboxItem b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (InboxUrl.Same(a.SourceUrl, b.SourceUrl)) return SameLinkReason;
        if (TitleSimilarity.IsNearIdentical(a.Title, b.Title)) return SimilarTitleReason;
        return null;
    }

    /// <summary>
    /// The batch's items that are the same capture, grouped around the one a
    /// merge would keep: in the batch's order, the first item not yet in a group
    /// starts one, and every later item not yet in a group that is a duplicate of
    /// <em>it</em> joins. Direct only, never through a third item — A sharing a
    /// link with B and B nearly a title with C does not make A and C one capture,
    /// and a merge keeping A must not archive C. Only groups of two or more,
    /// ordered by where each starts.
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<InboxItem>> DuplicateGroups(IReadOnlyList<InboxItem> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var grouped = new HashSet<Guid>();
        var groups = new List<IReadOnlyList<InboxItem>>();

        for (var i = 0; i < batch.Count; i++)
        {
            var kept = batch[i];
            if (grouped.Contains(kept.Id)) continue;

            var group = new List<InboxItem> { kept };
            for (var j = i + 1; j < batch.Count; j++)
            {
                var other = batch[j];
                if (other.Id == kept.Id || grouped.Contains(other.Id) || group.Any(member => member.Id == other.Id)) continue;
                if (DuplicateReason(kept, other) is not null) group.Add(other);
            }

            if (group.Count < 2) continue;

            grouped.UnionWith(group.Select(member => member.Id));
            groups.Add(group);
        }

        return groups;
    }

    // --- Hints -----------------------------------------------------------------

    internal const string SameLinkReason = "Same link";
    internal const string SimilarTitleReason = "Nearly the same title";
    internal const string EitherDuplicateReason = "Same link or nearly the same title";
    internal const string CaptureOrderReason = "Captured in another order";
    internal const string SetupFirstReason = "Reads like setup, so it may belong ahead of the rest";

    /// <summary>
    /// What the Inbox notices about the order <paramref name="batch"/> could go
    /// in, beyond what its items state — tier two, said and never written.
    /// Nothing for a batch of fewer than two items. In this order:
    /// <list type="number">
    /// <item><b>Duplicates</b>: each <see cref="DuplicateGroups"/> group, the one
    /// a merge would keep first. The only hint the panel can act on.</item>
    /// <item><b>Setup first</b>: an item whose title reads like setup
    /// (<see cref="IsSetupShaped"/>) that the proposed order puts behind one that
    /// does not.</item>
    /// <item><b>Same list</b> and <b>same repository</b>: two or more items
    /// sharing one — but not every item, because a hint true of the whole batch
    /// tells the person nothing about its order.</item>
    /// <item><b>Capture order</b>: the items in the order they were captured,
    /// only when that differs from the order proposed.</item>
    /// </list>
    /// <paramref name="dependencies"/> are the ones proposed, which give the
    /// proposed order; <paramref name="listNames"/> name the lists.
    /// </summary>
    public static IReadOnlyList<OrderingHint> Hints(
        IReadOnlyList<InboxItem> batch,
        IReadOnlyList<ProposedDependency> dependencies,
        IReadOnlyDictionary<Guid, string> listNames)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(listNames);

        var items = batch.DistinctBy(item => item.Id).ToList();
        if (items.Count < 2) return [];

        var hints = new List<OrderingHint>();

        foreach (var group in DuplicateGroups(items))
        {
            var reason = group.Skip(1).All(other => InboxUrl.Same(group[0].SourceUrl, other.SourceUrl))
                ? SameLinkReason
                : group.Skip(1).All(other => TitleSimilarity.IsNearIdentical(group[0].Title, other.Title))
                    ? SimilarTitleReason
                    : EitherDuplicateReason;
            hints.Add(new OrderingHint(OrderingHintKind.Duplicate, [.. group.Select(item => item.Id)], reason));
        }

        var ids = items.Select(item => item.Id).ToList();
        var proposed = InboxBatchOrder.Order(ids, InboxBatchOrder.Edges(dependencies));
        var byId = items.ToDictionary(item => item.Id);

        for (var at = 0; at < proposed.Count; at++)
        {
            var item = byId[proposed[at]];
            if (IsSetupShaped(item.Title) && proposed.Take(at).Any(earlier => !IsSetupShaped(byId[earlier].Title)))
            {
                hints.Add(new OrderingHint(OrderingHintKind.SetupFirst, [item.Id], SetupFirstReason));
            }
        }

        foreach (var list in items.Where(item => item.ListId is not null).GroupBy(item => item.ListId!.Value))
        {
            var members = list.ToList();
            if (members.Count < 2 || members.Count == items.Count) continue;

            var name = listNames.TryGetValue(list.Key, out var named) ? named : "a list";
            hints.Add(new OrderingHint(OrderingHintKind.SameList, [.. members.Select(item => item.Id)], $"Same list: {name}"));
        }

        var repositories = items
            .SelectMany(item => item.RepoIds.Select(repo => (Repo: repo, Item: item)))
            .GroupBy(pair => pair.Repo, StringComparer.OrdinalIgnoreCase);

        foreach (var repository in repositories)
        {
            var members = repository.Select(pair => pair.Item).DistinctBy(item => item.Id).ToList();
            if (members.Count < 2 || members.Count == items.Count) continue;

            hints.Add(new OrderingHint(
                OrderingHintKind.SameRepository,
                [.. members.Select(item => item.Id)],
                $"Same repository: {repository.First().Repo}"));
        }

        var captured = items.OrderBy(item => item.CapturedAt).Select(item => item.Id).ToList();
        if (!captured.SequenceEqual(proposed))
        {
            hints.Add(new OrderingHint(OrderingHintKind.CaptureOrder, captured, CaptureOrderReason));
        }

        return hints;
    }

    /// <summary>Whether a title reads like setup: it says set up, install,
    /// configure, scaffold, bootstrap, init or initialise, as whole words in any
    /// case — the work other work usually waits on.</summary>
    internal static bool IsSetupShaped(string? title) =>
        !string.IsNullOrWhiteSpace(title) && SetupPattern().IsMatch(title);

    [GeneratedRegex(
        @"(?<![\p{L}\p{N}_])(?:set[ -]?up|install(?:ing)?|configur(?:e|ing)|scaffold(?:ing)?|bootstrap(?:ping)?|init|initiali[sz](?:e|ing))(?![\p{L}\p{N}_])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SetupPattern();

    // --- Titles ----------------------------------------------------------------

    /// <summary>The text where it names <paramref name="title"/> as whole words,
    /// in any case and with any run of whitespace between them, or null — also
    /// for a title too short to be told apart from ordinary words.</summary>
    private static string? NamedTitle(string text, string title)
    {
        var trimmed = title.Trim();
        var words = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < MinTitleWords || trimmed.Length < MinTitleLength) return null;

        var pattern = @"(?<![\p{L}\p{N}_])" + string.Join(@"\s+", words.Select(Regex.Escape)) + @"(?![\p{L}\p{N}_])";

        try
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
            return match.Success ? match.Value : null;
        }
        catch (RegexMatchTimeoutException)
        {
            // The pattern is built from another item's title and run over this
            // one's notes, both of which the person or a feed wrote. A pathological
            // pair can outrun the timeout; that is no evidence either item names
            // the other, and a proposal is optional, so it proposes nothing
            // rather than failing the whole batch's panel.
            return null;
        }
    }
}
