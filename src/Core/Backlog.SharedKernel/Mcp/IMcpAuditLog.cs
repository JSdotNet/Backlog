namespace Backlog.SharedKernel.Mcp;

/// <summary>
/// The MCP audit log: one <see cref="McpAuditRecord"/> per tool call, kept on this
/// installation for <see cref="McpAudit.Retention"/>.
/// <para>
/// Both hosts write it from the same call filter, so the desktop app and the web harness
/// record calls the same way. An implementation raises <see cref="Changed"/> after each
/// write, from whichever thread wrote, so the Settings page follows.
/// </para>
/// </summary>
public interface IMcpAuditLog
{
    /// <summary>Raised after a record is written, on the writer's thread.</summary>
    event Action? Changed;

    /// <summary>Keeps one record, and drops every record older than
    /// <see cref="McpAudit.Retention"/>.</summary>
    Task AppendAsync(McpAuditRecord record, CancellationToken cancellationToken = default);

    /// <summary>At most <paramref name="count"/> records, newest first.</summary>
    Task<IReadOnlyList<McpAuditRecord>> LatestAsync(int count, CancellationToken cancellationToken = default);
}
