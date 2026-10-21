namespace Backlog.UI.Components.Devbook;

/// <summary>One row of a context's Glossary: a term or a model chapter, as the
/// Domain view's Glossary lists it (local ADR 0037 §5).</summary>
/// <param name="Title">The heading as written.</param>
/// <param name="Kind">The chapter's <c>type</c>: <c>term</c> for a word of the
/// ubiquitous language, else the model kind — <c>aggregate</c>,
/// <c>value-object</c>, <c>domain-event</c> and the rest.</param>
/// <param name="Aliases">The <c>aliases</c> as written, which the row shows as
/// code chips and the filter matches too.</param>
/// <param name="Summary">The chapter's first sentence.</param>
/// <param name="Deprecated">Whether its <c>status</c> is <c>deprecated</c>.</param>
/// <param name="Reference">The path and anchor that name the chapter.</param>
/// <param name="Proposed">Whether its <c>status</c> is <c>proposed</c>.</param>
/// <param name="Borrowed">For a term, the same heading in another context's
/// domain file that its <c>related</c> names; null for a word of its own.</param>
public sealed record DomainGlossaryEntry(
    string Title,
    string Kind,
    IReadOnlyList<string> Aliases,
    string Summary,
    bool Deprecated,
    string Reference,
    bool Proposed = false,
    DomainBorrowedTerm? Borrowed = null)
{
    /// <summary>The kind in words: <c>value object</c> for <c>value-object</c>.</summary>
    public string KindLabel => DomainGlossaryIndex.KindLabel(Kind);

    /// <summary>Whether this is a word of the ubiquitous language rather than a
    /// model chapter.</summary>
    public bool IsTerm => Kind == "term";

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

/// <summary>Where a borrowed term comes from: the other context's folder name and
/// the reference to its chapter of the same heading.</summary>
/// <param name="Context">The other context's folder, <c>roadmap</c>.</param>
/// <param name="Reference">The path and anchor of the chapter it names there.</param>
public sealed record DomainBorrowedTerm(string Context, string Reference);

/// <summary>Terms that name each other in <c>related</c>, directly or through
/// one another: one card of the Glossary's "Easily confused", A–Z.</summary>
public sealed record DomainConfusedGroup(IReadOnlyList<DomainGlossaryEntry> Terms);

/// <summary>
/// Reads the Glossary of a bounded context out of its domain files (local ADR 0037
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

    /// <summary>
    /// The term chapters that name each other in <c>related</c>, grouped: two terms
    /// that name each other are one group, and a term that names a third the same
    /// way joins it, so no term is on two cards. Each group's terms are A–Z and the
    /// groups are ordered by their first term. A one-way <c>related</c> is a
    /// pointer, not a confusion, so it groups nothing.
    /// </summary>
    public static IReadOnlyList<DomainConfusedGroup> Confused(IEnumerable<DomainChapterFile> files)
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

        // Union-find over the mutual pairs: the groups are its components.
        var parent = Enumerable.Range(0, terms.Count).ToArray();
        int Root(int index)
        {
            while (parent[index] != index) index = parent[index] = parent[parent[index]];
            return index;
        }

        var paired = new HashSet<int>();
        for (var a = 0; a < terms.Count; a++)
        {
            for (var b = a + 1; b < terms.Count; b++)
            {
                if (!names[terms[a].Key].Contains(terms[b].Key) || !names[terms[b].Key].Contains(terms[a].Key)) continue;

                parent[Root(a)] = Root(b);
                paired.Add(a);
                paired.Add(b);
            }
        }

        return
        [
            .. paired
                .GroupBy(Root)
                .Select(group => new DomainConfusedGroup(
                [
                    .. group
                        .Select(index => Entry(terms[index].File, terms[index].Chapter, "term"))
                        .OrderBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
                ]))
                .OrderBy(group => group.Terms[0].Title, StringComparer.OrdinalIgnoreCase)
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
            file.ReferenceTo(chapter),
            string.Equals(chapter.Status, "proposed", StringComparison.OrdinalIgnoreCase),
            kind == "term" ? BorrowedFrom(file, chapter) : null);

    /// <summary>
    /// The chapter a term borrows: the first of its <c>related</c> entries that
    /// names a domain file — <c>domain.md</c> or a split <c>domain.&lt;name&gt;.md</c>
    /// — of another context folder, at the term's own anchor. The same heading in
    /// another context is the same word, so the term is that context's.
    /// </summary>
    private static DomainBorrowedTerm? BorrowedFrom(DomainChapterFile file, DomainChapter chapter)
    {
        var own = DomainUnits.Normalize(file.Path);
        var ownContext = own.Contains('/') ? own[..own.IndexOf('/')] : string.Empty;

        foreach (var related in chapter.Related)
        {
            var target = DomainUnits.Resolve(related, file.Path);
            var hash = target.IndexOf('#');
            if (hash < 0) continue;

            var segments = target[..hash].Split('/');
            if (segments is not [var context, var name]) continue;
            if (string.Equals(context, ownContext, StringComparison.Ordinal)) continue;
            if (!name.StartsWith("domain", StringComparison.Ordinal) || !name.EndsWith(".md", StringComparison.Ordinal)) continue;
            if (!string.Equals(target[(hash + 1)..], chapter.Anchor, StringComparison.OrdinalIgnoreCase)) continue;

            return new DomainBorrowedTerm(context, DomainRoot(file.Path, own) + target);
        }

        return null;
    }

    /// <summary>The folder the term's own file sits under — <c>.devbook/domain/</c>,
    /// or the legacy <c>.domain/</c> — so the borrowed reference is spelled the
    /// way the term's own is, and not beneath the domain folder alone.</summary>
    private static string DomainRoot(string path, string normalized)
    {
        var text = path.Trim().Replace('\\', '/');
        return text.Length > normalized.Length ? text[..^normalized.Length] : string.Empty;
    }

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
