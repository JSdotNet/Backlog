using Backlog.Infrastructure.FileSystem;

using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// Takes a checkpoint (<see cref="AgentCheckpoints"/>) before a write tool runs — one whose
/// <c>ReadOnly</c> annotation is not <see langword="true"/> — so an agent's mistake can be put
/// back by hand. <see cref="AgentCheckpoints.TakeIfDue"/> keeps it to one every thirty minutes,
/// and a checkpoint that fails is logged there and never stops the call.
/// <para>
/// Installed last, after the scope check and Windows Hello, so only a call that is about to run
/// costs a copy: a refused call changes nothing and needs no checkpoint. A host that composes no
/// <see cref="AgentCheckpoints"/> passes every call through.
/// </para>
/// </summary>
public static class AgentCheckpointFilter
{
    /// <summary>Whether a call to <paramref name="tool"/> may write: any tool not annotated
    /// read-only, a missing annotation included.</summary>
    public static bool Writes(McpServerTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return tool.ProtocolTool.Annotations?.ReadOnlyHint is not true;
    }

    /// <summary>The call filter.</summary>
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) => (request, cancellationToken) =>
        {
            try
            {
                if (request.MatchedPrimitive is McpServerTool tool
                    && Writes(tool)
                    && request.Services?.GetService<AgentCheckpoints>() is { } checkpoints)
                {
                    checkpoints.TakeIfDue();
                }
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
            {
                // TakeIfDue logs its own failures and never throws; this is the request scope
                // failing to answer. Either way the write goes on without a checkpoint.
            }

            return next(request, cancellationToken);
        };
}
