using Backlog.UI.Components.Metadata;

namespace Backlog.UI.Components.Knowledge;

/// <summary>
/// One visual scale behind five vocabularies.
///
/// <para>Each knowledge folder names its lifecycle in its own words — <c>.tech</c>
/// runs a tech-radar ladder, <c>.backlog</c> tracks task progress, <c>.arc42</c>
/// describes a standing decision — and none of them should be renamed to suit a
/// stylesheet. The tone is the shared axis underneath: how far along, and does it
/// want attention. It exists so a reader who has learned one folder's colours can
/// read another's at a glance without the folders having to agree on words.</para>
/// </summary>
public enum KnowledgeStatusTone
{
    /// <summary>No opinion: the folder is not known, or the status is not one of
    /// its values. Renders as plainly as the status did before tones existed.</summary>
    Unknown,

    /// <summary>Written down but not agreed — <c>draft</c>, <c>trial</c>.</summary>
    Provisional,

    /// <summary>Agreed and waiting — <c>proposed</c>, <c>ready</c>, <c>candidate</c>.</summary>
    Planned,

    /// <summary>Live and current — <c>active</c>, <c>in-progress</c>, <c>adopted</c>.</summary>
    Active,

    /// <summary>Finished — <c>done</c>.</summary>
    Complete,

    /// <summary>Stuck or being avoided — <c>blocked</c>, <c>hold</c>.</summary>
    Attention,

    /// <summary>Kept for history only — <c>deprecated</c>, <c>retired</c>.</summary>
    Retired
}

