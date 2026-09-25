namespace Backlog.UI.Components.Devbook;

/// <summary>
/// The vocabulary <see cref="DevbookTypeMarker"/> can draw, published so a
/// consumer can decide its own fallback without keeping a second copy of the
/// list.
///
/// <para>Two sets, because the two questions are different ones. A
/// <c>.domain</c> file states a <c>type</c> under its <c>#</c> title saying what
/// kind of file it is, and every chapter in it states a <c>type</c> saying what
/// kind of thing the chapter describes. A document's header can only ever ask
/// the first; a chapter heading can only ever ask the second. Neither set is kept
/// here: both are <see cref="DevbookSchema"/>'s, which is what
/// <c>DevbookRuleTextContractTests</c> pins against the rule text, so a contract
/// that adds a kind adds it to the marks in the same change — and the glyph test
/// fails until somebody draws it.</para>
///
/// <para><see cref="All"/> is the union with the repeats collapsed. Up to
/// contract 9 every value was a distinct string and the union was simply the two
/// lists end to end. Contract 16 puts <c>requirements</c> and <c>invariants</c> in
/// both sets, so that is no longer true — and a union is still the right shape,
/// because the rule is explicit that the two words "mean the same thing at both
/// levels": the file holds a context's requirements, a chapter one feature's. One
/// meaning is one glyph. So the component still takes a raw <c>type</c> value and
/// never has to be told which question it came from; if a later contract ever
/// gives one word two meanings at the two levels, this is the sentence that stops
/// being true and the lookup has to grow a level.</para>
///
/// <para>One more thing is drawn that neither set contains: a <c>domain/</c>
/// context's additional page, whose file <c>type</c> is its own filename. That is
/// a value no closed list can hold, so it is recognised the way the schema
/// recognises it — <see cref="DevbookSchema.IsKnownType"/> with the file's name —
/// and drawn as one generic <see cref="AdditionalPage"/> mark. The <c>naming</c>
/// this list used to carry was exactly that case hard-coded: this repository's
/// own <c>.domain/devbook/naming.md</c> is an additional page, and under contract
/// 16 so is any other.</para>
///
/// <para>Recognition is what the caller's fallback hangs off. A value set that
/// grows must never make a page look broken, so an unrecognised value draws no
/// glyph at all and the caller goes on showing the plain word — see the
/// component's own header for why that is the only safe default.</para>
/// </summary>
public static class DevbookTypeMarkers
{
    /// <summary>What a chapter of a <c>.domain</c> file describes — the
    /// schema's chapter-level set, in the rule's order.</summary>
    public static IReadOnlyList<string> ChapterTypes { get; } = DevbookSchema.ChapterTypes(DevbookFolder.Domain);

    /// <summary>What a <c>.domain</c> file is, as its <c>#</c> title's block
    /// states it — the schema's file-level set, in the rule's order. An additional
    /// page's own-filename type is not in it; see <see cref="AdditionalPage"/>.</summary>
    public static IReadOnlyList<string> FileTypes { get; } = DevbookSchema.FileTypes(DevbookFolder.Domain);

    /// <summary>Both sets, in the order they are introduced, with the two words
    /// that are in both — <c>requirements</c>, <c>invariants</c> — listed
    /// once.</summary>
    public static IReadOnlyList<string> All { get; } =
        [.. ChapterTypes.Concat(FileTypes).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The slug the generic mark of an additional page is drawn and
    /// styled under. Not a <c>type</c> anybody writes: the page's own <c>type</c> is
    /// its filename, and this is the one glyph every such filename shares.</summary>
    public const string AdditionalPage = "page";

    private static readonly HashSet<string> Known = new(All, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the marker has a glyph for this value as a value of the
    /// closed sets. Nothing in, false — a missing <c>type</c> is not a type nobody
    /// drew.</summary>
    public static bool IsRecognised(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Known.Contains(value.Trim());

    /// <summary>
    /// Whether this is a <c>domain/</c> additional page stating its own filename as
    /// its <c>type</c> — <c>regulatory-annex.md</c> saying
    /// <c>type: regulatory-annex</c>.
    ///
    /// <para>Asked of the schema rather than answered here, so the one place that
    /// decides a filename is a legal file type is the one place that says so. A
    /// value the closed set already holds is not an additional page even when a
    /// file happens to be named after it: <c>domain.md</c> is a domain file.</para>
    /// </summary>
    public static bool IsAdditionalPage(string? value, string? fileName) =>
        !IsRecognised(value)
        && DevbookSchema.IsKnownType(DevbookFolder.Domain, DevbookMetadataLevel.File, value, fileName);

    /// <summary>The value as the lookup and the class modifier spell it.</summary>
    public static string Normalise(string value) => value.Trim().ToLowerInvariant();

    /// <summary>
    /// The glyph a value draws, normalised — the value itself for the closed sets,
    /// <see cref="AdditionalPage"/> for an additional page's own-filename type, and
    /// nothing for anything else.
    /// </summary>
    /// <param name="fileName">The file the value was read from, or nothing for a
    /// chapter. Only a file can be an additional page.</param>
    public static string? GlyphFor(string? value, string? fileName = null) =>
        IsRecognised(value) ? Normalise(value!)
        : IsAdditionalPage(value, fileName) ? AdditionalPage
        : null;

    /// <summary>
    /// Whether a <c>type</c> read from this folder is spelled in this vocabulary at
    /// all.
    ///
    /// <para><c>.domain</c> and nothing else. The values above are the
    /// <c>.domain</c> convention's, and <c>.tech</c> writes a <c>type</c> of its own
    /// — <c>format</c>, <c>library</c>, <c>tool</c> — that shares the field name and
    /// none of the words, and <c>ai/</c> another. They collide already:
    /// <c>ai/</c>'s chapter set has <c>model</c>, which is a <c>.domain</c> file
    /// type. A folder test rather than a value test is what keeps that from mattering:
    /// a value-only rule would draw a domain model's glyph on an AI model.</para>
    /// </summary>
    public static bool MarksTypesIn(DevbookFolder folder) => folder is DevbookFolder.Domain;

    /// <summary>
    /// The value a surface reading this folder draws as a mark, normalised — or
    /// nothing, which is both "not this folder" and "not a value the set knows".
    ///
    /// <para>One question rather than two, because the two callers that ask it also
    /// have to suppress the plain <c>type</c> row on exactly the same terms. Asked
    /// twice, the mark and the row could disagree, and either way round is a
    /// defect: the value drawn twice, or not at all.</para>
    /// </summary>
    public static string? MarkedIn(DevbookFolder folder, string? value) =>
        MarksTypesIn(folder) && IsRecognised(value) ? Normalise(value!) : null;

    /// <summary>
    /// The same question for a file's own block, which is the one level an
    /// additional page can answer: its own-filename <c>type</c> is known here and
    /// nowhere else.
    ///
    /// <para>Returns the <c>type</c> value, not the glyph slug, so a caller hands it
    /// straight to <see cref="DevbookTypeMarker"/> with the same file name and the
    /// mark is named with what the file actually says —
    /// <c>type: regulatory-annex</c> rather than a <c>page</c> nobody wrote.</para>
    /// </summary>
    public static string? MarkedFileIn(DevbookFolder folder, string? value, string? fileName) =>
        MarksTypesIn(folder) && GlyphFor(value, fileName) is not null ? Normalise(value!) : null;
}
