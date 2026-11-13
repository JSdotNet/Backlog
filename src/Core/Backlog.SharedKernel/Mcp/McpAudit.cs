namespace Backlog.SharedKernel.Mcp;

/// <summary>The MCP audit log's fixed values (local ADR 0052, Phase 0).</summary>
public static class McpAudit
{
    /// <summary>How long a record is kept.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    /// <summary>How many records the Settings page shows.</summary>
    public const int Shown = 200;

    /// <summary>The caller of a call the desktop app admits on the BACKLOG_MCP_TOKEN escape
    /// hatch, and before local ADR 0052, Phase 1 of every call: the shared bearer token.</summary>
    public const string SharedTokenCaller = "shared token";

    /// <summary>The caller of every call the web harness admits: it asks for no token,
    /// because it is not the installed app (local ADR 0052).</summary>
    public const string HarnessCaller = "web harness, no token";

    /// <summary>The caller of a record the listener writes for requests that carried no
    /// valid bearer token: nobody it could name.</summary>
    public const string NoValidTokenCaller = "no valid token";

    /// <summary>The tool column of the record written when failed bearer attempts trip
    /// the listener's lockout: not a tool, so not in a tool's snake case.</summary>
    public const string BearerLockout = "bearer lockout";

    /// <summary>The tool column of the record a granter agent's grant writes (local ADR 0052
    /// §5): the caller is the granter, the target the grant it made, both by name and id.</summary>
    public const string DelegatedGrant = "delegated grant";

    /// <summary>The tool column when a granter changes or renews a grant it made.</summary>
    public const string DelegatedScopes = "delegated scopes";

    /// <summary>The tool column when a granter revokes a grant it made.</summary>
    public const string DelegatedRevoke = "delegated revoke";
}
