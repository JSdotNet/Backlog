namespace Backlog.UI.Components.Knowledge;

/// <summary>
/// The vocabulary <see cref="KnowledgeTypeMarker"/> can draw, published so a
/// consumer can decide its own fallback without keeping a second copy of the
/// list.
///
/// <para>Two sets, because the two questions are different ones. A
/// <c>.domain</c> file states a <c>type</c> under its <c>#</c> title saying what
/// kind of file it is, and every <c>##</c> chapter in it states a <c>type</c>
/// saying what kind of thing the chapter describes. A knowledge tree row is a
/// file and can only ever ask the first; a chapter heading can only ever ask the
/// second. <see cref="All"/> is the flat union, and it is a union rather than a
/// pair of lookups because all eighteen values are distinct strings — so one
/// component takes a raw <c>type</c> value and never has to be told which
/// question it came from.</para>
///
/// <para>Recognition is what the caller's fallback hangs off. A value set that
/// grows must never make a page look broken, so an unrecognised value draws no
/// glyph at all and the caller goes on showing the plain word — see the
/// component's own header for why that is the only safe default.</para>
/// </summary>
public static class KnowledgeTypeMarkers
{
    /// <summary>What a <c>##</c> chapter of a <c>.domain</c> file describes.</summary>
    public static IReadOnlyList<string> ChapterTypes { get; } =
    [
        "aggregate",
        "entity",
        "value-object",
        "enum",
        "shared-value-objects",
        "shared-enums",
        "domain-service",
        "domain-event",
        "feature",
        "sub-feature",
        "term"
    ];

    /// <summary>What a <c>.domain</c> file is, as its <c>#</c> title's block
    /// states it — and as its filename already says.</summary>
    public static IReadOnlyList<string> FileTypes { get; } =
    [
        "context-map",
        "domain",
        "model",
        "features",
        "flow",
        "dependencies",
        "naming"
    ];

    /// <summary>Both sets, in the order they are introduced.</summary>
    public static IReadOnlyList<string> All { get; } = [.. ChapterTypes, .. FileTypes];

    private static readonly HashSet<string> Known = new(All, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the marker has a glyph for this value. Nothing in,
    /// false — a missing <c>type</c> is not a type nobody drew.</summary>
    public static bool IsRecognised(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Known.Contains(value.Trim());

    /// <summary>The value as the lookup and the class modifier spell it.</summary>
    public static string Normalise(string value) => value.Trim().ToLowerInvariant();

    /// <summary>
    /// Whether a <c>type</c> read from this folder is spelled in this vocabulary at
    /// all.
    ///
    /// <para><c>.domain</c> and nothing else. The eighteen values below are the
    /// <c>.domain</c> convention's, and <c>.tech</c> writes a <c>type</c> of its own
    /// — <c>format</c>, <c>library</c>, <c>tool</c> — that shares the field name and
    /// none of the words. The two sets happen not to collide today, and a folder
    /// test rather than a value test is what keeps that an accident rather than the
    /// thing the marks depend on: the day <c>.tech</c> writes <c>type: model</c>, a
    /// value-only rule would draw a domain model's glyph on it.</para>
    /// </summary>
    public static bool MarksTypesIn(KnowledgeFolder folder) => folder is KnowledgeFolder.Domain;

    /// <summary>
    /// The value a surface reading this folder draws as a mark, normalised — or
    /// nothing, which is both "not this folder" and "not a value the set knows".
    ///
    /// <para>One question rather than two, because the two callers that ask it also
    /// have to suppress the plain <c>type</c> row on exactly the same terms. Asked
    /// twice, the mark and the row could disagree, and either way round is a
    /// defect: the value drawn twice, or not at all.</para>
    /// </summary>
    public static string? MarkedIn(KnowledgeFolder folder, string? value) =>
        MarksTypesIn(folder) && IsRecognised(value) ? Normalise(value!) : null;
}
