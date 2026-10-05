using Backlog.Modules.Roadmap.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// Answers Roadmap's <see cref="IRoadmapViewPreferences"/> from the device's
/// <see cref="ShellNavigationStore"/>, the file that already remembers how the reader
/// last had the shell drawn. Per device and never synced, as the Hours switch and the
/// folded bands are meant to be (local ADR 0019, §4).
/// <para>
/// A host that composed no such store — a test host, a harness drawing the roadmap alone
/// — still resolves the port: the choice is then held for the life of this instance and
/// forgotten on restart, with the switch on and no band folded until somebody says otherwise.
/// </para>
/// </summary>
public sealed class RoadmapViewPreferences : IRoadmapViewPreferences
{
    private readonly ShellNavigationStore? _store;
    private bool _hoursShown = true;
    private IReadOnlyCollection<string> _collapsedGroups = [];

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

    public IReadOnlyCollection<string> CollapsedGroups => _store?.RoadmapCollapsedGroups ?? _collapsedGroups;

    public void SetCollapsedGroups(IReadOnlyCollection<string> collapsed)
    {
        if (_store is null)
        {
            _collapsedGroups = [.. collapsed];
            return;
        }

        _store.SetRoadmapCollapsedGroups(collapsed);
    }
}
