using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Roadmap.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Progress;

/// <summary>
/// Progress's <see cref="IStandUpPace"/>, answered from Roadmap Planning's published
/// <see cref="IPlanningVelocity"/>: the global pace, the one every plan filed under no
/// configured repository is placed at (<c>.devbook/domain/progress/domain.md#pace</c>). It is
/// resolved per call, as the other adapters here resolve theirs, so a host without the
/// roadmap answers no pace rather than failing the provider.
/// </summary>
public sealed class PlanningVelocityStandUpPace(Func<IPlanningVelocity?> velocity) : IStandUpPace
{
    public async Task<decimal?> PointsPerWeekAsync(CancellationToken cancellationToken = default) =>
        velocity() is { } roadmap
            ? await roadmap.GetStoryPointsPerWeekAsync(null, cancellationToken).ConfigureAwait(false)
            : null;
}
