using Backlog.Modules.Roadmap.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// Answers Roadmap's <see cref="IPlanningVelocity"/> from the per-device settings
/// file.
/// <para>
/// A one-line adapter, and it exists for the boundary rather than for the logic:
/// the module may not reference infrastructure, so it asks its own port and this —
/// which may see both — reads
/// <see cref="PlanningVelocitySettingsStore"/>. The same arrangement
/// <see cref="RoadmapPlanTagSource"/> uses for the tag picker.
/// </para>
/// <para>
/// Read per call rather than pinned at construction, so a pace changed on the
/// settings screen is what the next placement divides by. Nothing already drawn
/// moves — a window is stored, not recomputed (ADR 0013, ruling 5).
/// </para>
/// </summary>
public sealed class PlanningVelocitySource : IPlanningVelocity
{
    private readonly PlanningVelocitySettingsStore _settings;

    public PlanningVelocitySource(PlanningVelocitySettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    public decimal StoryPointsPerDay => _settings.StoryPointsPerDay;
}
