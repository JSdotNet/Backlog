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
/// roadmap is what the next placement divides by — including the redraw the roadmap
/// runs straight after the change, which reads every window still sized by effort at
/// it (ADR 0013, ruling 5 as amended; local ADR 0018).
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

    public decimal Manual(string? repository = null) => _settings.StoryPointsPerWeekFor(repository);

    public PaceSource Source(string? repository = null) => _settings.SourceFor(repository);

    public string? SetManual(string? typed, string? repository = null) => _settings.Set(typed, repository);

    public string? Choose(PaceSource source, string? repository = null) => _settings.Choose(source, repository);
}
