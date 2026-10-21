using System.Text.RegularExpressions;
using Backlog.UI.Components.Markdown;

namespace Backlog.UI.Components.Diagrams;

/// <summary>One bullet of a model file's <em>Fields</em> chapter: the fields it
/// names, what they mean, and the invariants it points at.</summary>
/// <param name="Names">The backticked field names that open the bullet —
/// <c>`title` · `content_md`</c> is two.</param>
/// <param name="Meaning">The sentence after the dash, as markdown, without the
/// trailing <c>Rules:</c> clause.</param>
/// <param name="Rules">The targets of the links in the <c>Rules:</c> clause, as
/// written: <c>domain.invariants.md#how-a-task-moves</c>.</param>
public sealed record DomainFieldEntry(IReadOnlyList<string> Names, string Meaning, IReadOnlyList<string> Rules);

/// <summary>One purpose group: a <c>###</c> chapter under <em>Fields</em>, the
/// question its first paragraph asks, and its bullets in the order written.</summary>
public sealed record DomainFieldPurpose(string Title, string? Question, IReadOnlyList<DomainFieldEntry> Entries);

/// <summary>A model file's <em>Fields</em> chapter: <c>## Fields</c> for the
/// file's one aggregate root, or <c>## Task fields</c> naming the root it is for.</summary>
/// <param name="Root">The root the heading names, or null for a plain
/// <c>## Fields</c>.</param>
public sealed record DomainFieldChapter(string? Root, IReadOnlyList<DomainFieldPurpose> Purposes);

/// <summary>
/// Reads the purpose groups a model file writes for its aggregate root's fields —
/// the convention the Model page's Fields tab reads (local ADR 0037 §5: the
/// wording is the chapters'). The chapter is optional: a model file without one
/// is listed by what the types say instead.
/// <code>
/// ## Fields
///
/// ### Work state
///
/// Where is the work?
///
/// - `status` — The recorded lifecycle state. Rules: [follows the lifecycle](domain.invariants.md#invariant-status-changes-follow-the-lifecycle).
/// - `title` · `content_md` — The entry text.
/// </code>
/// </summary>
public static partial class DomainFieldPurposes
{
    public static IReadOnlyList<DomainFieldChapter> Read(string? markdown)
    {
        var result = new List<DomainFieldChapter>();
        foreach (var chapter in MarkdownChapters.Split(markdown))
        {
            if (chapter.Level != 2) continue;

            var title = chapter.Title.Trim();
            string? root;
            if (string.Equals(title, "Fields", StringComparison.OrdinalIgnoreCase)) root = null;
            else if (title.EndsWith(" fields", StringComparison.OrdinalIgnoreCase)) root = title[..^" fields".Length].Trim();
            else continue;

            var purposes = MarkdownChapters.Split(chapter.Text)
                .Where(sub => sub.Level == 3)
                .Select(Purpose)
                .ToList();
            result.Add(new DomainFieldChapter(root, purposes));
        }

        return result;
    }

    /// <summary>The chapter for <paramref name="root"/>: the one that names it, else
    /// a plain <c>## Fields</c>.</summary>
    public static DomainFieldChapter? For(IReadOnlyList<DomainFieldChapter> chapters, string root) =>
        chapters.FirstOrDefault(c => c.Root is not null && string.Equals(Squash(c.Root), Squash(root), StringComparison.OrdinalIgnoreCase))
        ?? chapters.FirstOrDefault(c => c.Root is null);

    /// <summary>A name with its spaces and hyphens gone: <em>Inbox Item</em> is <c>InboxItem</c>.</summary>
    private static string Squash(string name) => new([.. name.Where(char.IsLetterOrDigit)]);

    private static DomainFieldPurpose Purpose(MarkdownChapter chapter)
    {
        var lines = WithoutFences(chapter.Text.Split('\n').Skip(1));

        string? question = null;
        var paragraph = new List<string>();
        var bullets = new List<string>();
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                bullets.Add(line[2..].Trim());
                continue;
            }

            if (line.Length > 0 && char.IsWhiteSpace(raw[0]) && bullets.Count > 0)
            {
                bullets[^1] += " " + line.Trim();
                continue;
            }

            if (bullets.Count == 0 && question is null)
            {
                if (line.Length > 0) paragraph.Add(line.Trim());
                else if (paragraph.Count > 0) question = string.Join(' ', paragraph);
            }
        }

        if (question is null && paragraph.Count > 0) question = string.Join(' ', paragraph);

        return new DomainFieldPurpose(chapter.Title.Trim(), question, [.. bullets.Select(Entry).OfType<DomainFieldEntry>()]);
    }

    /// <summary>A bullet that opens with one or more backticked names and a dash;
    /// anything else is prose the tab does not list.</summary>
    private static DomainFieldEntry? Entry(string bullet)
    {
        var match = EntryLine().Match(bullet);
        if (!match.Success) return null;

        var names = NameSpan().Matches(match.Groups["names"].Value).Select(m => m.Groups[1].Value.Trim()).Where(n => n.Length > 0).ToList();
        if (names.Count == 0) return null;

        var meaning = match.Groups["meaning"].Value.Trim();
        var rules = new List<string>();
        var clause = RulesClause().Match(meaning);
        if (clause.Success)
        {
            rules.AddRange(Link().Matches(clause.Groups["links"].Value).Select(m => m.Groups[1].Value.Trim()));
            meaning = meaning[..clause.Index].Trim();
        }

        return new DomainFieldEntry(names, meaning, rules);
    }

    private static IEnumerable<string> WithoutFences(IEnumerable<string> lines)
    {
        var inFence = false;
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (!inFence) yield return line;
        }
    }

    [GeneratedRegex(@"^(?<names>`[^`]+`(?:\s*[·,/]\s*`[^`]+`)*)\s*[—–:-]\s*(?<meaning>.*)$")]
    private static partial Regex EntryLine();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex NameSpan();

    [GeneratedRegex(@"(?:^|\s)Rules?:\s*(?<links>\[.+)$")]
    private static partial Regex RulesClause();

    [GeneratedRegex(@"\[[^\]]*\]\(([^)\s]+)\)")]
    private static partial Regex Link();
}
