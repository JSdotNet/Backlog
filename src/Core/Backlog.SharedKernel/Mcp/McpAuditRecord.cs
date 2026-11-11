namespace Backlog.SharedKernel.Mcp;

/// <summary>
/// One MCP tool call, as the audit log keeps it (local ADR 0052, Phase 0).
/// </summary>
/// <param name="At">When the call was answered, in UTC.</param>
/// <param name="Caller">Who made it. Today every registration sends the one shared
/// bearer token, so this is <see cref="McpAudit.SharedTokenCaller"/>; Phase 1 puts the
/// grant's name here.</param>
/// <param name="Tool">The tool name the call asked for, as sent.</param>
/// <param name="TargetId">The id the call was about — its <c>id</c> argument, else the
/// first argument named <c>…Id</c> that is not the caller's own — or null when it named
/// none.</param>
/// <param name="Outcome">How the call ended.</param>
/// <param name="CorrelationId">The request's trace id when one is running, else a fresh
/// id, so a record can be found again in the logs and traces of the same request.</param>
public sealed record McpAuditRecord(
    DateTimeOffset At,
    string Caller,
    string Tool,
    string? TargetId,
    McpCallOutcome Outcome,
    string CorrelationId);
