namespace Backlog.SharedKernel.Mcp;

/// <summary>How one MCP tool call ended.</summary>
public enum McpCallOutcome
{
    /// <summary>The tool ran and answered without an error.</summary>
    Succeeded,

    /// <summary>The call ended without a reason given: an answer marked as an error, an
    /// exception other than a refusal, or the client cancelling the call.</summary>
    Failed,

    /// <summary>The server refused the call and said why: the tool is unknown or
    /// switched off, an argument was refused, or the tool itself refused it (an unknown
    /// repository, an archived one). Phase 1's scope refusals land here too.</summary>
    Refused
}
