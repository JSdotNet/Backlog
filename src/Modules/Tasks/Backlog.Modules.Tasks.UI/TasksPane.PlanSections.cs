using System.Globalization;
using Backlog.Modules.Tasks.Abstractions.Services;
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

            // A plan opened on its own stays open only while it stays selected:
            // coming back to it later shows the sections again.
            if (!string.Equals(_planFocus, selected.Tag, StringComparison.OrdinalIgnoreCase)) _planFocus = null;
        }

        return PlanSections.Build(selected, State.PlanEntryStatesOf(selected.Tag, PlanRunOf(selected.Tag)), State.ResolvedDependsOn);
    }

    /// <summary>The plan's run as the pane knows it: its open request, and the stage each
    /// entry's plan session reported. An entry no session reports reads off its own status.</summary>
    private PlanRun PlanRunOf(string tag) => PlanRun.From(_planSessions.OpenFor(tag), _planStages);

    /// <summary>Where the stages come from, or null when the host composed none.</summary>
    private IPlanEntryStages? _planStageSource;

    /// <summary>The last answer <see cref="_planStageSource"/> gave, by entry id.</summary>
    private IReadOnlyDictionary<Guid, string> _planStages = PlanRun.None.Stages;

    /// <summary>Moved by every read of the stages, so a slow read that answers after a
    /// newer one is dropped rather than drawing the older answer.</summary>
    private int _planStagesVersion;

    /// <summary>Starts listening for the plan sessions' stages and reads them once.</summary>
    private void StartPlanStages()
    {
        _planStageSource = Services.GetService(typeof(IPlanEntryStages)) as IPlanEntryStages;
        if (_planStageSource is null) return;

        _planStageSource.Changed += HandlePlanStagesChanged;
        _ = RefreshPlanStagesAsync();
    }

    private void StopPlanStages()
    {
        if (_planStageSource is not null) _planStageSource.Changed -= HandlePlanStagesChanged;
    }

    private void HandlePlanStagesChanged() => _ = RefreshPlanStagesAsync();

    /// <summary>Reads the stages again and redraws, on the renderer's thread and only
    /// when no newer read set out after this one.</summary>
    private async Task RefreshPlanStagesAsync()
    {
        if (_planStageSource is null) return;

        var version = Interlocked.Increment(ref _planStagesVersion);
        IReadOnlyDictionary<Guid, string> answer;

        try
        {
            answer = await _planStageSource.ReadAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            answer = PlanRun.None.Stages;
        }

        try
        {
            await InvokeAsync(() =>
            {
                if (version != Volatile.Read(ref _planStagesVersion)) return;

                _planStages = answer;
                StateHasChanged();
            }).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The pane went while the read was out.
        }
    }

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
