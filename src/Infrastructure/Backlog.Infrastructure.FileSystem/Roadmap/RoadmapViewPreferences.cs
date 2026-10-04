using Backlog.Modules.Roadmap.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// Answers Roadmap's <see cref="IRoadmapViewPreferences"/> from the device's
/// <see cref="ShellNavigationStore"/>, the file that already remembers how the reader
/// last had the shell drawn. Per device and never synced, as the Hours switch is meant
/// to be (local ADR 0019, §4).
/// <para>
/// A host that composed no such store — a test host, a harness drawing the roadmap alone
/// — still resolves the port: the choice is then held for the life of this instance and
/// forgotten on restart, with the switch on until somebody turns it off.
/// </para>
/// </summary>
public sealed class RoadmapViewPreferences : IRoadmapViewPreferences
{
    private readonly ShellNavigationStore? _store;
    private bool _hoursShown = true;

    public RoadmapViewPreferences(ShellNavigationStore? store)
    {
        _store = store;
    }

    public bool HoursShown => _store?.RoadmapHoursShown ?? _hoursShown;

    public void SetHoursShown(bool shown)
    {
        if (_store is null)
        {
            _hoursShown = shown;
            return;
        }

        _store.SetRoadmapHoursShown(shown);
    }
}
