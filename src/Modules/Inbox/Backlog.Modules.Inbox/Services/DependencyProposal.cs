using System.Globalization;
using System.Text.RegularExpressions;

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
/// naming the other.
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
            var links = Links(text);
            var offered = new HashSet<(DependencyTargetKind, Guid)>();

            foreach (var other in batch.Where(other => other.Id != item.Id))
            {
                if (other.SourceUrl is { } source && Written(links, source) is { } written)
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
                if (NamedIssue(text, links, task, item.RepoIds) is { } issue) Offer(DependencyTarget.ForTask(task), issue);
            }

            foreach (var task in tasks)
            {
                if (task.Links.Select(link => Written(links, link)).FirstOrDefault(written => written is not null) is { } written)
                {
                    Offer(DependencyTarget.ForTask(task), written);
                }
            }

            void Offer(DependencyTarget to, string reason)
            {
                if (to.Kind == DependencyTargetKind.Item && to.Id == item.Id) return;
                if (!offered.Add((to.Kind, to.Id))) return;

                proposed.Add(new ProposedDependency(item.Id, to, reason, DependencyTier.Stated));
            }
        }

        return proposed;
    }

    // --- Links -----------------------------------------------------------------

    /// <summary>A link written in the text, as written and as compared.</summary>
    private readonly record struct Link(string Written, string Key);

    /// <summary>Every <c>http(s)</c> link in the text, trailing sentence
    /// punctuation left off — "see https://example.com/a." links to <c>/a</c>.
    /// A markdown link's brackets and a quoted link's quotes end it too.</summary>
    private static List<Link> Links(string text) =>
        [.. LinkPattern().Matches(text)
            .Select(match => match.Value.TrimEnd('.', ',', ';', ':', '!', '?'))
            .Where(written => written.Length > 0)
            .Select(written => new Link(written, Key(written)))];

    /// <summary>How two links are compared: without case, without a fragment,
    /// and without a trailing slash, so <c>…/issues/12#issuecomment-3</c> and
    /// <c>…/issues/12/</c> both name issue 12 — and <c>…/issues/123</c> does not.</summary>
    private static string Key(string url)
    {
        var key = url.Trim();
        var fragment = key.IndexOf('#', StringComparison.Ordinal);
        if (fragment >= 0) key = key[..fragment];

        return key.TrimEnd('/').ToLowerInvariant();
    }

    /// <summary>The text's own spelling of <paramref name="url"/>, or null when
    /// the text does not link to it.</summary>
    private static string? Written(List<Link> links, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var key = Key(url);
        return links.FirstOrDefault(link => string.Equals(link.Key, key, StringComparison.Ordinal)).Written;
    }

    [GeneratedRegex(@"https?://[^\s<>()\[\]{}""'`]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkPattern();

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

    // --- Issues ----------------------------------------------------------------

    /// <summary>The text naming one of the task's issues — <c>#123</c> for an
    /// issue in one of the item's repositories, or the issue's GitHub URL for
    /// any — or null.</summary>
    private static string? NamedIssue(string text, List<Link> links, InboxTaskReferenceDto task, IReadOnlyList<string> itemRepos)
    {
        foreach (var issue in task.Issues)
        {
            if (itemRepos.Contains(issue.Repo, StringComparer.OrdinalIgnoreCase))
            {
                foreach (Match match in IssueNumberPattern().Matches(text))
                {
                    if (int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                        && number == issue.Number)
                    {
                        return match.Value;
                    }
                }
            }

            foreach (var link in links)
            {
                var url = IssueUrlPattern().Match(link.Key);
                if (url.Success
                    && string.Equals(url.Groups["repo"].Value, issue.Repo, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(url.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                    && number == issue.Number)
                {
                    return link.Written;
                }
            }
        }

        return null;
    }

    /// <summary>A <c>#123</c> that is not the tail of a word, a link or an
    /// entity — <c>page#12</c> is an anchor and <c>&amp;#12;</c> a character.</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}_&/#])#(?<number>[0-9]+)(?![\p{L}\p{N}_])", RegexOptions.CultureInvariant)]
    private static partial Regex IssueNumberPattern();

    /// <summary>A GitHub issue URL, already compared-form (lower case, no
    /// fragment, no trailing slash).</summary>
    [GeneratedRegex(@"^https?://(?:www\.)?github\.com/(?<repo>[a-z0-9._-]+/[a-z0-9._-]+)/issues/(?<number>[0-9]+)(?:[/?].*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex IssueUrlPattern();
}
