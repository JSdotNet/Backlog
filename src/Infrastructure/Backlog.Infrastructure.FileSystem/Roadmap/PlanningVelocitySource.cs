using Backlog.Modules.Roadmap.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// Answers Roadmap's <see cref="IPlanningVelocitySettings"/> from the per-device
/// settings file.
/// <para>
/// A pass-through, and it exists for the boundary rather than for the logic: the
/// module may not reference infrastructure, so it asks its own port and this —
/// which may see both — reads <see cref="PlanningVelocitySettingsStore"/>. The same
/// arrangement <see cref="RoadmapPlanTagSource"/> uses for the tag picker.
/// </para>
/// <para>
/// Read per call rather than pinned at construction, so a pace changed on the
/// roadmap is what the next placement divides by — including the re-lengthening the
/// roadmap runs straight after the change (ADR 0013, ruling 5 as amended).
/// </para>
/// </summary>
public sealed class PlanningVelocitySource : IPlanningVelocitySettings
{
    private readonly PlanningVelocitySettingsStore _settings;

    public PlanningVelocitySource(PlanningVelocitySettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    public event Action? Changed
    {
        add => _settings.Changed += value;
        remove => _settings.Changed -= value;
    }

    public decimal Manual => _settings.StoryPointsPerWeek;

    public PaceSource Source => _settings.Source;

    public string? SetManual(string? typed) => _settings.Set(typed);

    public string? Choose(PaceSource source) => _settings.Choose(source);
}
