using Backlog.UI.Components.Selects;

namespace Backlog.UI.Components.Metadata;

/// <summary>
/// The status words a surface allows, and how each of them is drawn.
///
/// <para>This is what <see cref="MetadataView"/> used to take a
/// <c>KnowledgeFolder</c> for. The record itself was never knowledge-shaped — a
/// field nobody writes is simply absent, which <see cref="MetadataRecord"/>
/// already models — but the status was: the view asked
/// <c>KnowledgeStatus.Values(folder)</c> which words were legal and
/// <c>KnowledgeStatusBadge.Slug(folder, status)</c> what each of them looked
/// like, so a caller outside <c>.arc42</c>, <c>.domain</c>, <c>.design</c>,
/// <c>.backlog</c> and <c>.tech</c> had no way in at all. Handing the vocabulary
/// down instead means the view knows what a status is worth without knowing what
/// a knowledge folder is.</para>
///
/// <para>Four questions and no more, because four is what the view actually
/// asks: which options may be offered, whether the one in hand is among them,
/// whether a select may be shown for it at all, and which of the application's
/// status badges it wears. Everything a folder knows beyond that stays in the
/// folder — including whether stating no status is allowed, which arrives here as
/// <see cref="AllowsNone"/> rather than as the view learning what a knowledge
/// folder is.</para>
///
/// <para>Instances are meant to be held, not built per render. A fresh object on
/// every render is a changed parameter to Blazor, so the knowledge side keeps one
/// per folder — see <c>KnowledgeStatus.Vocabulary</c>.</para>
/// </summary>
public sealed class MetadataStatusVocabulary
{
    /// <summary>No vocabulary at all: the status is shown and not judged.
    ///
    /// <para>The counterpart of <c>KnowledgeFolder.Unknown</c>, and the default
    /// every component takes. Nothing is unrecognised against it — there is
    /// nothing to recognise against — so a status drawn through it gets the plain
    /// badge, which is exactly the "no opinion" it means.</para></summary>
    public static readonly MetadataStatusVocabulary None = new([]);

    private readonly Func<string, string>? _slugFor;

    /// <param name="values">The words this surface allows, in the order it would
    /// list them. That order is the order a select offers them in, so it is the
    /// caller's to choose rather than something sorted here.</param>
    /// <param name="slugFor">Which <c>badge--status-*</c> modifier a value it
    /// recognises wears. Only ever asked about a value in
    /// <paramref name="values"/>: the unrecognised case is this type's, so a
    /// resolver never has to answer for a word it has never heard of. Null draws
    /// every value as the plain badge.</param>
    /// <param name="allowsNone">Whether stating no status at all is a legitimate
    /// answer on this surface, and so whether a reader may choose it. See
    /// <see cref="AllowsNone"/>.</param>
    public MetadataStatusVocabulary(IReadOnlyList<string> values, Func<string, string>? slugFor = null, bool allowsNone = false)
    {
        Values = values;
        _slugFor = slugFor;
        AllowsNone = allowsNone;
    }

    /// <summary>The words this surface allows, in the order it lists them.</summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>
    /// Whether "no status" is a state this surface allows, and therefore one a
    /// reader may pick.
    ///
    /// <para>It is a property of the surface and not of the control, because
    /// whether absence means anything depends entirely on what the status is
    /// doing. Where it records how settled a piece of writing is, there is a
    /// resting value that needs no saying and omitting it loses nothing. Where it
    /// is a rating on a ladder — how far a technology has been adopted — the
    /// position is the whole point of the field, and an absent one could not be
    /// told apart from the bottom rung. So the first kind of folder allows none
    /// and the second does not, and a caller cannot flip that from the call
    /// site.</para>
    /// </summary>
    public bool AllowsNone { get; }

    /// <summary>Whether there is a vocabulary here to judge anything against.
    /// Empty is not "nothing is allowed" — it is "nobody said", which is why an
    /// empty one flags nothing.</summary>
    public bool IsEmpty => Values.Count == 0;

    /// <summary>
    /// Whether a select may offer this status: the value has to be one of the
    /// options <em>exactly</em>.
    ///
    /// <para>Ordinal and untrimmed, unlike <see cref="Recognises"/>, and
    /// deliberately the stricter of the two. A browser matches a select's value
    /// against its options literally, so a status of <c>Adopted</c> put into a
    /// select offering <c>adopted</c> would leave the control showing the first
    /// option as though the file had said it. The pill takes that case instead and
    /// prints the word as it was written.</para>
    /// </summary>
    public bool Offers(string? status) =>
        status is not null && Values.Contains(status, StringComparer.Ordinal);

