using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// The Plans view's main column while a plan is selected on the rail (local ADR 0062 §3;
/// <c>.devbook/domain/tasks/features.md#plans-view</c>): the plan read as Now, Next and
/// Later by <see cref="PlanSections"/>, the other plans folded, then Unplanned.
/// </summary>
public partial class TasksPane
{
    /// <summary>How many unplanned rows close the column before its "n more".</summary>
    private const int UnplannedShown = 4;

    /// <summary>Raised with a plan's name to open it on the Roadmap; the heading offers
    /// no Roadmap link while the host wired nothing.</summary>
    [Parameter]
    public EventCallback<string> OnOpenRoadmap { get; set; }

    /// <summary>Which of Later's two lines has its list open, or null for neither.</summary>
    private PlanSection? _planLaterOpen;

    /// <summary>The plan whose Later line was opened, so selecting another plan shuts it.</summary>
    private string? _planLaterPlan;

    /// <summary>Whether the main column reads as plan sections: the Plans view on a
    /// wide or narrow column with a plan the rail lists selected. A plan pressed some
    /// other way that the rail does not list keeps the one list.</summary>
    private bool PlanSectionsShown =>
        PlansGrouped
        && State.SelectedPlan is { } selected
        && !State.NowAcrossPlansOnly
        && State.MyDayOn is null
        && RailPlans().Any(plan => string.Equals(plan.Tag, selected, StringComparison.OrdinalIgnoreCase));

    /// <summary>The selected plan expanded, or null when the rail lists no such plan.</summary>
    private PlanSectionsView? SelectedPlanSections(IReadOnlyList<PlanRailPlan> plans)
    {
        if (State.SelectedPlan is not { } tag) return null;
        if (plans.FirstOrDefault(plan => string.Equals(plan.Tag, tag, StringComparison.OrdinalIgnoreCase)) is not { } selected) return null;

        if (!string.Equals(_planLaterPlan, selected.Tag, StringComparison.OrdinalIgnoreCase))
        {
            _planLaterPlan = selected.Tag;
            _planLaterOpen = null;
        }

        return PlanSections.Build(selected, State.PlanEntryStatesOf(selected.Tag, PlanRunOf(selected.Tag)), State.ResolvedDependsOn);
    }

    /// <summary>The plan's run as the pane knows it: its open request, and no stage
    /// source yet — an entry in a session reads off its own status.</summary>
    private PlanRun PlanRunOf(string tag) => PlanRun.From(_planSessions.OpenFor(tag));

    /// <summary>How many of a plan's entries read under Next and how many wait, for its
    /// folded note.</summary>
    private (int Ready, int Waiting) PlanSectionCounts(string tag)
    {
        var sections = State.PlanEntryStatesOf(tag, PlanRunOf(tag)).Select(pair => PlanSections.SectionOf(pair.Row, pair.State)).ToList();

        return (sections.Count(section => section is PlanSection.Next), sections.Count(section => section is PlanSection.Waiting));
    }

    /// <summary>The open entries in no plan, off the rows the repository scope leaves in view.</summary>
    private IReadOnlyList<EntryRow> UnplannedRows() =>
        [.. State.ScopedRows.Where(row => !row.IsPreviewCompleted && TasksDesktopState.IsUnplanned(row))];

    private void TogglePlanLater(PlanSection section) =>
        _planLaterOpen = _planLaterOpen == section ? null : section;

    /// <summary>A Next row's type and points: <c>prompt · 5 pt</c>.</summary>
    private static string PlanRowTypeAndPoints(EntryRow row) =>
        row.PreviewEffort is { } points
            ? $"{row.PreviewType.ToString().ToLowerInvariant()} · {points.ToString(CultureInfo.InvariantCulture)} pt"
            : row.PreviewType.ToString().ToLowerInvariant();

    /// <summary>
    /// Select → new plan. Promote to plan was dropped (local ADR 0020 §7), so this is
    /// the way the pane already files entries under a plan: the list narrowed to
    /// Unplanned with selection on, the picked rows then given a plan tag.
    /// </summary>
    private void OnSelectForNewPlan()
    {
        if (!State.IsRailItemSelected(PlanRailItem.Unplanned)) OnRailItemPressed(PlanRailItem.Unplanned);
        State.SetSelectionMode(true);
    }
}
