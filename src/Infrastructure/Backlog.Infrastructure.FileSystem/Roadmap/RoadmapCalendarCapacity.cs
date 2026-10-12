using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// ADAPTER — answers Tasks' <see cref="ICalendarCapacity"/> from the working week the
/// Roadmap keeps (local ADR 0019): its Hours and its Days off, read through
/// <see cref="IPlanningVelocitySettings.WorkingWeek"/> and never written.
/// <para>
/// One way only. The Calendar's planned hours are the person's own plan; nothing here,
/// and nothing in Tasks, hands them to the roadmap, so a block can never move a window
/// or change a pace.
/// </para>
/// <para>
/// <see cref="Changed"/> is the settings' own signal, which a change to the working week
/// or a day off raises.
/// </para>
/// </summary>
public sealed class RoadmapCalendarCapacity : ICalendarCapacity, IDisposable
{
    private readonly IPlanningVelocitySettings _settings;

    public RoadmapCalendarCapacity(IPlanningVelocitySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _settings.Changed += OnSettingsChanged;
    }

    public event Action? Changed;

    public decimal HoursOn(DateOnly day) =>
        Math.Round((decimal)_settings.WorkingWeek.WorkedOn(day).TotalHours, 2, MidpointRounding.AwayFromZero);

    private void OnSettingsChanged() => Changed?.Invoke();

    public void Dispose() => _settings.Changed -= OnSettingsChanged;
}
