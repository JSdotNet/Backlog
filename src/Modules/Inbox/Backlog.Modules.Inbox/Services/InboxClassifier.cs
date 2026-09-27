using System.Text.RegularExpressions;

using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.DomainModels;

namespace Backlog.Modules.Inbox.Services;

/// <summary>
/// The Classification domain service's suggestions: the tags, repositories and
/// destination an open item looks like it wants, each with the reason it was
/// proposed.
/// <para>
/// Only ever proposals. Nothing here writes to the item; the pane shows each one
/// as a chip the reader accepts or turns down, and a suggestion turned down is
/// recorded on the item (<see cref="InboxItem.DismissSuggestion"/>) and left out
/// from then on. A heuristic, deterministic and named as one — like
/// <see cref="ContentKindDetector"/> — because a suggestion the reader can see the
/// reason for is one they can trust or dismiss at a glance.
/// </para>
/// <list type="bullet">
/// <item><b>Tags.</b> A <c>#word</c> written in the title or body, and any tag
/// the backlog already files entries under that the text mentions as a whole
/// word — so a capture about "sync" is offered the backlog's own <c>#sync</c>
/// rather than a new spelling of it.</item>
/// <item><b>Repositories.</b> Every repository a routing rule names whose pattern
/// the item matches, in the order the rules are written.</item>
/// <item><b>Destination.</b> One: archive for a newsletter, the backlog for an
/// item already assigned a repository or a note of the reader's own, and
/// knowledge for collected material.</item>
/// </list>
/// </summary>
internal static partial class InboxClassifier
{
    /// <summary>How many tag chips an item is offered at most: enough for the
    /// words that matter, few enough that the row stays a glance.</summary>
    internal const int MaxTagSuggestions = 5;

    internal const string KnowledgeNotBuilt = "Keeping an item as knowledge is not built yet.";

    /// <summary>The kinds that are a thought of the reader's own rather than
    /// collected material — see <c>Content Kind</c> in the domain chapter, which
    /// names every other kind a reference kind.</summary>
    private static readonly HashSet<ContentKind> OwnKinds = [ContentKind.Text, ContentKind.Code, ContentKind.Voice];

    public static string TagKey(string tag) => "tag:" + tag.Trim().TrimStart('#').ToLowerInvariant();

    public static string RepositoryKey(string repoId) => "repository:" + repoId.Trim().ToLowerInvariant();

    public static string DestinationKey(RoutingDomain domain) => "destination:" + InboxEnumMap.ToWire(domain);

    /// <summary>What to propose for <paramref name="item"/>: tags first, then
    /// repositories, then the destination. Empty for an item already decided —
    /// a routed or archived item's tags and repositories are what that decision
    /// was made with.</summary>
    public static IReadOnlyList<InboxSuggestionDto> Suggest(
        InboxItem item,
        IReadOnlyList<string> backlogTags,
        IReadOnlyList<InboxRoutingRule> rules)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(backlogTags);
        ArgumentNullException.ThrowIfNull(rules);

        if (!item.IsOpen || item.IsRouted) return [];

        var suggestions = new List<InboxSuggestionDto>();
        suggestions.AddRange(Tags(item, backlogTags));
        suggestions.AddRange(Repositories(item, rules));
        if (Destination(item) is { } destination) suggestions.Add(destination);