    /// <summary>
    /// Whether a select may be shown at all for this status — the question
    /// <see cref="Offers"/> used to be asked in place of.
    ///
    /// <para>Wider than <see cref="Offers"/> by exactly one case: a blank status
    /// on a surface that <see cref="AllowsNone"/>. That case is what keeps
    /// clearing a status reversible. Judged on <see cref="Offers"/> alone the
    /// control would vanish the moment a reader cleared it — a blank is in no
    /// vocabulary's word list — leaving a static badge with nothing in it and no
    /// way to set a status again.</para>
    ///
    /// <para><see cref="Offers"/> itself stays strict, because it answers a
    /// literal question about the DOM: is this value one of the option values a
    /// browser will match. Widening it would also make an absent status
    /// "offerable" on the read-only badge path, and an unrecognised word must
    /// still fall through to the pill that flags it.</para>
    /// </summary>
    public bool Selectable(string? status) =>
        Offers(status) || (AllowsNone && string.IsNullOrWhiteSpace(status));

    /// <summary>
    /// The options a select offers for this status: the vocabulary's words, led by
    /// an explicit "no status" entry where <see cref="AllowsNone"/>.
    ///
    /// <para>The blank belongs here rather than in the control. The status select
    /// is shared with surfaces that are not knowledge folders at all — a task's
    /// status among them — and a control parameter defaulting to off would protect
    /// those only until somebody flipped it for convenience. A vocabulary that
    /// answers <c>false</c> cannot be talked round from a call site.</para>
    ///
    /// <para>Its value is the empty string, because that is what a browser hands
    /// back for an option with no value and there is no point inventing a sentinel
    /// the DOM would only have to be translated out of. Its label is a word rather
    /// than a blank, so the closed control reads as a state that was chosen
    /// instead of a control that failed to load.</para>
    /// </summary>
    public IReadOnlyList<SelectorOption> Options()
    {
        var options = new List<SelectorOption>(Values.Count + 1);
        if (AllowsNone) options.Add(new SelectorOption(string.Empty, "No status"));
        options.AddRange(Values.Select(value => new SelectorOption(value, value)));

        return options;
    }

    /// <summary>Whether the word is one this surface knows. Trimmed and
    /// case-insensitive: a stray capital is not a different status.</summary>
    public bool Recognises(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;

        var value = status.Trim();

        foreach (var known in Values)
        {
            if (known.Equals(value, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>Whether there is a vocabulary and this value is not in it — which
    /// is nearly always a typo, and worth saying so. False for every value while
    /// <see cref="IsEmpty"/>: a surface that named no vocabulary has not claimed
    /// anything is wrong.
    ///
    /// <para>A blank is not a typo either. Stating no status is a legitimate answer
    /// where <see cref="AllowsNone"/>, and where it is not, the missing field is
    /// the index generator's complaint to make and not a word to flag as
    /// misspelled. Without this case a cleared status wore
    /// <see cref="UnrecognisedSlug"/> — the retired pill — under a tooltip saying
    /// it was unexpected. Two callers used to guard the blank themselves before
    /// asking; they no longer have to.</para></summary>
    public bool IsUnrecognised(string? status) =>
        !IsEmpty && !string.IsNullOrWhiteSpace(status) && !Recognises(status);

    /// <summary>The modifier a value nobody recognises wears.
    ///
    /// <para><c>archived</c> is the one <c>badge--status-*</c> modifier that spends
    /// no colour: a transparent surface inside a border. That is the honest reading
    /// of a word nobody recognises — not a state, rather than a state gone wrong —
    /// and it is what keeps the flag legible. Painted on the alarming surface
    /// instead it wore the same red as a legitimate <c>blocked</c> and the two
    /// could not be told apart at a glance. The urgency is carried by
    /// <see cref="Expectation"/> naming the words that were allowed.</para></summary>
    private const string UnrecognisedSlug = "archived";

    /// <summary>
    /// The <c>badge--status-*</c> modifier this status wears, or the empty string
    /// for one this vocabulary has no opinion about.
    ///
    /// <para>Empty rather than a fallback: the modifiers are the states the
    /// application badge knows, and every one of them is a real state. Claiming
    /// <c>draft</c> for a status nobody scoped would be painting a verdict nobody
    /// reached. An absent modifier leaves the plain <c>badge badge--status</c>,
    /// which is the "no opinion" being expressed.</para>
    /// </summary>
    public string SlugFor(string? status)
    {
        if (IsUnrecognised(status)) return UnrecognisedSlug;

        return string.IsNullOrWhiteSpace(status) ? string.Empty : _slugFor?.Invoke(status) ?? string.Empty;
    }

    /// <summary>Naming the values that were expected is the whole point of knowing
    /// the vocabulary: a status nobody recognises is nearly always a typo, and a
    /// typo is only useful once it is visible. Null for a status there is nothing
    /// to say about, which leaves the badge with no title at all.</summary>
    public string? Expectation(string? status) =>
        IsUnrecognised(status)
            ? $"Unexpected status. Expected one of: {string.Join(", ", Values)}."
            : null;
}
