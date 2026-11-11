using Backlog.Desktop.UI.Extensions;
using Backlog.Desktop.UI.Mcp;
using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Desktop.Mcp;

/// <summary>
/// Where this head receives Claude Code's telemetry: the MCP listener's configured port
/// on loopback, at <c>/v1/logs</c>, behind the telemetry token of its own — never the MCP
/// server's, which that endpoint no longer takes (local ADR 0024).
/// <para>
/// Read off <see cref="IMcpEndpointSource"/> rather than the worker, for the reason that
/// port exists: the configured port is what a registration is written from, and it is
/// answerable whether or not the listener has bound it yet.
/// </para>
/// </summary>
public sealed class DesktopClaudeCodeTelemetryEndpoint(IMcpEndpointSource mcp, WorkspaceSettingsStore settings) : IClaudeCodeTelemetryEndpoint
{
    private readonly IMcpEndpointSource _mcp = mcp ?? throw new ArgumentNullException(nameof(mcp));
    private readonly WorkspaceSettingsStore _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    /// <inheritdoc />
    public Uri LogsEndpoint => ClaudeCodeLogsEndpoint.LogsEndpoint(BacklogMcpServerRegistration.EndpointUri(_mcp.Port));

    /// <inheritdoc />
    public string EnsureToken() => _settings.EnsureTelemetryToken();
}
