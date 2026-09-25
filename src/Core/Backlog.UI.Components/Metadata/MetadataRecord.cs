using Backlog.UI.Components.Devbook;

namespace Backlog.UI.Components.Metadata;

/// <summary>
/// The contents of one fenced <c>meta</c> block: the small, parseable record a
/// knowledge chapter or file carries directly under its heading.
///
/// <para>Only <c>status</c> is required. Every other field is written only when
/// it has a value — the convention is explicit that empty collections and nulls
/// are omitted rather than spelled out — so an absent field here means "not
/// stated", never "stated as empty".</para>
/// </summary>
public sealed record MetadataRecord
{
    /// <summary>Lifecycle state. The allowed values are folder-specific; see
    /// <see cref="DevbookStatus"/>.</summary>
    public string? Status { get; init; }

    /// <summary>References this chapter or file points at for context, without a
    /// hard dependency. Available in every folder.</summary>
    public IReadOnlyList<DevbookReference> Related { get; init; } = [];

    /// <summary>References that must land first — features, backlog items, and
    /// technologies use this where <c>related</c> would understate the order.</summary>
    public IReadOnlyList<DevbookReference> DependsOn { get; init; } = [];

    /// <summary>What a backlog item delivers, as references into the domain.
    ///
    /// <para>Legacy. <c>implements</c> belongs to <c>.backlog</c>, which the product
    /// still reads from older checkouts and which the convention is retiring; it is
    /// parsed so those files keep showing what they say, and it is no longer drawn
    /// as an edge in the atlas.</para></summary>
    public IReadOnlyList<DevbookReference> Implements { get; init; } = [];

    /// <summary>The tracking issue: a URL, or the <c>owner/repo#number</c>
    /// shorthand. Stored exactly as authored — the shorthand is not a reference
    /// and resolving it needs a remote this library does not know about.</summary>
    public string? Issue { get; init; }

    /// <summary>Surface names a <c>.domain</c> term is also known by. Plain
    /// strings by design — the link to where the term is modelled is carried by
    /// <see cref="Related"/> instead.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>Technologies that were weighed against this one. Plain strings:
    /// an alternative that was not adopted has no chapter to point at.</summary>
    public IReadOnlyList<string> Alternatives { get; init; } = [];

    /// <summary>
    /// The <c>kind</c> field as the file wrote it: <c>tech/</c>'s old spelling of
    /// <see cref="Type"/>.
    ///
    /// <para>Kept so a caller that has always read it still compiles and still
    /// sees what the file says, and so the row it has always drawn is still drawn.
    /// It is not the field a surface should ask "what kind of thing is this?" —
    /// <see cref="Type"/> is, and it already falls back to this value.</para>
    /// </summary>
    public string? Kind { get; init; }

    /// <summary>
    /// What kind of thing this chapter or file <em>is</em> — <c>aggregate</c>,
    /// <c>library</c>, <c>context</c> — read from <c>type</c>, or from the legacy
    /// <c>kind</c> when the block writes only that.
    ///
    /// <para>One field, several vocabularies: <c>domain/</c>, <c>tech/</c> and
    /// <c>ai/</c> each define their own, and <c>arc42/</c> and <c>design/</c>
    /// define none. Which of them applies is the caller's question, because the
    /// folder is the caller's fact — <see cref="DevbookSchema.IsKnownType"/>
    /// answers it given one.</para>
    ///
    /// <para>Absent, blank, or <c>null</c> in the file all read back as
    /// <see langword="null"/>.</para>
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// Whether <see cref="Type"/> came from the legacy <c>kind</c> spelling rather
    /// than from <c>type</c>.
    ///
    /// <para>The convention still parses the old name so a repository is not broken
    /// by a sync, and reports it so it gets renamed. This is how the report finds
    /// out — see <c>DevbookMetadataFindings</c>. It also decides which row is
    /// drawn: a value that came from <c>kind</c> keeps the classification chip the
    /// <c>kind</c> row has always been, rather than being drawn a second time as a
    /// <c>type</c> row.</para>
    /// </summary>
    public bool TypeReadFromKind { get; init; }

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
    /// never read as <see cref="DevbookReference"/>s and never become
    /// links.</summary>
    public IReadOnlyList<string> Roadmap { get; init; } = [];

    /// <summary>
    /// The application feature flags that deliver a <c>.domain</c> feature
    /// chapter in the running product, by key.
    ///
    /// <para>Plain strings, and explicitly not references: the flag lives in the
    /// application's own catalog rather than in a knowledge folder, so there is no
    /// chapter to address and the key is never validated here. One chapter may
    /// name several flags, and one flag may appear on several chapters.</para>
    ///
    /// <para>An identity link and not a status mapping. How settled the written
    /// model is and whether the running behaviour can be relied on are different
    /// questions, so nothing here infers <see cref="Status"/> from a flag or the
    /// reverse.</para>
    /// </summary>
    public IReadOnlyList<string> FeatureFlag { get; init; } = [];

