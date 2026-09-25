using Backlog.UI.Components.Metadata;

namespace Backlog.UI.Components.Devbook;

/// <summary>
/// One visual scale behind six folders' vocabularies.
///
/// <para>Each knowledge folder names its lifecycle in its own words — <c>.tech</c>
/// runs a tech-radar ladder, <c>.backlog</c> tracks task progress, <c>.arc42</c>
/// describes a standing decision — and none of them should be renamed to suit a
/// stylesheet. The tone is the shared axis underneath: how far along, and does it
/// want attention. It exists so a reader who has learned one folder's colours can
/// read another's at a glance without the folders having to agree on words.</para>
/// </summary>
public enum DevbookStatusTone
{
    /// <summary>No opinion: the folder is not known, or the status is not one of
    /// its values. Renders as plainly as the status did before tones existed.</summary>
    Unknown,

    /// <summary>Written down but not agreed — <c>draft</c>, <c>trial</c>.</summary>
    Provisional,

    /// <summary>Agreed and waiting — <c>proposed</c>, <c>ready</c>, <c>candidate</c>,
    /// and <c>domain/</c>'s <c>approved</c>.</summary>
    Planned,

    /// <summary>Live and current — <c>active</c>, <c>in-progress</c>, <c>adopted</c>.</summary>
    Active,

    /// <summary>Finished — <c>done</c>, and <c>domain/</c>'s <c>accepted</c>.</summary>
    Complete,

    /// <summary>Stuck or being avoided — <c>blocked</c>, <c>hold</c>.</summary>
    Attention,

    /// <summary>Kept for history only — <c>deprecated</c>, <c>retired</c>.</summary>
    Retired
}

/// <summary>
/// The <c>status</c> vocabulary of each knowledge folder, and its mapping onto
/// <see cref="DevbookStatusTone"/>. Knowing the vocabulary is what lets a view
/// tell a status apart from a typo.
/// </summary>
public static class DevbookStatus
{
    private static readonly string[] StandingValues = ["draft", "proposed", "active", "deprecated"];
    private static readonly string[] DesignValues = ["draft", "active", "deprecated"];
    private static readonly string[] BacklogValues = ["draft", "ready", "in-progress", "done", "blocked"];
    private static readonly string[] TechValues = ["candidate", "trial", "adopted", "hold", "retired"];

    /// <summary>
    /// A folder's vocabulary as the record views take it — the small adapter
    /// between "which knowledge folder is this" and "which status words are
    /// allowed, and what does each look like".
    ///
    /// <para><see cref="Metadata.MetadataView"/> used to take the folder itself and
    /// ask this class both questions. That was the one thing genuinely stopping a
    /// caller outside these five folders from drawing a record, so the views take
    /// the vocabulary now and this is where a knowledge surface gets one. Every
    /// knowledge caller behaves exactly as it did: the values are the same list in
    /// the same order, and the modifier is the same tone mapping.</para>
    ///
    /// <para>Held per folder rather than built per call, and that is not only
    /// thrift: a fresh object is a changed parameter to Blazor, so building one in
    /// a render expression would re-render every record on every pass.</para>
    /// </summary>
    public static MetadataStatusVocabulary Vocabulary(DevbookFolder folder) => folder switch
    {
        DevbookFolder.Arc42 => Arc42Vocabulary,
        DevbookFolder.Domain => DomainVocabulary,
        DevbookFolder.Design => DesignVocabulary,
        DevbookFolder.Backlog => BacklogVocabulary,
        DevbookFolder.Tech => TechVocabulary,
        DevbookFolder.Ai => AiVocabulary,
        _ => MetadataStatusVocabulary.None
    };

    // One per folder and not one per value list: .arc42 and .domain share a list
    // and do not share a tone mapping — `proposed` is Planned in both, but the
    // switch that says so is keyed on the folder — so a vocabulary built from the
    // list alone would answer for the wrong folder.
    private static readonly MetadataStatusVocabulary Arc42Vocabulary = For(DevbookFolder.Arc42);
    private static readonly MetadataStatusVocabulary DomainVocabulary = For(DevbookFolder.Domain);
    private static readonly MetadataStatusVocabulary DesignVocabulary = For(DevbookFolder.Design);
    private static readonly MetadataStatusVocabulary BacklogVocabulary = For(DevbookFolder.Backlog);
    private static readonly MetadataStatusVocabulary TechVocabulary = For(DevbookFolder.Tech);