        return suggestions;
    }

    // --- Tags ----------------------------------------------------------------

    private static IEnumerable<InboxSuggestionDto> Tags(InboxItem item, IReadOnlyList<string> backlogTags)
    {
        var text = item.Title + "\n" + item.BodyMd;
        var offered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var known = backlogTags
            .Select(tag => (tag ?? string.Empty).Trim().TrimStart('#'))
            .Where(tag => tag.Length > 1 && char.IsLetter(tag[0]))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var results = new List<InboxSuggestionDto>();

        foreach (Match match in HashtagPattern().Matches(text))
        {
            var written = match.Groups["tag"].Value.TrimEnd('-', '_');
            // The backlog's spelling when it has the word, so accepting the chip
            // files the item under the tag the backlog already uses.
            var name = known.FirstOrDefault(tag => string.Equals(tag, written, StringComparison.OrdinalIgnoreCase)) ?? written;

            Offer(name, $"The item says #{written}.");
        }

        foreach (var tag in known)
        {
            if (MentionsWord(text, tag)) Offer(tag, $"The backlog files entries under #{tag}, and the item mentions it.");
        }

        return results.Take(MaxTagSuggestions);

        void Offer(string name, string reason)
        {
            if (name.Length == 0 || !offered.Add(name)) return;
            if (item.Tags.Any(tag => string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase))) return;

            var key = TagKey(name);
            if (item.IsSuggestionDismissed(key)) return;

            results.Add(new InboxSuggestionDto(key, InboxSuggestionKind.Tag, name, reason));
        }
    }

    /// <summary>Whether the text holds the tag as a whole word — or, for a tag
    /// of several words (<c>release-notes</c>), those words in order with any
    /// run of spaces, hyphens or underscores between them.</summary>
    private static bool MentionsWord(string text, string tag)
    {
        var words = tag.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return false;

        var pattern = @"(?<![\p{L}\p{N}_])" + string.Join(@"[\s_-]+", words.Select(Regex.Escape)) + @"(?![\p{L}\p{N}_])";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    }

    /// <summary>A <c>#word</c> that opens with a letter and is not the tail of a
    /// link or of another word — <c>page#section</c> and <c>/#top</c> are anchors,
    /// <c>#123</c> an issue number, and a markdown heading has a space after its
    /// hashes.</summary>
    [GeneratedRegex(@"(?<![\p{L}\p{N}_#/&])#(?<tag>\p{L}[\p{L}\p{N}_-]*)", RegexOptions.CultureInvariant)]
    private static partial Regex HashtagPattern();

    // --- Repositories -----------------------------------------------------------

    private static IEnumerable<InboxSuggestionDto> Repositories(InboxItem item, IReadOnlyList<InboxRoutingRule> rules)
    {
        var offered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules)
        {
            if (!Matches(rule, item)) continue;
            if (item.RepoIds.Any(id => string.Equals(id, rule.Repository, StringComparison.OrdinalIgnoreCase))) continue;
            if (!offered.Add(rule.Repository)) continue;

            var key = RepositoryKey(rule.Repository);
            if (item.IsSuggestionDismissed(key)) continue;

            yield return new InboxSuggestionDto(key, InboxSuggestionKind.Repository, rule.Repository, $"Your rule {rule}.");
        }
    }

    /// <summary>Whether the rule's pattern matches the item: a tag rule any tag
    /// it carries, a person rule the person who shared it, and a source rule the
    /// channel or the source link. The tags read are the ones the item carries,
    /// never the ones only suggested — accepting a tag is what brings its rule in.</summary>
    internal static bool Matches(InboxRoutingRule rule, InboxItem item) => rule.Target switch
    {
        InboxRoutingRuleTarget.Tag => item.Tags.Any(tag => rule.Matches(tag.Name)),
        InboxRoutingRuleTarget.Person => rule.Matches(item.Source.Person?.TrimStart('@')),
        _ => rule.Matches(item.Source.Channel) || rule.Matches(item.SourceUrl),
    };

    // --- Destination --------------------------------------------------------------

    /// <summary>The one decision the item looks like it wants, or null when the
    /// reader turned that one down. Never a second guess after a refusal: a
    /// different destination offered the moment one was dismissed would read as
    /// the dismissed suggestion coming back in another form.</summary>
    private static InboxSuggestionDto? Destination(InboxItem item)
    {
        var (domain, reason) = DestinationFor(item);
        var key = DestinationKey(domain);
        if (item.IsSuggestionDismissed(key)) return null;

        return new InboxSuggestionDto(key, InboxSuggestionKind.Destination, InboxEnumMap.ToWire(domain), reason)
        {
            UnavailableReason = domain == RoutingDomain.Devbook ? KnowledgeNotBuilt : null
        };
    }

    private static (RoutingDomain Domain, string Reason) DestinationFor(InboxItem item)
    {
        if (item.Title.Contains("unsubscribe", StringComparison.OrdinalIgnoreCase)
            || item.BodyMd.Contains("unsubscribe", StringComparison.OrdinalIgnoreCase))
        {
            return (RoutingDomain.Archive, "It reads like a newsletter: it offers to unsubscribe.");
        }

        if (item.RepoIds.Count > 0)
        {
            return (RoutingDomain.Tasks, "It is assigned to a repository, so it is work for the backlog.");
        }

        if (OwnKinds.Contains(item.Kind))
        {
            return (RoutingDomain.Tasks, "It is a note of your own rather than collected material.");
        }

        return (RoutingDomain.Devbook, $"It is collected material — {Article(item.KindSlug)} — to keep rather than do.");
    }

    private static string Article(string slug)
    {
        var word = slug.Replace('-', ' ');
        return ("aeiou".Contains(char.ToLowerInvariant(word[0])) ? "an " : "a ") + word;
    }
}
