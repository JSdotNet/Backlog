namespace Backlog.Modules.DevPc.Abstractions;

/// <summary>
/// How this machine last had the system tools pane drawn: the tab it was on and the
/// host filter. Choices about the view, not the catalog, so they are kept per device
/// and never synced — the same footing as the roadmap's Hours switch (local ADR 0019,
/// §4), answered by the host over its per-device settings file.
/// <para>
/// A port on this module's own surface because a module may not reference
/// infrastructure (<c>ModuleBoundaryTests.A_module_never_references_infrastructure</c>).
/// The pane resolves it optionally: a host that composes none holds both choices for
/// the life of the pane and opens on the defaults next time.
/// </para>
/// </summary>
public interface IToolsViewPreferences
{
    /// <summary>The tab the pane was last on, by its id — <c>overview</c>, <c>plugins</c>,
    /// <c>marketplaces</c>, <c>mcp-servers</c>, <c>applications</c> or <c>checklist</c>.
    /// Null until one was chosen on this device.</summary>
    string? Tab { get; }

    /// <summary>The host filter the pane was last on — <c>all</c>, <c>claude</c> or
    /// <c>copilot</c>. Null until one was chosen on this device.</summary>
    string? HostFilter { get; }

    /// <summary>Remembers the tab on this device.</summary>
    void SetTab(string tab);

    /// <summary>Remembers the host filter on this device.</summary>
    void SetHostFilter(string hostFilter);
}
