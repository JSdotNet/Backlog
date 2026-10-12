using Backlog.Desktop.UI.Extensions;
using Backlog.Desktop.UI.Mcp;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Desktop.Mcp;

/// <summary>
/// Where this head receives Claude Code's telemetry: the MCP listener's configured port
/// on loopback, at <c>/v1/logs</c>, behind the MCP server's bearer token (local ADR
/// 0024).
/// <para>
/// Read off <see cref="IMcpEndpointSource"/> rather than the worker, for the reason that
/// port exists: the configured port is what a registration is written from, and it is
/// answerable whether or not the listener has bound it yet.
/// </para>
/// </summary>
public sealed class DesktopClaudeCodeTelemetryEndpoint(IMcpEndpointSource mcp) : IClaudeCodeTelemetryEndpoint
{
    private readonly IMcpEndpointSource _mcp = mcp ?? throw new ArgumentNullException(nameof(mcp));

    /// <inheritdoc />
    public Uri LogsEndpoint => ClaudeCodeLogsEndpoint.LogsEndpoint(BacklogMcpServerRegistration.EndpointUri(_mcp.Port));

    /// <inheritdoc />
    public string EnsureToken() => _mcp.EnsureToken();
}