    // Its own instance although its list and its tones are `.tech`'s, for the
    // reason the comment above gives: the vocabulary answers for a folder, and
    // handing `.ai` the `.tech` object would make a badge report the wrong one.
    private static readonly MetadataStatusVocabulary AiVocabulary = For(DevbookFolder.Ai);

    /// <summary>The folder's values, with the tone-to-badge mapping as the
    /// resolver. The resolver is only ever asked about a value the vocabulary
    /// recognises — the unrecognised case belongs to
    /// <see cref="MetadataStatusVocabulary"/> — so this is the tone mapping and
    /// nothing else.
    ///
    /// <para>The two contract-16 additions arrive the same way. The decision rungs
    /// go in as words recognised and never offered, and the resting value as the
    /// word the empty option stands for — both taken from
    /// <see cref="DevbookSchema"/> rather than restated, so the folder that rests
    /// and the folder that has rungs are decided in one place.</para></summary>
    private static MetadataStatusVocabulary For(DevbookFolder folder) =>
        new(
            Values(folder),
            status => Modifier(Tone(folder, status)),
            AllowsNone(folder),
            recognisedOnly: RecognisedOnly(folder),
            restingValue: DevbookSchema.RestsByOmission(folder) ? DevbookSchema.RestingStatus : null);

    /// <summary>
    /// Whether the folder lets a chapter state no status at all.
    ///
    /// <para>It splits on what the field is doing, which is not the same job in
    /// every folder. In <c>.arc42</c>, <c>.domain</c> and <c>.design</c> it records
    /// how settled the writing is, and <c>active</c> is a resting value — content
    /// that is simply current says nothing by saying nothing, so the field is worth
    /// writing only while a chapter is in transition or carries a standing warning.
    /// In <c>.tech</c> and <c>.ai</c> it is a position on an adoption ladder and
    /// in <c>.backlog</c> a work state; there every value is a claim the reader
    /// needs, and an absent one would be indistinguishable from <c>candidate</c>
    /// or from untracked. So those three keep it required.</para>
    ///
    /// <para>Contract 16 states the same split as a rule — the editorial folders
    /// rest at <c>active</c> by omission, the rating folders require the field —
    /// so it is asked of <see cref="DevbookSchema.RestsByOmission"/> rather than
    /// kept as a second list here. <c>.backlog</c> is in neither of the rule's
    /// rows and keeps its status required.</para>
    /// </summary>
    private static bool AllowsNone(DevbookFolder folder) => DevbookSchema.RestsByOmission(folder);

    /// <summary>
    /// The words a folder recognises and never offers: <c>domain/</c>'s two
    /// decision rungs, and nothing anywhere else.
    ///
    /// <para>Kept out of <see cref="Values"/> because that list is what a select
    /// offers and what the installed generator's <c>STATUS_BY_FOLDER</c> is pinned
    /// against. A rung is not a step a reader takes: the approval gate writes it
    /// together with the record that signs and dates it. Outside <c>domain/</c> the
    /// rule says a rung is not in the folder's vocabulary at all, so there it is
    /// unrecognised and flagged like any other word the folder never
    /// defined.</para>
    /// </summary>
    public static IReadOnlyList<string> RecognisedOnly(DevbookFolder folder) =>
        DevbookSchema.AllowsDecisionRungs(folder) ? DevbookSchema.DecisionRungs : [];

    /// <summary>Which of the application's status badges a tone wears.
    ///
    /// <para>The answer is a modifier the stylesheet already defines, not a scale
    /// of this folder's own. Every tone has an exact counterpart in the
    /// application's status badge, so a knowledge status is drawn by the same rule
    /// as a backlog entry's rather than by a second set of rules that has to be
    /// kept in step with it by hand.</para></summary>
    private static string Modifier(DevbookStatusTone tone) => tone switch
    {
        DevbookStatusTone.Provisional => "draft",
        DevbookStatusTone.Planned => "ready",
        DevbookStatusTone.Active => "active",
        DevbookStatusTone.Complete => "done",
        DevbookStatusTone.Attention => "blocked",
        DevbookStatusTone.Retired => "archived",
        _ => string.Empty
    };

