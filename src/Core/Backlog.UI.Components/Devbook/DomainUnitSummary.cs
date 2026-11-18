using System.Text.RegularExpressions;

namespace Backlog.UI.Components.Devbook;

/// <summary>One link of a unit page's link row — "What is always true · 29 →",
/// "How it runs, on Model →": its words, and what following it does.</summary>
public sealed record DomainPageLink(string Text, string TestId, Func<Task> Follow);

/// <summary>
/// The Domain view's functional pages in the domain's own language (local ADR
/// 0047 §5–6): a chapter's first sentence as its summary, with any list of
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

    /// <summary>
    /// The words an enum's chapter puts before its values — "Lifecycle state:
    /// `draft`, …" reads "Lifecycle state" — which its row leads with. Empty when
    /// the sentence naming the values has no colon (a range such as "`monday`
    /// through `sunday`"), when the words before it hold a code span or a link,
    /// run past six words or end in "one of", or when they only repeat
    /// <paramref name="heading"/>. The
    /// chapter's words, never a question the view would write (§5).
    /// </summary>
    public static string EnumLabel(string? markdown, string? heading = null)
    {
        var sentence = SentenceOf(FirstParagraph(markdown));
        var colon = sentence.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0) return string.Empty;

        var before = sentence[..colon];
        if (before.Contains('`', StringComparison.Ordinal) || before.Contains('[', StringComparison.Ordinal)) return string.Empty;

        var label = Plain(before);
        if (label.Length == 0 || Whitespace().Split(label).Length > MaxLabelWords) return string.Empty;

        // "Classifies the destination as one of:" introduces the values; it names nothing.
        if (label.EndsWith(" one of", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        return heading is not null && string.Equals(label, heading.Trim(), StringComparison.OrdinalIgnoreCase) ? string.Empty : label;
    }

    private const int MaxLabelWords = 6;

    /// <summary>
    /// The lifecycle an enum's values are the states of: the index, among
    /// <paramref name="flows"/>, of the first <c>stateDiagram-v2</c> holding a
    /// state for every value — <c>in_progress</c> matching <c>InProgress</c> or
    /// "In progress", compared as lower-case letters and digits — or
    /// <see langword="null"/>. An enum of fewer than two values names no lifecycle.
    /// </summary>
    public static int? LifecycleOf(DomainEnum item, IReadOnlyList<DomainChapterFlow> flows)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(flows);
        if (item.Values.Count < 2) return null;

        for (var i = 0; i < flows.Count; i++)
        {
            if (flows[i].Figure is not DomainFlowDiagram diagram) continue;
            var states = diagram.States
                .Where(state => state.Kind == DomainFlowStateKind.State)
                .SelectMany(state => new[] { Compact(state.Id), Compact(state.Label) })
                .ToHashSet(StringComparer.Ordinal);
            if (item.Values.All(value => states.Contains(Compact(value)))) return i;
        }
        return null;
    }

    /// <summary>A name as lower-case letters and digits only, so <c>in_progress</c>,
    /// <c>InProgress</c> and "In progress" read alike.</summary>
    private static string Compact(string value) => Diagrams.DomainChapterContracts.Compact(value).ToLowerInvariant();

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
        RelatedOf(unit.Chapter.Related, filePath);

    /// <summary><c>related</c> entries as written in <paramref name="filePath"/>,
    /// as repository-relative references — for a reader that holds the entries
    /// without the unit, as Model's domain-service chapters do.</summary>
    public static IReadOnlyList<string> RelatedOf(IEnumerable<string> related, string filePath) =>
        [.. related.Select(entry => RepositoryRelative(entry, filePath)).Where(reference => reference.Length > 0)];

    /// <summary>Whether a reference names a requirements file.</summary>
    public static bool IsRequirementsReference(string reference) => FileNameStarts(reference, "requirements");

    /// <summary>A unit's first sentence as markdown, its links and code spans
    /// kept and its lines run together, or the empty string: what
    /// <see cref="FirstSentence"/> reads, before <see cref="Plain"/> flattens it.</summary>
    public static string FirstSentenceMarkdown(string? markdown)
    {
        var paragraph = FirstParagraph(markdown);
        return paragraph.Length == 0 ? string.Empty : Whitespace().Replace(SentenceOf(paragraph), " ").Trim();
    }

    /// <summary>Whether a <c>related</c> entry written in
    /// <paramref name="fromDocument"/> names the file at <paramref name="path"/>,
    /// with or without an anchor, whichever root either side spells.</summary>
    public static bool NamesFile(string related, string fromDocument, string path)
    {
        ArgumentNullException.ThrowIfNull(related);
        ArgumentNullException.ThrowIfNull(fromDocument);
        ArgumentNullException.ThrowIfNull(path);
        return related.Trim().Length > 0
            && string.Equals(PathOf(Resolve(related, fromDocument)), Normalize(path), StringComparison.Ordinal);
    }

    /// <summary>
    /// The Model file a domain file is built in, of <paramref name="modelPaths"/>.
    /// By name first: for a split <c>domain.&lt;name&gt;.md</c> the
    /// <c>model.&lt;name&gt;.md</c>, for <c>domain.md</c> the <c>model.md</c>. When
    /// no name matches, the file's <c>related</c> decides — its title's entries,
    /// then each chapter's at every level, depth first in file order — by the
    /// first that names one of the Model files, as <c>BoekingsService</c> names
    /// <c>model.domain-services.md</c>. Only then <c>model.md</c>, else the first.
    /// <see langword="null"/> when there is no Model file.
    /// </summary>
    /// <param name="domainPath">The domain file, as the host spells it.</param>
    /// <param name="modelPaths">The context's Model files.</param>
    /// <param name="parsed">The domain file as read, whose <c>related</c> entries
    /// decide when no name matches. Unset, they are not consulted.</param>
    public static string? ModelFileFor(string domainPath, IReadOnlyList<string> modelPaths, DomainChapterFile? parsed = null)
    {
        ArgumentNullException.ThrowIfNull(domainPath);
        ArgumentNullException.ThrowIfNull(modelPaths);

        var name = FileNameOf(domainPath);
        var counterpart = string.Equals(name, "domain.md", StringComparison.OrdinalIgnoreCase)
            ? "model.md"
            : name.StartsWith("domain.", StringComparison.OrdinalIgnoreCase) ? "model." + name["domain.".Length..] : null;
        if (counterpart is not null
            && modelPaths.FirstOrDefault(model => string.Equals(FileNameOf(model), counterpart, StringComparison.OrdinalIgnoreCase)) is { } named)
        {
            return named;
        }

        if (parsed is not null)
        {
            foreach (var entry in parsed.Related.Concat(parsed.AllChapters().SelectMany(chapter => chapter.Related)))
            {
                if (modelPaths.FirstOrDefault(model => NamesFile(entry, domainPath, model)) is { } linked) return linked;
            }
        }

        return modelPaths.FirstOrDefault(model => string.Equals(FileNameOf(model), "model.md", StringComparison.OrdinalIgnoreCase))
            ?? modelPaths.FirstOrDefault();
    }

    private static string FileNameOf(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized[(normalized.LastIndexOf('/') + 1)..];
    }

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
