using System.Text.RegularExpressions;
using Backlog.UI.Components.Markdown;

namespace Backlog.UI.Components.Diagrams;

/// <summary>One domain file of a bounded context, as the Model page reads it:
/// <c>domain.md</c> or a <c>domain.&lt;name&gt;.md</c> split, with its path as the
/// devbook spells it so a <c>related</c> link can be matched to it.</summary>
public sealed record DomainChapterSource(string Path, string Markdown);

/// <summary>A domain-service chapter as Fields shows it: what it is, and how it runs.</summary>
/// <param name="Summary">The chapter's first sentence.</param>
/// <param name="Invocation">The chapter's <em>Invocation semantics</em> prose, as
/// markdown, without the label — or null when the chapter does not say how it runs.</param>
public sealed record DomainServiceInvocation(string Title, string Anchor, string Summary, string? Invocation);

/// <summary>Whether an event is only described or actually raised, as the
/// chapters say.</summary>
public enum DomainEventDelivery
{
    /// <summary>The chapter describes the contract; nothing says it is raised, or a
    /// chapter says it is not raised yet.</summary>
    Documented,

    /// <summary>The chapter that raises it says it emits or publishes it.</summary>
    Emitted
}

/// <summary>A <c>domain-event</c> chapter read as a contract.</summary>
/// <param name="RaisedBy">The aggregates and domain services that raise it — the
/// chapters its <c>related</c> field names, or its own "Published by" sentence.</param>
/// <param name="Payload">The <em>Payload</em> sub-chapter as markdown, or null.</param>
/// <param name="Consumers">The <em>Consumers</em> sub-chapter as markdown, or null.</param>
/// <param name="Rules">The <em>Published language rules</em> sub-chapter as markdown, or null.</param>
public sealed record DomainEventContract(
    string Title,
    string Anchor,
    string Status,
    string Summary,
    IReadOnlyList<string> RaisedBy,
    string? Payload,
    string? Consumers,
    string? Rules,
    DomainEventDelivery Delivery);

/// <summary>The technical half of a context's domain chapters: how each domain
/// service runs and what each event carries (local ADR 0037 §6).</summary>
public sealed record DomainChapterReading(IReadOnlyList<DomainServiceInvocation> Services, IReadOnlyList<DomainEventContract> Events)
{
    public static DomainChapterReading Empty { get; } = new([], []);
}

/// <summary>
/// Reads a bounded context's domain files for the Model page: each
/// <c>domain-service</c> chapter's invocation prose, and each <c>domain-event</c>
/// chapter as a contract. Nothing is asked of the chapters beyond what the devbook
/// convention already writes — a <c>meta</c> block with a <c>type</c>, the
/// <em>Invocation semantics</em> sentence, and an event's <em>Payload</em>,
/// <em>Consumers</em> and <em>Published language rules</em> sub-chapters.
/// </summary>
public static partial class DomainChapterContracts
{
    private sealed record Chapter(string? Folder, string File, string Title, string Anchor, string Type, string Status, IReadOnlyList<string> Related, string Own, IReadOnlyDictionary<string, string> Subchapters, string Text);

    public static DomainChapterReading Read(IEnumerable<DomainChapterSource> sources)
    {
        var chapters = sources.SelectMany(Chapters).ToList();
        if (chapters.Count == 0) return DomainChapterReading.Empty;

        var services = chapters
            .Where(c => c.Type == "domain-service")
            .Select(c => new DomainServiceInvocation(c.Title, c.Anchor, FirstSentence(c.Own), Invocation(c.Own)))
            .ToList();

        // A context that says it publishes no events yet has said it of every event.
        var noneEmitted = chapters.Any(c => PublishesNone().IsMatch(c.Text));

        var events = chapters
            .Where(c => c.Type == "domain-event")
            .Select(c =>
            {
                var raisers = RaisedBy(c, chapters);
                return new DomainEventContract(
                    c.Title,
                    c.Anchor,
                    c.Status,
                    FirstSentence(c.Own),
                    [.. raisers.Select(r => r.Title)],
                    Subchapter(c, "payload"),
                    Subchapter(c, "consumers"),
                    Subchapter(c, "published language rules") ?? Subchapter(c, "published language"),
                    noneEmitted ? DomainEventDelivery.Documented : Delivery(c, raisers));
            })
            .ToList();

        return new DomainChapterReading(services, events);
    }