    /// <summary>The values a folder allows, in the order its own instructions
    /// list them.</summary>
    public static IReadOnlyList<string> Values(DevbookFolder folder) => folder switch
    {
        // Architecture and domain knowledge both describe a standing structure
        // rather than a task, which is why neither has a `done`.
        DevbookFolder.Arc42 or DevbookFolder.Domain => StandingValues,
        DevbookFolder.Design => DesignValues,
        DevbookFolder.Backlog => BacklogValues,
        // `.ai` rates a way of working with a technology on the ladder `.tech`
        // rates the technology on — deliberately the same five words, so a
        // reader learns one adoption vocabulary and applies it in both folders.
        DevbookFolder.Tech or DevbookFolder.Ai => TechValues,
        _ => []
    };

    /// <summary>Whether a status is one the folder recognises — one of its
    /// <see cref="Values"/>, or one of the words it recognises and never offers.
    /// Trimmed and case-insensitive: a stray capital is not a different
    /// status.</summary>
    public static bool IsKnown(DevbookFolder folder, string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;

        var value = status.Trim();
        foreach (var known in Values(folder).Concat(RecognisedOnly(folder)))
        {
            if (known.Equals(value, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>
    /// The tone a status carries in its folder. Anything the folder does not
    /// recognise — and every status at all when the folder is not known — comes
    /// back <see cref="DevbookStatusTone.Unknown"/> rather than being guessed
    /// at: two folders spell different meanings with the same word, so a guess
    /// made without the folder would be wrong about half the time.
    /// </summary>
    public static DevbookStatusTone Tone(DevbookFolder folder, string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return DevbookStatusTone.Unknown;

        var value = status.Trim().ToLowerInvariant();

        return folder switch
        {
            DevbookFolder.Arc42 or DevbookFolder.Domain or DevbookFolder.Design => value switch
            {
                "draft" => DevbookStatusTone.Provisional,
                "proposed" when folder is not DevbookFolder.Design => DevbookStatusTone.Planned,
                "active" => DevbookStatusTone.Active,
                "deprecated" => DevbookStatusTone.Retired,
                // The decision rungs, domain/'s alone. `approved` is a specification
                // a person agreed and nothing has been built against yet — "agreed
                // and waiting" is exactly what Planned says, and it sits beside
                // `proposed` on purpose: both are the chapter's content waiting on
                // work, one asked for and one granted. `accepted` is the build
                // judged against it, the one state in this folder that is
                // finished, so it takes Complete — the tone `.backlog` gives `done`.
                "approved" when folder is DevbookFolder.Domain => DevbookStatusTone.Planned,
                "accepted" when folder is DevbookFolder.Domain => DevbookStatusTone.Complete,
                _ => DevbookStatusTone.Unknown
            },
            DevbookFolder.Backlog => value switch
            {
                "draft" => DevbookStatusTone.Provisional,
                "ready" => DevbookStatusTone.Planned,
                "in-progress" => DevbookStatusTone.Active,
                "done" => DevbookStatusTone.Complete,
                "blocked" => DevbookStatusTone.Attention,
                _ => DevbookStatusTone.Unknown
            },
            DevbookFolder.Tech or DevbookFolder.Ai => value switch
            {
                // A candidate is named but unproven and a trial is being run:
                // the ladder puts candidate first, but the trial is the one that
                // is still tentative, which is why the two do not read in order.
                "candidate" => DevbookStatusTone.Planned,
                "trial" => DevbookStatusTone.Provisional,
                "adopted" => DevbookStatusTone.Active,
                "hold" => DevbookStatusTone.Attention,
                "retired" => DevbookStatusTone.Retired,
                _ => DevbookStatusTone.Unknown
            },
            _ => DevbookStatusTone.Unknown
        };
    }
}
