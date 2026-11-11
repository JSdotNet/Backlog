namespace Backlog.SharedKernel.Mcp;

/// <summary>The MCP audit log's fixed values (local ADR 0052, Phase 0).</summary>
public static class McpAudit
{
    /// <summary>How long a record is kept.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    /// <summary>How many records the Settings page shows.</summary>
    public const int Shown = 200;

    /// <summary>The caller of every call the desktop app admits today: the one bearer
    /// token every registration carries (local ADR 0012 §1).</summary>
    public const string SharedTokenCaller = "shared token";

    /// <summary>The caller of every call the web harness admits: it asks for no token,
    /// because it is not the installed app (local ADR 0052).</summary>
    public const string HarnessCaller = "web harness, no token";
}
