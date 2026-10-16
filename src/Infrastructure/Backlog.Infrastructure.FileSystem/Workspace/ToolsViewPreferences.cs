using Backlog.Modules.DevPc.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Answers Dev PC Management's <see cref="IToolsViewPreferences"/> from the device's
/// <see cref="ShellNavigationStore"/>, the file that already remembers how the reader
/// last had the shell drawn. Per device and never synced, as the roadmap's view
/// choices are (local ADR 0019, §4).
/// <para>
/// A host that composed no such store still resolves the port: the choices are then
/// held for the life of this instance and forgotten on restart.
/// </para>
/// </summary>
public sealed class ToolsViewPreferences : IToolsViewPreferences
{
    private readonly ShellNavigationStore? _store;
    private string? _tab;
    private string? _hostFilter;

    public ToolsViewPreferences(ShellNavigationStore? store)
    {
        _store = store;
    }

    public string? Tab => _store is null ? _tab : _store.ToolsTab;

    public string? HostFilter => _store is null ? _hostFilter : _store.ToolsHostFilter;

    public void SetTab(string tab)
    {
        if (_store is null)
        {
            _tab = tab;
            return;
        }

        _store.SetToolsTab(tab);
    }

    public void SetHostFilter(string hostFilter)
    {
        if (_store is null)
        {
            _hostFilter = hostFilter;
            return;
        }

        _store.SetToolsHostFilter(hostFilter);
    }
}
