using System.Globalization;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// One plan alone (local ADR 0062 §7; <c>.devbook/domain/tasks/features.md#plans-view</c>):
/// opened from the expanded plan's heading, it shows the plan's name, bar and counts, a
/// Waves / List toggle and a run line naming the plan branch the run lands on.
/// </summary>
public partial class TasksPane
{
    /// <summary>The plan opened on its own, by tag, or null while the Plans view reads
    /// as sections. Selecting another plan leaves it, since it names only this one.</summary>
    private string? _planFocus;

    /// <summary>How the opened plan is laid out; Waves each time a plan is opened.</summary>
    private PlanFocusLayout _planFocusLayout = PlanFocusLayout.Waves;

    /// <summary>The Waves / List toggle's two choices, in the order drawn.</summary>
    private static readonly PlanFocusLayout[] PlanFocusLayouts = [PlanFocusLayout.Waves, PlanFocusLayout.List];

    /// <summary>Whether the main column shows the selected plan alone.</summary>
    private bool PlanFocusShown =>
        PlanSectionsShown
        && _planFocus is { } focus
        && string.Equals(focus, State.SelectedPlan, StringComparison.OrdinalIgnoreCase);

    private void OpenPlanFocus(string tag)
    {
        _planFocus = tag;
        _planFocusLayout = PlanFocusLayout.Waves;
    }

    private void ClosePlanFocus() => _planFocus = null;

    private void SetPlanFocusLayout(PlanFocusLayout layout) => _planFocusLayout = layout;

    /// <summary>The opened plan's counts: <c>24 entries · 8 done · 71 pts left</c>.</summary>
    private static string PlanFocusCounts(PlanSectionsView view) =>
        $"{view.Total.ToString(CultureInfo.InvariantCulture)} {(view.Total == 1 ? "entry" : "entries")} · "
        + $"{view.Done.Count.ToString(CultureInfo.InvariantCulture)} done · "
        + $"{view.PointsLeft.ToString(CultureInfo.InvariantCulture)} {(view.PointsLeft == 1 ? "pt" : "pts")} left";
}
