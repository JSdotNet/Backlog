using System.Text.RegularExpressions;

namespace Backlog.UI.Components.Devbook;

/// <summary>One link of a unit page's link row — "What is always true · 29 →",
/// "How it runs, on Model →": its words, and what following it does.</summary>
public sealed record DomainPageLink(string Text, string TestId, Func<Task> Follow);

/// <summary>
/// The Domain view's functional pages in the domain's own language (local ADR
/// 0037 §5–6): a chapter's first sentence as its summary, with any list of
/// fields it closes on left to Model, and an enum's values read as words.
/// </summary>
public static partial class DomainUnits
{
    /// <summary>
    /// A chapter's first sentence as plain text, without the list of fields it
    /// may close on: "An immutable link … created from the task: `repo_id`,
    /// `external_id`" reads as far as the colon. Field names are Model's (§6);
    /// the words before them are the chapter's own (§5).
    /// </summary>
    public static string FunctionalSummary(string? markdown)
    {
        var sentence = SentenceOf(FirstParagraph(markdown));
        if (sentence.Length == 0) return string.Empty;

        var colon = sentence.LastIndexOf(": ", StringComparison.Ordinal);
        if (colon > 0 && IsTechnical(sentence[(colon + 2)..]))
            sentence = sentence[..colon].TrimEnd() + ".";

        return Plain(sentence);
    }

    /// <summary>
    /// The sentence after the one that names an enum's values, as plain text —
    /// "A follow-up is a Dependency on the task it comes after rather than a type
    /// of its own." — or the empty string when there is none or it names a field.
    /// </summary>
    public static string EnumNote(string? markdown)
    {
        var paragraph = FirstParagraph(markdown);
        var first = SentenceOf(paragraph);
        if (first.Length >= paragraph.Length) return string.Empty;

        var rest = paragraph[first.Length..].TrimStart();
        var note = SentenceOf(rest);
        return note.Length == 0 || IsTechnical(note) ? string.Empty : Plain(note);
    }

    /// <summary>A value as the reader says it: <c>in_progress</c> reads "In
    /// progress", <c>draft</c> "Draft".</summary>
    public static string Humanize(string value)
    {
        var words = value.Replace('_', ' ').Replace('-', ' ').Trim();
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }

    /// <summary>The kind of a unit in words, as its page's kicker reads it.</summary>
    public static string KindWords(DomainUnitKind kind) => kind switch
    {
        DomainUnitKind.Aggregate => "aggregate",
        DomainUnitKind.DomainService => "domain service",
        DomainUnitKind.Glossary => "glossary",
        _ => "shared"
    };

    /// <summary>A unit page's kicker: its kind in words, and its <c>status</c>
    /// when the chapter states one — "domain service · proposed".</summary>
    public static string Kicker(DomainUnitKind kind, string? status) =>
        status is { Length: > 0 } ? $"{KindWords(kind)} · {status}" : KindWords(kind);

    /// <summary>A heading inside a sentence: "Task" reads "a task", "Inbox Item"
    /// "an inbox item", and an acronym keeps its capitals — "an AI work log".</summary>
    public static string WithArticle(string title)
    {
        var words = Whitespace().Split(title.Trim())
            .Select(word => word.Length > 1 && char.IsUpper(word[1]) ? word : word.ToLowerInvariant());
        var phrase = string.Join(' ', words);
        if (phrase.Length == 0) return phrase;
        return ("aeiouAEIOU".Contains(phrase[0]) ? "an " : "a ") + phrase;
    }

    /// <summary>The unit's <c>related</c> entries as repository-relative
    /// references, read against <paramref name="filePath"/>.</summary>
    public static IReadOnlyList<string> RelatedOf(DomainUnit unit, string filePath) =>
        [.. unit.Chapter.Related.Select(related => RepositoryRelative(related, filePath)).Where(reference => reference.Length > 0)];

    /// <summary>Whether a reference names a flow file — <c>flow.md</c> or a split
    /// <c>flow.&lt;name&gt;.md</c>.</summary>
    public static bool IsFlowReference(string reference) => FileNameStarts(reference, "flow");

    /// <summary>Whether a reference names a requirements file.</summary>
    public static bool IsRequirementsReference(string reference) => FileNameStarts(reference, "requirements");

    private static bool FileNameStarts(string reference, string kind)
    {
        var path = PathOf(reference).Replace('\\', '/');
        var name = path[(path.LastIndexOf('/') + 1)..];
        return name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(name, kind + ".md", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith(kind + ".", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether <paramref name="markdown"/> names a field: a word with an
    /// underscore inside it, in a code span or not — <c>repo_id</c>,
    /// <c>`depends_on`</c>.</summary>
    internal static bool IsTechnical(string markdown) => FieldName().IsMatch(markdown);

    [GeneratedRegex(@"\b[A-Za-z][A-Za-z0-9]*_[A-Za-z0-9_]+\b")]
    private static partial Regex FieldName();
}