    /// <summary>
    /// The test cases that assert what this chapter or file claims, each a
    /// <c>&lt;level&gt;:&lt;runner&gt;:&lt;selector&gt;</c> identifier —
    /// <c>unit:dotnet:Ordering.Domain.Tests.OrderTests</c>.
    ///
    /// <para>Plain strings, exactly as authored. Only the first two colons of an
    /// entry delimit, because a selector routinely carries colons of its own — a
    /// pytest node id, a <c>file:line</c> — and nothing here splits one further or
    /// turns it into a reference: like <see cref="Roadmap"/>, an entry is a node
    /// attribute and never a graph edge.</para>
    /// </summary>
    public IReadOnlyList<string> Tests { get; init; } = [];

    /// <summary>This document's number within its directory — arc42 chapter 9,
    /// TDR 2. File-level only. <see langword="null"/> when absent or not a
    /// non-negative integer, the same reading <see cref="Effort"/> gets.</summary>
    public int? Number { get; init; }

    /// <summary>How this document steers the generated outline: <c>root</c> or
    /// <c>exclude</c>. File-level only, and as authored — a value outside the two
    /// is reported rather than dropped.</summary>
    public string? Index { get; init; }

    /// <summary>The calendar day this chapter or file records, as authored
    /// (<c>YYYY-MM-DD</c>). Part of the content — the day a decision was taken —
    /// and never a last-modified stamp.</summary>
    public string? Date { get; init; }

    /// <summary>How a bounded context ships: <c>service</c> or <c>module</c>, as
    /// authored. Written on a <c>domain/</c> context map's <c>bounded-context</c>
    /// chapter and on that context's <c>context.md</c> file block.</summary>
    public string? Deployment { get; init; }

    /// <summary>
    /// The extension namespace: every <c>ext.&lt;plugin&gt;.&lt;key&gt;</c> the
    /// block carries, keyed by what follows <c>ext.</c> with the author's own
    /// casing, and each value exactly as written.
    ///
    /// <para>Opaque on purpose. The state belongs to a plugin layered on top of
    /// devbook, and the convention carries it through untouched and unvalidated —
    /// including the omit-when-empty rule every other field obeys, so a key
    /// written with no value is kept here as an empty string rather than dropped.
    /// Nothing reads one of these as schema, and none of them reaches
    /// <see cref="Extra"/>.</para>
    /// </summary>
    public IReadOnlyDictionary<string, string> Ext { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Decision and review state: the approval and acceptance records and the
    /// review triad.
    ///
    /// <para>Held apart from every other field because none of it is chapter
    /// content. A reader loading the chapter for context skips it, and a view draws
    /// it beside the status rather than as rows in the body — see
    /// <see cref="MetadataView"/>. None of the nine keys ever reaches
    /// <see cref="Extra"/>.</para>
    /// </summary>
    public MetadataState State { get; init; } = MetadataState.Empty;

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

    /// <summary>
    /// The same record with its <c>type</c> taken out.
    ///
    /// <para>For the one surface that has drawn the value somewhere better — a mark
    /// in the heading the block describes — where leaving the row as well would be
    /// the same fact said twice, once as a picture and once as the raw field it
    /// replaced. The caller that drew the mark is the caller that asks; nothing here
    /// decides that a mark was drawn.</para>
    ///
    /// <para>The same instance back when there is no <c>type</c> to remove, which is
    /// not only an allocation saved: <see cref="MetadataView"/> reseeds a reader's
    /// unsaved status choice whenever the record it was given stops comparing equal,
    /// and this record's collections compare by reference. A caller that asks on
    /// every render must therefore be handed the same object on every render — see
    /// the callers, which all ask once and cache.</para>
    /// </summary>
    public MetadataRecord WithoutType() =>
        Type is null
            ? this
            : this with
            {
                BeforeMark = this,
                Type = null,
                TypeReadFromKind = false,

                // A value that came from `kind` was drawn as the kind chip, so
                // that is the row the mark replaces. A `kind` written beside a
                // `type` is a second statement and keeps its own row.
                Kind = TypeReadFromKind ? null : Kind
            };

    /// <summary>
    /// The record as it was read, before <see cref="WithoutType"/> took the
    /// <c>type</c> out for a surface drawing it as a mark; null on a record nobody
    /// took anything out of.
    ///
    /// <para>What is drawn and what the block states are two questions. Taking the
    /// row away answers the first; the second still has the <c>type</c> in it —
    /// a <c>bounded-context</c> chapter's <c>deployment</c> is legal because of
    /// that type — so <see cref="Devbook.DevbookMetadataFindings"/> judges this
    /// record rather than the one on screen.</para>
    /// </summary>
    internal MetadataRecord? BeforeMark { get; init; }

    /// <summary>A block that stated nothing.</summary>
    public static MetadataRecord Empty { get; } = new();

    /// <summary>Whether there is anything at all to show. A chapter with no
    /// metadata should not leave a gap where the strip would have been.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Status)
        && Related.Count == 0
        && DependsOn.Count == 0
        && Implements.Count == 0
        && string.IsNullOrWhiteSpace(Issue)
        && Aliases.Count == 0
        && Alternatives.Count == 0
        && string.IsNullOrWhiteSpace(Kind)
        && string.IsNullOrWhiteSpace(Version)
        && Effort is null
        && Roadmap.Count == 0
        && FeatureFlag.Count == 0
        && string.IsNullOrWhiteSpace(Type)
        && Tests.Count == 0
        && Number is null
        && string.IsNullOrWhiteSpace(Index)
        && string.IsNullOrWhiteSpace(Date)
        && string.IsNullOrWhiteSpace(Deployment)
        && Ext.Count == 0
        && State.IsEmpty
        && Extra.Count == 0;

    /// <summary>
    /// Every reference this block carries, in field order and de-duplicated on
    /// the authored form. One target named by two fields is one edge in the
    /// graph, and a caller walking references should not visit it twice.
    /// </summary>
    public IReadOnlyList<DevbookReference> References
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            return [.. Related.Concat(DependsOn).Concat(Implements).Where(reference => seen.Add(reference.Raw))];
        }
    }
}

