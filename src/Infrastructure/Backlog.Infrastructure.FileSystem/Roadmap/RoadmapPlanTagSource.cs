using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// Answers Tasks' <see cref="IRoadmapTagSource"/> from the roadmap
/// plan.
/// <para>
/// This is the join, and it lives in an adapter on purpose: the backlog UI may not
/// reach into Roadmap Planning, so it asks its own port and this adapter — which is
/// allowed to see both — reads the plan through <see cref="IRoadmapPlanning"/> and
/// hands back the tags in use. The same arrangement <c>DevbookFolderSource</c>
/// uses to answer two contexts over one lookup.
/// </para>
/// </summary>
public sealed class RoadmapPlanTagSource : IRoadmapTagSource
{
    private readonly IRoadmapPlanning _planning;

    public RoadmapPlanTagSource(IRoadmapPlanning planning)
    {
        ArgumentNullException.ThrowIfNull(planning);
        _planning = planning;
    }

    /// <summary>The plan's own change signal, which is held across scopes, so a save
    /// from the roadmap band's scope reaches the task list in its own.</summary>
    public event Action? Changed
    {
        add => _planning.Changed += value;
        remove => _planning.Changed -= value;
    }

    public async Task<IReadOnlyList<string>> TagsInUseAsync(CancellationToken cancellationToken = default)
    {
        var plan = await _planning.GetPlanAsync(cancellationToken).ConfigureAwait(false);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tags = new List<string>();

        // First appearance wins, so the offered order is stable between reads — the
        // plan lists its items in one order and the picker should not shuffle them.
        //
        // Offered wearing the plan sigil, because that is how the backlog stores a
        // tag that names a roadmap item: the plan holds the bare slug, the entry
        // holds `+slug`, and the picker has to offer the value the entry will keep.
        foreach (var item in plan.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Tag)) continue;
            if (seen.Add(item.Tag)) tags.Add("+" + item.Tag);
        }

        return tags;
    }

    public async Task<IReadOnlyDictionary<string, DateOnly>> PlansOnMyDayAsync(CancellationToken cancellationToken = default)
    {
        var plan = await _planning.GetPlanAsync(cancellationToken).ConfigureAwait(false);

        var stamps = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Tag) || item.MyDayOn is not { } day) continue;

            // A shared tag is one plan; its items take the date together, so they agree
            // unless a device wrote one of them alone. The latest word is the one read.
            if (!stamps.TryGetValue(item.Tag, out var seen) || day > seen) stamps[item.Tag] = day;
        }

        return stamps;
    }

    public async Task<bool> SetPlanMyDayAsync(string planTag, DateOnly? on, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planTag);

        var put = await _planning.PutPlanOnMyDayAsync(planTag, on, cancellationToken).ConfigureAwait(false);
        return put is Ok<IReadOnlyList<RoadmapItemDto>>;
    }
}