    private static IEnumerable<Chapter> Chapters(DomainChapterSource source)
    {
        var file = FileName(source.Path);
        var folder = FolderName(source.Path);
        foreach (var chapter in MarkdownChapters.Split(source.Markdown))
        {
            if (chapter.Level < 2) continue;

            var lines = chapter.Text.Split('\n').Skip(1).ToList();
            var meta = ReadMeta(lines);
            if (!meta.TryGetValue("type", out var type)) continue;

            var body = WithoutFences(lines);
            var own = body.TakeWhile(line => HeadingLine().Match(line) is not { Success: true } h || h.Groups[1].Length <= chapter.Level).ToList();

            var subchapters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sub in MarkdownChapters.Split(string.Join('\n', body)).Where(s => s.Level == chapter.Level + 1))
            {
                var subBody = string.Join('\n', sub.Text.Split('\n').Skip(1)).Trim();
                if (subBody.Length > 0) subchapters.TryAdd(sub.Title.Trim(), subBody);
            }

            yield return new Chapter(
                folder,
                file,
                chapter.Title,
                $"{source.Path}#{Slug(chapter.Title)}",
                type.Trim().ToLowerInvariant(),
                meta.TryGetValue("status", out var status) ? status.Trim() : "draft",
                meta.TryGetValue("related", out var related) ? List(related) : [],
                string.Join('\n', own).Trim(),
                subchapters,
                string.Join('\n', body));
        }
    }

    /// <summary>The chapters that raise the event: what its <c>related</c> field
    /// names among the context's aggregates, entities and services, and failing that
    /// the backticked name in its "Published by" or "Published when a" sentence.</summary>
    private static List<Chapter> RaisedBy(Chapter @event, IReadOnlyList<Chapter> chapters)
    {
        static bool CanRaise(Chapter c) => c.Type is "aggregate" or "entity" or "domain-service";

        var raisers = new List<Chapter>();
        foreach (var link in @event.Related)
        {
            var hash = link.IndexOf('#');
            if (hash < 0) continue;

            var anchor = link[(hash + 1)..];
            var linkFile = hash == 0 ? @event.File : FileName(link[..hash]);
            // A link with a folder names its context; one without is this one's.
            var linkFolder = hash == 0 ? null : FolderName(link[..hash]);
            var target = chapters.FirstOrDefault(c => CanRaise(c)
                && string.Equals(c.File, linkFile, StringComparison.OrdinalIgnoreCase)
                && (linkFolder is null || string.Equals(c.Folder, linkFolder, StringComparison.OrdinalIgnoreCase))
                && string.Equals(Slug(c.Title), anchor, StringComparison.OrdinalIgnoreCase));
            if (target is not null && !raisers.Contains(target)) raisers.Add(target);
        }

        if (raisers.Count > 0) return raisers;

        foreach (Match match in PublishedBy().Matches(@event.Own))
        {
            var name = match.Groups["name"].Value;
            var target = chapters.FirstOrDefault(c => CanRaise(c) && string.Equals(Compact(c.Title), Compact(name), StringComparison.OrdinalIgnoreCase));
            if (target is not null && !raisers.Contains(target)) raisers.Add(target);
        }

        return raisers;
    }

    /// <summary>Emitted only when a chapter says so — the raiser emits or publishes
    /// it by name — and no chapter says it is not raised yet.</summary>
    private static DomainEventDelivery Delivery(Chapter @event, IReadOnlyList<Chapter> raisers)
    {
        if (NotEmitted().IsMatch(@event.Text) || raisers.Any(r => NotEmitted().IsMatch(r.Text))) return DomainEventDelivery.Documented;

        var name = Regex.Escape(@event.Title);
        var emits = new Regex($@"\b(emits|emitting|publishes|publishing|raises|raising)\b[^.]*?\b{name}\b", RegexOptions.IgnoreCase);
        return raisers.Any(r => emits.IsMatch(r.Text)) ? DomainEventDelivery.Emitted : DomainEventDelivery.Documented;
    }

    private static string? Subchapter(Chapter chapter, string title) =>
        chapter.Subchapters.TryGetValue(title, out var body) ? body : null;

    /// <summary>The <em>Invocation semantics</em> prose: from the label to the end of
    /// its paragraph, and the list beneath it when the label ends its line.</summary>
    private static string? Invocation(string own)
    {
        var lines = own.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var match = InvocationLabel().Match(lines[i]);
            if (!match.Success) continue;

            var collected = new List<string> { lines[i][(match.Index + match.Length)..].Trim() };
            for (var j = i + 1; j < lines.Length; j++)
            {
                var line = lines[j];
                if (line.Trim().Length == 0)
                {
                    // A label that ends its line introduces what follows; a list
                    // carries on past its blank lines.
                    var next = lines.Skip(j + 1).FirstOrDefault(l => l.Trim().Length > 0);
                    var empty = string.Join(' ', collected).Trim().Length == 0;
                    if (next is not null && (empty || ListItem().IsMatch(next))) { collected.Add(string.Empty); continue; }
                    break;
                }

                collected.Add(line);
            }

            var text = string.Join('\n', collected).Trim();
            return text.Length == 0 ? null : char.ToUpperInvariant(text[0]) + text[1..];
        }

        return null;
    }

    private static string FirstSentence(string own)
    {
        var paragraph = string.Join(' ', own.Split('\n')
            .Select(l => l.Trim())
            .SkipWhile(l => l.Length == 0 || l.StartsWith('>'))
            .TakeWhile(l => l.Length > 0));
        var match = Sentence().Match(paragraph);
        return (match.Success ? match.Value : paragraph).Trim();
    }

    private static Dictionary<string, string> ReadMeta(List<string> lines)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        while (index < lines.Count && lines[index].Trim().Length == 0) index++;
        if (index >= lines.Count || !string.Equals(lines[index].Trim(), "```meta", StringComparison.OrdinalIgnoreCase)) return result;

        for (index++; index < lines.Count && lines[index].Trim() != "```"; index++)
        {
            var colon = lines[index].IndexOf(':');
            if (colon > 0) result.TryAdd(lines[index][..colon].Trim(), lines[index][(colon + 1)..].Trim());
        }

        return result;
    }

    /// <summary>The body without its <c>meta</c> and <c>annotation</c> fences: the
    /// one is the chapter's record, the other a review note, and neither is prose.</summary>
    private static List<string> WithoutFences(List<string> lines)
    {
        var result = new List<string>();
        var skipping = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!skipping && (trimmed.Equals("```meta", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("```annotation", StringComparison.OrdinalIgnoreCase)))
            {
                skipping = true;
                continue;
            }

            if (skipping)
            {
                if (trimmed == "```") skipping = false;
                continue;
            }

            result.Add(line);
        }

        return result;
    }

    private static IReadOnlyList<string> List(string value) =>
        [.. value.Trim().TrimStart('[').TrimEnd(']').Split(',').Select(item => item.Trim().Trim('"', '\'').Trim()).Where(item => item.Length > 0)];

    /// <summary>The folder a path sits in — the context's slug — or null for a
    /// bare file name, which is the linking chapter's own context.</summary>
    private static string? FolderName(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 || parts[^2] is "." ? null : parts[^2];
    }

    private static string FileName(string path)
    {
        var trimmed = path.Replace('\\', '/');
        var slash = trimmed.LastIndexOf('/');
        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }

    /// <summary>A heading's anchor as the devbook writes one: lower case, words
    /// joined by hyphens, punctuation dropped.</summary>
    internal static string Slug(string title)
    {
        var lower = title.Trim().ToLowerInvariant();
        var kept = new string([.. lower.Where(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_')]);
        return Regex.Replace(kept, @"\s", "-");
    }

    private static string Compact(string value) => new([.. value.Where(char.IsLetterOrDigit)]);

    [GeneratedRegex(@"^(#{1,6})[ \t]+\S")]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"Invocation semantics\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex InvocationLabel();

    [GeneratedRegex(@"^\s*([-*+]|\d+\.)\s")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^.*?[.!?](?=\s|$)")]
    private static partial Regex Sentence();

    [GeneratedRegex(@"publishes no (domain )?events", RegexOptions.IgnoreCase)]
    private static partial Regex PublishesNone();

    [GeneratedRegex(@"documented rather than emitted|\bnot (yet )?(emitted|published|raised)\b|\b(emitted|published|raised) yet\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotEmitted();

    [GeneratedRegex(@"Published (by|when an?)\s+`(?<name>[^`]+)`", RegexOptions.IgnoreCase)]
    private static partial Regex PublishedBy();
}