/// <summary>
/// The decision and review state a block carries: the nine fields
/// <see cref="DevbookSchema.StateFields"/> names, and nothing else.
///
/// <para>A record of its own rather than nine more properties on
/// <see cref="MetadataRecord"/>, because the difference is the point. These say
/// where a chapter stands on the way to a decision — who owes the next move, who
/// signed it off and on which day — and not what the chapter says. So a view draws
/// them beside the status, a context reader skips them, and a caller asking "is
/// there any state?" asks <see cref="IsEmpty"/> once instead of nine times.</para>
///
/// <para>Every value is kept exactly as authored. Whether a <c>review</c> is one of
/// the three states, whether an approval was signed and dated, and whether any of
/// it is legal in the folder at all are questions for
/// <c>DevbookMetadataFindings</c>, which knows the folder; this record only knows
/// what the block said.</para>
/// </summary>
public sealed record MetadataState
{
    /// <summary>Who approved the chapter: a person, a handle, or a team.</summary>
    public string? ApprovedBy { get; init; }

    /// <summary>The day it was approved, <c>YYYY-MM-DD</c>.</summary>
    public string? ApprovedAt { get; init; }

    /// <summary>The fingerprint of the approved content, <c>sha256:</c> and eight
    /// hex characters. Written by the approval gate, never by hand.</summary>
    public string? ApprovedHash { get; init; }

    /// <summary>Who accepted the built work against the chapter.</summary>
    public string? AcceptedBy { get; init; }

    /// <summary>The day it was accepted, on or after <see cref="ApprovedAt"/>.</summary>
    public string? AcceptedAt { get; init; }

    /// <summary>The fingerprint of the accepted content.</summary>
    public string? AcceptedHash { get; init; }

    /// <summary>Where the review pass stands: <c>requested</c>,
    /// <c>changes-requested</c> or <c>cleared</c>, as authored.</summary>
    public string? Review { get; init; }

    /// <summary>Who owes the next move: one handle, name, or role.</summary>
    public string? Reviewer { get; init; }

    /// <summary>The day the current review state was written.</summary>
    public string? ReviewAt { get; init; }

    /// <summary>No state at all.</summary>
    public static MetadataState Empty { get; } = new();

    /// <summary>Whether any of the three approval fields is written.</summary>
    public bool HasApproval =>
        Stated(ApprovedBy) || Stated(ApprovedAt) || Stated(ApprovedHash);

    /// <summary>Whether any of the three acceptance fields is written.</summary>
    public bool HasAcceptance =>
        Stated(AcceptedBy) || Stated(AcceptedAt) || Stated(AcceptedHash);

    /// <summary>Whether any of the review triad is written.</summary>
    public bool HasReview =>
        Stated(Review) || Stated(Reviewer) || Stated(ReviewAt);

    /// <summary>Whether the block stated none of the nine.</summary>
    public bool IsEmpty => !HasApproval && !HasAcceptance && !HasReview;

    /// <summary>The value one of the nine keys holds, by the key the file spells
    /// it with. Null for a key that is not one of them, or was not written.</summary>
    public string? this[string field] => field.Trim().ToLowerInvariant() switch
    {
        "approved-by" => ApprovedBy,
        "approved-at" => ApprovedAt,
        "approved-hash" => ApprovedHash,
        "accepted-by" => AcceptedBy,
        "accepted-at" => AcceptedAt,
        "accepted-hash" => AcceptedHash,
        "review" => Review,
        "reviewer" => Reviewer,
        "review-at" => ReviewAt,
        _ => null
    };

    private static bool Stated(string? value) => !string.IsNullOrWhiteSpace(value);
}
