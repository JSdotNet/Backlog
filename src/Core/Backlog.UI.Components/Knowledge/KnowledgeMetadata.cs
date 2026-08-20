namespace Backlog.UI.Components.Knowledge;

/// <summary>
/// The contents of one fenced <c>meta</c> block: the small, parseable record a
/// knowledge chapter or file carries directly under its heading.
///
/// <para>Only <c>status</c> is required. Every other field is written only when
/// it has a value — the convention is explicit that empty collections and nulls
/// are omitted rather than spelled out — so an absent field here means "not
/// stated", never "stated as empty".</para>
/// </summary>
public sealed record KnowledgeMetadata
{
    /// <summary>Lifecycle state. The allowed values are folder-specific; see
    /// <see cref="KnowledgeStatus"/>.</summary>
    public string? Status { get; init; }

    /// <summary>References this chapter or file points at for context, without a
    /// hard dependency. Available in every folder.</summary>
    public IReadOnlyList<KnowledgeReference> Related { get; init; } = [];

    /// <summary>References that must land first — features, backlog items, and
    /// technologies use this where <c>related</c> would understate the order.</summary>
    public IReadOnlyList<KnowledgeReference> DependsOn { get; init; } = [];

    /// <summary>What a backlog item delivers, as references into the domain.</summary>
    public IReadOnlyList<KnowledgeReference> Implements { get; init; } = [];

    /// <summary>The tracking issue: a URL, or the <c>owner/repo#number</c>
    /// shorthand. Stored exactly as authored — the shorthand is not a reference
    /// and resolving it needs a remote this library does not know about.</summary>
    public string? Issue { get; init; }

    /// <summary>The reading order a root document declares for its directory:
    /// plain sibling file or directory names, never paths.</summary>
    public IReadOnlyList<string> Order { get; init; } = [];

    /// <summary>Surface names a <c>.domain</c> term is also known by. Plain
    /// strings by design — the link to where the term is modelled is carried by
    /// <see cref="Related"/> instead.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>Technologies that were weighed against this one. Plain strings:
    /// an alternative that was not adopted has no chapter to point at.</summary>
    public IReadOnlyList<string> Alternatives { get; init; } = [];

    /// <summary>What kind of thing a <c>.tech</c> entry is (<c>format</c>,
    /// <c>library</c>, …).</summary>
    public string? Kind { get; init; }

    /// <summary>The pinned version of a <c>.tech</c> entry, as authored.</summary>
    public string? Version { get; init; }

    /// <summary>A story-point estimate, parsed to the integer the UI shows.
    /// <see langword="null"/> when the field is absent, <c>null</c>, or holds
    /// something that is not a non-negative integer — this side is a reader, so
    /// an unreadable estimate surfaces as "not estimated" rather than throwing.
    /// <c>0</c> is a real estimate and stays distinct from unset.</summary>
    public int? Effort { get; init; }

    /// <summary>Roadmap item tag slugs this chapter or file contributes to.
    /// Plain strings by design — like <see cref="Aliases"/>, the values name
    /// roadmap items by their tag rather than addressing a chapter, so they are
    /// never read as <see cref="KnowledgeReference"/>s and never become
    /// links.</summary>
    public IReadOnlyList<string> Roadmap { get; init; } = [];

    /// <summary>
    /// Every key the schema does not define, kept verbatim.
    ///
    /// <para>The convention says not to invent fields, but a reader that silently
    /// discarded an unknown one would make a genuine schema addition invisible:
    /// the field would be in the file, absent from the view, and nobody would
    /// know which of the two was wrong. Keeping it means an unrecognised field
    /// shows up as itself.</para>
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Extra { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>A block that stated nothing.</summary>
    public static KnowledgeMetadata Empty { get; } = new();

    /// <summary>Whether there is anything at all to show. A chapter with no
    /// metadata should not leave a gap where the strip would have been.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Status)
        && Related.Count == 0
        && DependsOn.Count == 0
        && Implements.Count == 0
        && string.IsNullOrWhiteSpace(Issue)
        && Order.Count == 0
        && Aliases.Count == 0
        && Alternatives.Count == 0
        && string.IsNullOrWhiteSpace(Kind)
        && string.IsNullOrWhiteSpace(Version)
        && Effort is null
        && Roadmap.Count == 0
        && Extra.Count == 0;

    /// <summary>
    /// Every reference this block carries, in field order and de-duplicated on
    /// the authored form. One target named by two fields is one edge in the
    /// graph, and a caller walking references should not visit it twice.
    /// </summary>
    public IReadOnlyList<KnowledgeReference> References
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            return [.. Related.Concat(DependsOn).Concat(Implements).Where(reference => seen.Add(reference.Raw))];
        }
    }
}
