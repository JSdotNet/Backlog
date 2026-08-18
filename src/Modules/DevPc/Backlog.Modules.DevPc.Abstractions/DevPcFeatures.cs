namespace Backlog.Modules.DevPc.Abstractions;

/// <summary>
/// The feature keys Dev PC Management owns.
/// <para>
/// Only the Shell gates on this today, because the tools pane is toggled from
/// the app chrome rather than from inside the pane. That makes it tempting to
/// file the key with the Shell's own — but the Shell is asking a question about
/// this context, and the day the pane wants to gate on it too the key would have
/// to move out of a place nothing below the Shell may read. It sits with the
/// port it is a feature of instead.
/// </para>
/// </summary>
public static class DevPcFeatures
{
    /// <summary>Check, update, enable, and disable the configured Copilot
    /// plugins, repository tools, and MCP servers this port reports.</summary>
    public const string SystemTools = "system-tools";
}
