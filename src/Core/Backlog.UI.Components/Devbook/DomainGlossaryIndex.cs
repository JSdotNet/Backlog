namespace Backlog.UI.Components.Devbook;

/// <summary>One row of a context's Glossary: a term or a model chapter, as the
/// Domain view's Glossary lists it (local ADR 0036 §5).</summary>
/// <param name="Title">The heading as written.</param>
/// <param name="Kind">The chapter's <c>type</c>: <c>term</c> for a word of the
/// ubiquitous language, else the model kind — <c>aggregate</c>,
/// <c>value-object</c>, <c>domain-event</c> and the rest.</param>
/// <param name="Aliases">The <c>aliases</c> as written, which the row shows as
/// code chips and the filter matches too.</param>
/// <param name="Summary">The chapter's first sentence.</param>
/// <param name="Deprecated">Whether its <c>status</c> is <c>deprecated</c>.</param>
/// <param name="Reference">The path and anchor that name the chapter.</param>
public sealed record DomainGlossaryEntry(
    string Title,
    string Kind,
    IReadOnlyList<string> Aliases,
    string Summary,
    bool Deprecated,
    string Reference)
{
    /// <summary>The kind in words: <c>value object</c> for <c>value-object</c>.</summary>
    public string KindLabel => DomainGlossaryIndex.KindLabel(Kind);

    /// <summary>Whether <paramref name="filter"/> is in the heading or in one of
    /// the aliases, ignoring case. A blank filter matches every row.</summary>
    public bool Matches(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        var text = filter.Trim();
        return Title.Contains(text, StringComparison.OrdinalIgnoreCase)
            || Aliases.Any(alias => alias.Contains(text, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Two terms that name each other in <c>related</c>: the pair the
/// Glossary's "Easily confused" sets side by side.</summary>
public sealed record DomainConfusedPair(DomainGlossaryEntry First, DomainGlossaryEntry Second);

/// <summary>
/// Reads the Glossary of a bounded context out of its domain files (local ADR 0036
/// §5): every term chapter under a <c>ubiquitous-language</c> grouping and every
/// model chapter, A–Z. Pure, as <see cref="DomainUnits"/> is, so the storybook
/// draws the page from a sample string.
/// </summary>
public static class DomainGlossaryIndex
{
    /// <summary>The model kinds a Glossary lists beside the terms: the building
    /// blocks a <c>domain.md</c> names.</summary>
    public static readonly IReadOnlyList<string> ModelKinds =
        ["aggregate", "entity", "value-object", "enum", "domain-service", "domain-event"];

    /// <summary>Every term and model chapter of <paramref name="files"/>, A–Z by
    /// heading, each once.</summary>
    public static IReadOnlyList<DomainGlossaryEntry> Entries(IEnumerable<DomainChapterFile> files)
    {
        // Each chapter once, by the chapter itself rather than by its reference: a
        // term and an aggregate of one heading share an anchor and are two rows.
        var seen = new HashSet<DomainChapter>(ReferenceEqualityComparer.Instance);
        var entries = new List<DomainGlossaryEntry>();
        foreach (var (file, chapter, kind) in Chapters(files))
        {
            if (!seen.Add(chapter)) continue;
            entries.Add(Entry(file, chapter, kind));
        }

        return
        [
            .. entries
                .OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Kind, StringComparer.Ordinal)
        ];
    }

    /// <summary>The kinds <paramref name="entries"/> hold, terms first and then the
    /// model kinds in the order <see cref="ModelKinds"/> gives: the kind filters.</summary>
    public static IReadOnlyList<string> Kinds(IEnumerable<DomainGlossaryEntry> entries)
    {
        var present = entries.Select(entry => entry.Kind).ToHashSet(StringComparer.Ordinal);
        return [.. new[] { "term" }.Concat(ModelKinds).Where(present.Contains)];
    }

    /// <summary>
    /// Every pair of term chapters that name each other in <c>related</c>, each
    /// pair once, ordered by its first term. A one-way <c>related</c> is a pointer,
    /// not a confusion, so it makes no pair.
    /// </summary>
    public static IReadOnlyList<DomainConfusedPair> Confused(IEnumerable<DomainChapterFile> files)
    {
        var terms = Chapters(files)
            .Where(found => found.Kind == "term")
            .Select(found => (Key: DomainUnits.Normalize(found.File.ReferenceTo(found.Chapter)), found.File, found.Chapter))
            .GroupBy(found => found.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        var names = terms.ToDictionary(
            term => term.Key,
            term => term.Chapter.Related.Select(related => DomainUnits.Resolve(related, term.File.Path)).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);

        var pairs = new List<DomainConfusedPair>();
        for (var a = 0; a < terms.Count; a++)
        {
            for (var b = a + 1; b < terms.Count; b++)
            {
                if (!names[terms[a].Key].Contains(terms[b].Key) || !names[terms[b].Key].Contains(terms[a].Key)) continue;

                var first = Entry(terms[a].File, terms[a].Chapter, "term");
                var second = Entry(terms[b].File, terms[b].Chapter, "term");
                pairs.Add(StringComparer.OrdinalIgnoreCase.Compare(first.Title, second.Title) <= 0
                    ? new DomainConfusedPair(first, second)
                    : new DomainConfusedPair(second, first));
            }
        }

        return
        [
            .. pairs
                .OrderBy(pair => pair.First.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(pair => pair.Second.Title, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>A kind in words: the <c>type</c> with its hyphens as spaces.</summary>
    public static string KindLabel(string kind) => kind.Replace('-', ' ');

    private static DomainGlossaryEntry Entry(DomainChapterFile file, DomainChapter chapter, string kind) =>
        new(
            chapter.Title,
            kind,
            chapter.Aliases,
            chapter.Summary,
            string.Equals(chapter.Status, "deprecated", StringComparison.OrdinalIgnoreCase),
            file.ReferenceTo(chapter));

    /// <summary>The term chapters — a <c>term</c>, or an untyped heading, directly
    /// under a <c>ubiquitous-language</c> grouping, as <see cref="DomainUnits.Terms"/>
    /// reads them — and the model chapters at any depth, in file order.</summary>
    private static IEnumerable<(DomainChapterFile File, DomainChapter Chapter, string Kind)> Chapters(IEnumerable<DomainChapterFile> files)
    {
        foreach (var file in files)
        {
            foreach (var chapter in file.AllChapters())
            {
                if (chapter.Type == "ubiquitous-language")
                {
                    foreach (var term in chapter.Children.Where(child => child.Type is null or "term"))
                    {
                        yield return (file, term, "term");
                    }
                }
                else if (chapter.Type is { } type && ModelKinds.Contains(type))
                {
                    yield return (file, chapter, type);
                }
            }
        }
    }
}
