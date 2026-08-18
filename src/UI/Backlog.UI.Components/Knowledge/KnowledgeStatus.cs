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
