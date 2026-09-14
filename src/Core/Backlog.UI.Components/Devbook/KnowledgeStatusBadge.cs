namespace Backlog.UI.Components.Knowledge;

/// <summary>
/// Which application status badge a knowledge status wears, wherever it is drawn.
///
/// <para>A status has two forms — the read-only <see cref="KnowledgeStatusPill"/>
/// and the select the record's headline offers once the vocabulary names one — and
/// they are the same fact about the same chapter. Built twice they drifted, so
/// both come through one answer and the two cannot disagree.</para>
///
/// <para>That answer now lives on
/// <see cref="Metadata.MetadataStatusVocabulary"/>, which is what the record views
/// take in place of a folder. This is the folder-shaped way of asking it, kept
/// because a knowledge surface holds a folder and not a vocabulary, and because
/// the mapping from one to the other is worth naming once. Nothing here decides
/// anything of its own any more.</para>
/// </summary>
internal static class KnowledgeStatusBadge
{
    /// <summary>The <c>badge--status-*</c> modifier a status maps onto through its
    /// tone, the unrecognised modifier for a word the folder does not define, or
    /// the empty string for a status no folder scoped.</summary>
    public static string Slug(KnowledgeFolder folder, string? status) =>
        KnowledgeStatus.Vocabulary(folder).SlugFor(status);

    /// <summary>Whether the folder is known and does not define this status —
    /// which is nearly always a typo, and worth saying so. Only ever true of a
    /// pill: the headline offers a select exactly when the status is one of the
    /// folder's values, so a select can never be wearing this.</summary>
    public static bool IsUnrecognised(KnowledgeFolder folder, string? status) =>
        KnowledgeStatus.Vocabulary(folder).IsUnrecognised(status);
}
