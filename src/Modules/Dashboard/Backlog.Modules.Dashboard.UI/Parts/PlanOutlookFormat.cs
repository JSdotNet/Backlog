using Backlog.Modules.Dashboard.Abstractions.Insights;

namespace Backlog.Modules.Dashboard.UI.Parts;

/// <summary>
/// How a roadmap item's outlook reads, in the one set of words the Tasks tab's timeline
/// and the Overview's roadmap rows both print. Here once, so an item behind on one tab
/// cannot read as something else on the other.
/// </summary>
public static class PlanOutlookFormat
{
    /// <summary>The outlook in words: <c>On track</c>, <c>Behind</c>, and so on.</summary>
    public static string Word(PlanOutlook outlook) => outlook switch
    {
        PlanOutlook.Finished => "Finished",
        PlanOutlook.OnTrack => "On track",
        PlanOutlook.Behind => "Behind",
        PlanOutlook.Overdue => "Overdue",
        PlanOutlook.Unsized => "Unsized",
        PlanOutlook.NoPace => "No pace",
        _ => "Placed by effort"
    };

    /// <summary>
    /// The outlook as the board's chip says it: <c>Done</c>, <c>On track</c>,
    /// <c>Behind</c> or <c>Not started</c> (decisions Q8 and AC9).
    /// <para>
    /// Overdue reads Behind: both are work landing later than planned, in one tone. Any
    /// item that is not late and has nothing done yet reads Not started, whatever its
    /// outlook, whether it starts after today or started with nothing ticked off. Once
    /// something is done, the outlooks the board has no word for (unsized, no pace,
    /// placed by effort) read On track, the nearest, and keep their precise
    /// <see cref="Word"/> for the tooltip and the sentence beside the bar.
    /// </para>
    /// </summary>
    public static string Chip(PlanItemInsight row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.Outlook == PlanOutlook.Finished) return "Done";
        if (IsLate(row.Outlook)) return "Behind";

        var nothingDone = row.Item.DoneEffort == 0 && row.Item.DoneCount == 0;

        return nothingDone ? "Not started" : "On track";
    }

    /// <summary>Whether the item's work is landing later than planned — behind or
    /// overdue, the two outlooks the timeline draws in the behind tone.</summary>
    public static bool IsLate(PlanOutlook outlook) =>
        outlook is PlanOutlook.Behind or PlanOutlook.Overdue;

    /// <summary>The share of an item the done effort fills, as a whole percentage. A
    /// finished item fills it whatever it was estimated at; an unsized one fills none.</summary>
    public static int DoneShare(PlanItemInsight row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return row.Outlook == PlanOutlook.Finished
            ? 100
            : row.Item.TotalEffort <= 0
                ? 0
                : Math.Clamp((int)Math.Round(100m * row.Item.DoneEffort / row.Item.TotalEffort, MidpointRounding.AwayFromZero), 0, 100);
    }
}
