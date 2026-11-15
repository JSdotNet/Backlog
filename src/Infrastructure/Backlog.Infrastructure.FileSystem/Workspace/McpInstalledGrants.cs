namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The grants the desktop app made in place of the shared MCP token on its first start after the
/// upgrade (local ADR 0052, Phase 1), by id: what <c>settings.json</c> records under
/// <c>mcpServer.grants</c>. A grant id is a label, not a password, so recording it there puts no
/// secret in the file.
/// </summary>
/// <param name="Delivery">"Backlog delivery": the delivery-flow scopes, for a repository's
/// <c>.mcp.json</c> registration and the user-scope one the Dev PC row installed.</param>
/// <param name="QuestionHook">"Question hook": <c>notify:ask</c>, for the <c>ask-backlog</c> hook.</param>
/// <param name="Telemetry">"Telemetry": <c>telemetry:write</c>, for Claude Code's OpenTelemetry
/// exporter.</param>
/// <param name="Manager">"Manager": <c>sessions:dispatch</c>, <c>tasks:read</c> and
/// <c>tasks:write</c>, for the dispatch-sessions Manager alone. Null in a row recorded before the
/// Manager grant existed, until the next start makes it.</param>
public sealed record McpInstalledGrants(string Delivery, string QuestionHook, string Telemetry, string? Manager = null);
