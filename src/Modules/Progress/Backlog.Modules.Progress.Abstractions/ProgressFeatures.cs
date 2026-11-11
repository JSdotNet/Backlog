namespace Backlog.Modules.Progress.Abstractions;

/// <summary>
/// The feature keys the Progress context owns. Goals and impediments are always on
/// (<c>.devbook/domain/progress/features.md</c>); what is switched is what is not
/// finished yet.
/// </summary>
public static class ProgressFeatures
{
    /// <summary>The Weekly review takeover beside the Inbox in the Home shell's header
    /// (<c>.devbook/domain/progress/features.md#the-weekly-review</c>).</summary>
    public const string WeeklyReview = "weekly-review";
}
