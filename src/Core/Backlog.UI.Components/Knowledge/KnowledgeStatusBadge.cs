namespace Backlog.UI.Components.Knowledge;

/// <summary>
/// Which application status badge a knowledge status wears, wherever it is drawn.
///
/// <para>A status has two forms — the read-only <see cref="KnowledgeStatusPill"/>
/// and the select the record's headline offers once the folder names a
/// vocabulary — and they are the same fact about the same chapter. Built twice
/// they drifted, so both come through here: one answer, and the two cannot
/// disagree.</para>
///
/// <para>The answer is a modifier the stylesheet already defines, not a scale of
/// this folder's own. Every tone has an exact counterpart in the application's
/// status badge, so a knowledge status is drawn by the same rule as a backlog
/// entry's rather than by a second set of rules that has to be kept in step with
/// it by hand.</para>
/// </summary>
internal static class KnowledgeStatusBadge
{
    /// <summary>The modifier a status the folder does not define wears.
    ///
    /// <para><c>archived</c> is the one modifier that spends no colour: a
    /// transparent surface inside a border. That is the honest reading of a word
    /// nobody recognises — not a state, rather than a state gone wrong — and it
    /// is what keeps the flag legible. Painted on the alarming surface instead it
    /// wore the same red as a legitimate <c>blocked</c> status and the two could
    /// not be told apart at a glance. The urgency is carried by the title
    /// naming the values the folder actually allows.</para></summary>
    private const string UnrecognisedSlug = "archived";

    /// <summary>The <c>badge--status-*</c> modifier a status maps onto through its
    /// tone, or the empty string for a status that has no tone.
    ///
    /// <para>Empty rather than a fallback: the modifiers are the states the
    /// application badge knows, and every one of them is a real state. Claiming
    /// <c>draft</c> for a block whose folder was never given would be painting a
    /// verdict nobody reached. An absent modifier leaves the plain
    /// <c>badge badge--status</c>, which is exactly the "no opinion" the tone is
    /// expressing.</para>
    ///
    /// <para>A status the folder does define but does not recognise is answered
    /// here rather than by the caller, and answered before the tone is consulted
    /// at all. <see cref="IsUnrecognised"/> and this method are one decision read
    /// two ways, so asking them separately is the only way they could ever
    /// disagree.</para></summary>
    public static string Slug(KnowledgeFolder folder, string? status) =>
        IsUnrecognised(folder, status)
            ? UnrecognisedSlug
            : KnowledgeStatus.Tone(folder, status) switch
            {
                KnowledgeStatusTone.Provisional => "draft",
                KnowledgeStatusTone.Planned => "ready",
                KnowledgeStatusTone.Active => "active",
                KnowledgeStatusTone.Complete => "done",
                KnowledgeStatusTone.Attention => "blocked",
                KnowledgeStatusTone.Retired => "archived",
                _ => string.Empty
            };

    /// <summary>Whether the folder is known and does not define this status —
    /// which is nearly always a typo, and worth saying so. Only ever true of a
    /// pill: the headline offers a select exactly when the status is one of the
    /// folder's values, so a select can never be wearing this.</summary>
    public static bool IsUnrecognised(KnowledgeFolder folder, string? status) =>
        folder is not KnowledgeFolder.Unknown && !KnowledgeStatus.IsKnown(folder, status);
}