/// <summary>
/// The <c>status</c> vocabulary of each knowledge folder, and its mapping onto
/// <see cref="KnowledgeStatusTone"/>. Knowing the vocabulary is what lets a view
/// tell a status apart from a typo.
/// </summary>
public static class KnowledgeStatus
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
    public static MetadataStatusVocabulary Vocabulary(KnowledgeFolder folder) => folder switch
    {
        KnowledgeFolder.Arc42 => Arc42Vocabulary,
        KnowledgeFolder.Domain => DomainVocabulary,
        KnowledgeFolder.Design => DesignVocabulary,
        KnowledgeFolder.Backlog => BacklogVocabulary,
        KnowledgeFolder.Tech => TechVocabulary,
        _ => MetadataStatusVocabulary.None
    };

    // One per folder and not one per value list: .arc42 and .domain share a list
    // and do not share a tone mapping — `proposed` is Planned in both, but the
    // switch that says so is keyed on the folder — so a vocabulary built from the
    // list alone would answer for the wrong folder.
    private static readonly MetadataStatusVocabulary Arc42Vocabulary = For(KnowledgeFolder.Arc42);
    private static readonly MetadataStatusVocabulary DomainVocabulary = For(KnowledgeFolder.Domain);
    private static readonly MetadataStatusVocabulary DesignVocabulary = For(KnowledgeFolder.Design);
    private static readonly MetadataStatusVocabulary BacklogVocabulary = For(KnowledgeFolder.Backlog);
    private static readonly MetadataStatusVocabulary TechVocabulary = For(KnowledgeFolder.Tech);

    /// <summary>The folder's values, with the tone-to-badge mapping as the
    /// resolver. The resolver is only ever asked about a value the vocabulary
    /// recognises — the unrecognised case belongs to
    /// <see cref="MetadataStatusVocabulary"/> — so this is the tone mapping and
    /// nothing else.</summary>
    private static MetadataStatusVocabulary For(KnowledgeFolder folder) =>
        new(Values(folder), status => Modifier(Tone(folder, status)), AllowsNone(folder));

    /// <summary>
    /// Whether the folder lets a chapter state no status at all.
    ///
    /// <para>It splits on what the field is doing, which is not the same job in
    /// every folder. In <c>.arc42</c>, <c>.domain</c> and <c>.design</c> it records
    /// how settled the writing is, and <c>active</c> is a resting value — content
    /// that is simply current says nothing by saying nothing, so the field is worth
    /// writing only while a chapter is in transition or carries a standing warning.
    /// In <c>.tech</c> it is a position on an adoption ladder and in
    /// <c>.backlog</c> a work state; there every value is a claim the reader needs,
    /// and an absent one would be indistinguishable from <c>candidate</c> or from
    /// untracked. So those two keep it required.</para>
    /// </summary>
    private static bool AllowsNone(KnowledgeFolder folder) => folder switch
    {
        KnowledgeFolder.Arc42 or KnowledgeFolder.Domain or KnowledgeFolder.Design => true,
        _ => false
    };

    /// <summary>Which of the application's status badges a tone wears.
    ///
    /// <para>The answer is a modifier the stylesheet already defines, not a scale
    /// of this folder's own. Every tone has an exact counterpart in the
    /// application's status badge, so a knowledge status is drawn by the same rule
    /// as a backlog entry's rather than by a second set of rules that has to be
    /// kept in step with it by hand.</para></summary>
    private static string Modifier(KnowledgeStatusTone tone) => tone switch
    {
        KnowledgeStatusTone.Provisional => "draft",
        KnowledgeStatusTone.Planned => "ready",
        KnowledgeStatusTone.Active => "active",
        KnowledgeStatusTone.Complete => "done",
        KnowledgeStatusTone.Attention => "blocked",
        KnowledgeStatusTone.Retired => "archived",
        _ => string.Empty
    };

    /// <summary>The values a folder allows, in the order its own instructions
    /// list them.</summary>
    public static IReadOnlyList<string> Values(KnowledgeFolder folder) => folder switch
    {
        // Architecture and domain knowledge both describe a standing structure
        // rather than a task, which is why neither has a `done`.
        KnowledgeFolder.Arc42 or KnowledgeFolder.Domain => StandingValues,
        KnowledgeFolder.Design => DesignValues,
        KnowledgeFolder.Backlog => BacklogValues,
        KnowledgeFolder.Tech => TechValues,
        _ => []
    };

    /// <summary>Whether a status is one the folder recognises. Trimmed and
    /// case-insensitive: a stray capital is not a different status.</summary>
    public static bool IsKnown(KnowledgeFolder folder, string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;

        var value = status.Trim();
        foreach (var known in Values(folder))
        {
            if (known.Equals(value, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>
    /// The tone a status carries in its folder. Anything the folder does not
    /// recognise — and every status at all when the folder is not known — comes
    /// back <see cref="KnowledgeStatusTone.Unknown"/> rather than being guessed
    /// at: two folders spell different meanings with the same word, so a guess
    /// made without the folder would be wrong about half the time.
    /// </summary>
    public static KnowledgeStatusTone Tone(KnowledgeFolder folder, string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return KnowledgeStatusTone.Unknown;

        var value = status.Trim().ToLowerInvariant();

        return folder switch
        {
            KnowledgeFolder.Arc42 or KnowledgeFolder.Domain or KnowledgeFolder.Design => value switch
            {
                "draft" => KnowledgeStatusTone.Provisional,
                "proposed" when folder is not KnowledgeFolder.Design => KnowledgeStatusTone.Planned,
                "active" => KnowledgeStatusTone.Active,
                "deprecated" => KnowledgeStatusTone.Retired,
                _ => KnowledgeStatusTone.Unknown
            },
            KnowledgeFolder.Backlog => value switch
            {
                "draft" => KnowledgeStatusTone.Provisional,
                "ready" => KnowledgeStatusTone.Planned,
                "in-progress" => KnowledgeStatusTone.Active,
                "done" => KnowledgeStatusTone.Complete,
                "blocked" => KnowledgeStatusTone.Attention,
                _ => KnowledgeStatusTone.Unknown
            },
            KnowledgeFolder.Tech => value switch
            {
                // A candidate is named but unproven and a trial is being run:
                // the ladder puts candidate first, but the trial is the one that
                // is still tentative, which is why the two do not read in order.
                "candidate" => KnowledgeStatusTone.Planned,
                "trial" => KnowledgeStatusTone.Provisional,
                "adopted" => KnowledgeStatusTone.Active,
                "hold" => KnowledgeStatusTone.Attention,
                "retired" => KnowledgeStatusTone.Retired,
                _ => KnowledgeStatusTone.Unknown
            },
            _ => KnowledgeStatusTone.Unknown
        };
    }
}
