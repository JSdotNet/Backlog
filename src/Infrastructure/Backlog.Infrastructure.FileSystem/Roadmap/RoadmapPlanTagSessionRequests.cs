using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// Answers Tasks' <see cref="IPlanTagSessionRequests"/> — the plan filter chip's queue
/// icon — from the roadmap: the repositories a plan is filed under are read from its
/// roadmap item through <see cref="IRoadmapPlanning"/>, and the requests go through the
/// roadmap's own <see cref="IPlanSessionRequests"/>, so a plan queued from the filter bar
/// is the same request the roadmap item's queue icon shows, and either one cancels it.
/// <para>
/// The join lives here for the reason <see cref="RoadmapPlanTagSource"/>'s does: the
/// backlog UI may not reach into Roadmap Planning. A host that composed no plan queue
/// leaves <see cref="IPlanSessionRequests"/> unregistered, and this answers that the
/// action is not offered.
/// </para>
/// </summary>
public sealed class RoadmapPlanTagSessionRequests : IPlanTagSessionRequests
{
    private readonly IRoadmapPlanning _planning;
    private readonly IPlanSessionRequests? _queue;

    /// <param name="planning">The roadmap's plan, which says where each plan is filed.</param>
    /// <param name="queue">The roadmap's plan session queue, or null when the host
    /// composed none.</param>
    public RoadmapPlanTagSessionRequests(IRoadmapPlanning planning, IPlanSessionRequests? queue)
    {
        ArgumentNullException.ThrowIfNull(planning);
        _planning = planning;
        _queue = queue;
    }

    /// <summary>The plan's own change signal, held across scopes, so an item's
    /// repositories edited on the roadmap reach the filter bar in its own scope.</summary>
    public event Action? PlanChanged
    {
        add => _planning.Changed += value;
        remove => _planning.Changed -= value;
    }

    public bool IsOffered => _queue?.IsOffered ?? false;

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> RepositoriesAsync(CancellationToken cancellationToken = default)
    {
        var plan = await _planning.GetPlanAsync(cancellationToken).ConfigureAwait(false);
        var repositories = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in plan.Items)
        {
            var tag = (item.Tag ?? string.Empty).Trim().TrimStart('+', '#').Trim();
            if (tag.Length > 0) repositories.TryAdd(tag, item.RepositoryAliases);
        }

        return repositories;
    }

    public async Task<IReadOnlyDictionary<string, EntrySessionRequest>> OpenAsync(CancellationToken cancellationToken = default)
    {
        if (_queue is null) return new Dictionary<string, EntrySessionRequest>(StringComparer.OrdinalIgnoreCase);

        var open = await _queue.OpenAsync(cancellationToken).ConfigureAwait(false);

        return open.ToDictionary(pair => pair.Key, pair => Translate(pair.Value), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<EntrySessionRequest> RequestAsync(
        string planTag,
        string repository,
        string cloneFolder,
        CancellationToken cancellationToken = default)
    {
        if (_queue is null) throw new InvalidOperationException("No plan session queue is composed.");

        return Translate(await _queue.RequestAsync(planTag, repository, cloneFolder, cancellationToken).ConfigureAwait(false));
    }

    public Task<bool> CancelAsync(Guid requestId, CancellationToken cancellationToken = default) =>
        _queue is null ? Task.FromResult(false) : _queue.CancelAsync(requestId, cancellationToken);

    private static EntrySessionRequest Translate(PlanSessionRequest request) =>
        new(
            request.Id,
            request.State switch
            {
                PlanSessionRequestState.Claimed => EntrySessionRequestState.Claimed,
                PlanSessionRequestState.Started => EntrySessionRequestState.Started,
                _ => EntrySessionRequestState.Requested
            },
            request.Repository);
}
