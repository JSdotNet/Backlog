namespace Backlog.Modules.Goals.Abstractions;

/// <summary>
/// The feature keys the Goals context owns. Goals and impediments are always on
/// (<c>.devbook/domain/goals/features.md</c>); what is switched is what is not
/// finished yet.
/// </summary>
public static class GoalsFeatures
{
    /// <summary>The Weekly review takeover beside the Inbox in the Home shell's header
    /// (<c>.devbook/domain/goals/features.md#the-weekly-review</c>).</summary>
    public const string WeeklyReview = "weekly-review";
}
