namespace Backlog.UI.Components.Settings;

/// <summary>
/// One scope an agent can be given, as <see cref="AgentScopeSwitches"/> draws it: its name, the
/// one line saying what it lets the agent do, and whether it is locked.
/// <para>
/// The host owns the list and its order. The library knows no scope by name, because which
/// scopes exist and what each one opens is the MCP server's to say (local ADR 0052 §4).
/// </para>
/// </summary>
/// <param name="Name">The scope as a grant stores it, such as <c>tasks:read</c>.</param>
/// <param name="Description">One line on what it lets the agent do.</param>
/// <param name="Locked">Drawn disabled, as the grant holds it, so a boundary the person cannot
/// move here is visible rather than missing — <c>devbook:decide</c>, which no grant holds.</param>
public sealed record AgentScopeOption(string Name, string Description, bool Locked = false);

/// <summary>One switch flipped in <see cref="AgentScopeSwitches"/>: which scope, and whether
/// it is on now.</summary>
public readonly record struct AgentScopeChange(string Scope, bool On);
