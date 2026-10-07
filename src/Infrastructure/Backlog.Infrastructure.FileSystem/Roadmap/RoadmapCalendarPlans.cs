using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// ADAPTER — answers Tasks' <see cref="ICalendarPlans"/> from the Roadmap context's own
/// abstractions: the plan through <see cref="IRoadmapPlanning"/>, its windows as the
/// keep-up projection reads them today (<see cref="EffortWindow"/>), what each item
/// gathered through <see cref="IRoadmapItemRollup"/>, and the shelf through
/// <see cref="IImportedPlanSource"/>.
/// <para>
/// It holds no roadmap rule. A window started on a day goes through
/// <see cref="IRoadmapPlanning.ImportPlanItemsAsync"/> — the door the roadmap's own shelf
/// uses — carrying the day as the entry's <see cref="PlanImportEntryDto.Start"/>, so the
/// roadmap places it and counts its end from the plan's points and the pace in use.
/// </para>
/// <para>
/// <see cref="Changed"/> is raised for everything that moves what the Calendar draws — the
/// plan, the work its items gather (<see cref="IRoadmapWorkChanges"/>) and the pace
/// (<see cref="IPlanningPace"/>) — the three the roadmap band redraws on. The shelf is the
/// band's own (<see cref="RoadmapShelf"/>).
/// </para>
/// <para>
/// With the roadmap switched off nothing is read, and the Calendar offers no plans: a
/// plan somebody turned off is not one to draw behind their back.
/// </para>
/// </summary>
public sealed class RoadmapCalendarPlans : ICalendarPlans, IDisposable
{
    private readonly IRoadmapPlanning _planning;
    private readonly IRoadmapItemRollup _rollups;
    private readonly IPlanningVelocity _velocity;
    private readonly IImportedPlanSource _imported;
    private readonly IAppFeatureSettings? _features;
    private readonly ShellNavigationStore? _store;
    private readonly IRoadmapWorkChanges? _work;
    private readonly IPlanningPace? _pace;
    private bool _shown = true;

    public RoadmapCalendarPlans(
        IRoadmapPlanning planning,
        IRoadmapItemRollup rollups,
        IPlanningVelocity velocity,
        IImportedPlanSource imported,
        IAppFeatureSettings? features = null,
        ShellNavigationStore? store = null,
        IRoadmapWorkChanges? work = null,
        IPlanningPace? pace = null)
    {
        ArgumentNullException.ThrowIfNull(planning);
        ArgumentNullException.ThrowIfNull(rollups);
        ArgumentNullException.ThrowIfNull(velocity);
        ArgumentNullException.ThrowIfNull(imported);

        _planning = planning;
        _rollups = rollups;
        _velocity = velocity;
        _imported = imported;
        _features = features;
        _store = store;
        _work = work;
        _pace = pace;
        _planning.Changed += OnPlanChanged;
        if (_work is not null) _work.Changed += OnPlanChanged;
        if (_pace is not null) _pace.Changed += OnPlanChanged;
    }

    public event Action? Changed;

    public bool Shown => _store?.CalendarPlansShown ?? _shown;

    public void SetShown(bool shown)
    {
        if (_store is null)
        {
            _shown = shown;
            return;
        }

        _store.SetCalendarPlansShown(shown);
    }

    public async Task<CalendarPlansDto> ReadAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        if (!RoadmapOn) return CalendarPlansDto.Off;

        var plan = await _planning.GetPlanAsync(cancellationToken).ConfigureAwait(false);
        var gathered = plan.Items.Count == 0
            ? new Dictionary<Guid, RoadmapItemRollupDto>()
            : await _rollups.GatherPlanAsync(plan, cancellationToken).ConfigureAwait(false);
        var paces = await _velocity.ReadPacesInUseAsync(cancellationToken).ConfigureAwait(false);

        var read = EffortWindow.Derive(plan.Items, gathered, paces, today, plan.Milestones);
        var imported = await _imported.ListAsync(cancellationToken).ConfigureAwait(false);

        return new CalendarPlansDto(
            [
                .. read.Select(item =>
                {
                    var rollup = gathered.GetValueOrDefault(item.Id) ?? RoadmapItemRollupDto.Empty;
                    return new CalendarPlanWindowDto(
                        item.Id,
                        item.Title,
                        item.Tag,
                        item.Start,
                        item.End,
                        item.RepositoryAliases,
                        rollup.DoneEffort,
                        rollup.TotalEffort);
                })
            ],
            [.. plan.Milestones.Select(milestone => new CalendarMilestoneDto(milestone.Id, milestone.Title, milestone.On))],
            [
                .. RoadmapShelf.Unplanned(imported, plan).Select(candidate => new CalendarShelfPlanDto(
                    candidate.Tag,
                    RoadmapShelf.TitleOf(candidate.Tag),
                    candidate.RepositoryAliases,
                    candidate.TaskCount,
                    candidate.TotalEffort,
                    candidate.UnestimatedCount))
            ]);
    }

    public async Task<string?> StartAsync(string tag, DateOnly start, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        if (!RoadmapOn) return "The roadmap is switched off.";

        var bare = tag.StartsWith('+') ? tag[1..] : tag;

        // Only a plan still on the shelf: one an item already carries is planned, and a
        // drop must never re-place that item's window.
        var plan = await _planning.GetPlanAsync(cancellationToken).ConfigureAwait(false);
        var shelf = RoadmapShelf.Unplanned(await _imported.ListAsync(cancellationToken).ConfigureAwait(false), plan);
        var imported = shelf.FirstOrDefault(candidate => RoadmapShelf.SameTag(candidate.Tag, bare));
        if (imported is null) return $"+{bare} is no longer waiting on the shelf.";

        // The entry the roadmap's own shelf builds, with the day the person chose: the
        // same import, so the window is the one the roadmap places.
        var entry = new PlanImportEntryDto(
            Title: RoadmapShelf.TitleOf(imported.Tag),
            Tag: imported.Tag,
            RepositoryAliases: imported.RepositoryAliases,
            Start: start);

        var result = await _planning.ImportPlanItemsAsync([entry], [imported.Effort], cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.IsFailure ? result.Error.Message : null;
    }

    private bool RoadmapOn => _features?.IsEnabled(RoadmapFeatures.Roadmap) ?? true;

    private void OnPlanChanged() => Changed?.Invoke();

    public void Dispose()
    {
        _planning.Changed -= OnPlanChanged;
        if (_work is not null) _work.Changed -= OnPlanChanged;
        if (_pace is not null) _pace.Changed -= OnPlanChanged;
    }
}
