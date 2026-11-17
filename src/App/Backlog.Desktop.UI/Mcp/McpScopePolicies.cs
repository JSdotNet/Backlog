using System.Security.Claims;

using Backlog.Infrastructure.Mcp;
using Backlog.SharedKernel.Mcp;

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// The named authorization policies of local ADR 0052 §4, one per scope, and the fallback-deny
/// that stands behind them — the sync service's pattern (<c>SyncAuthenticationExtensions</c>),
/// applied per tool and per route rather than per endpoint, because every tool is one
/// <c>/mcp</c> endpoint.
/// <para>
/// <b>Every caller is a principal.</b> The listener puts one on every request it admits: a
/// grant token's own, or <see cref="SharedTokenCaller"/> for the <c>BACKLOG_MCP_TOKEN</c> escape
/// hatch where a debug setting switched it on; the web harness, which asks for no token, puts
/// <see cref="HarnessCaller"/> on. A request with none is refused by every policy here.
/// </para>
/// <para>
/// <b>Fallback-deny.</b> A tool <see cref="McpToolScopes"/> does not name and a route
/// <see cref="RoutePolicy"/> does not name resolve to <see cref="Fallback"/>, which nobody
/// satisfies. The policies are named rather than set as <c>AuthorizationOptions.FallbackPolicy</c>
/// because the harness composes these into a Blazor app whose own pages must not inherit them.
/// </para>
/// </summary>
public static class McpScopePolicies
{
    /// <summary>What every policy name here starts with, so none collides with a host's own.</summary>
    public const string Prefix = "mcp:";

    /// <summary>The policy an unmapped tool or route resolves to: it admits nobody.</summary>
    public const string Fallback = Prefix + "fallback-deny";

    /// <summary>The policy of <c>/mcp</c> itself — <c>initialize</c>, <c>tools/list</c> and the
    /// protocol's housekeeping: any caller the listener admitted. A tool call then meets its
    /// scope's policy as well.</summary>
    public const string Caller = Prefix + "caller";

    /// <summary>The claim naming who a principal is for the audit log.</summary>
    public const string CallerClaim = "backlog_mcp_caller";

    /// <summary>What a static caller's identity is authenticated as.</summary>
    public const string StaticAuthenticationType = "mcp-static";

    /// <summary>The policy name for <paramref name="scope"/>: <see cref="Prefix"/> and the scope.</summary>
    public static string PolicyName(string scope) => Prefix + scope;

    /// <summary>The <c>BACKLOG_MCP_TOKEN</c> escape hatch (local ADR 0052, Phase 1): a dev or CI
    /// run's static token, accepted only where a debug setting switched it on. Every grantable
    /// scope, as the harness's caller has, since it stands in for no particular agent; audited
    /// as <see cref="McpAudit.SharedTokenCaller"/>, the name the retired shared token had.</summary>
    public static ClaimsPrincipal SharedTokenCaller { get; } = Static(McpAudit.SharedTokenCaller, McpScopes.Grantable);

    /// <summary>The web harness's caller, which presents nothing: every grantable scope, since
    /// the harness keeps no auth (ADR 0052) and its tools are QA's.</summary>
    public static ClaimsPrincipal HarnessCaller { get; } = Static(McpAudit.HarnessCaller, McpScopes.Grantable);

    /// <summary>
    /// Registers the authorization services and one policy per scope in
    /// <see cref="McpScopes.All"/> but <see cref="McpScopes.DevbookDecide"/>, plus
    /// <see cref="Caller"/> and <see cref="Fallback"/>. Idempotent.
    /// </summary>
    public static IServiceCollection AddMcpScopePolicies(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthorizationCore();

        // Singleton and stateless; TryAdd-free on purpose so a second call adds a second
        // handler, which answers the same.
        if (!services.Any(descriptor => descriptor.ImplementationType == typeof(ScopeHandler)))
        {
            services.AddSingleton<IAuthorizationHandler, ScopeHandler>();
        }

        services.Configure<AuthorizationOptions>(options =>
        {
            options.AddPolicy(Fallback, policy => policy.AddRequirements(new DenyRequirement()));
            options.AddPolicy(Caller, policy => policy.RequireAuthenticatedUser().RequireClaim(CallerClaim));

            foreach (var scope in McpScopes.Grantable)
            {
                options.AddPolicy(PolicyName(scope), policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim(CallerClaim)
                    .AddRequirements(new ScopeRequirement(scope)));
            }
        });

        return services;
    }

    /// <summary>The policy a call to <paramref name="toolName"/> must pass: <see cref="Caller"/>
    /// for a tool <see cref="McpToolScopes.AnyCaller"/> names, its scope's, or
    /// <see cref="Fallback"/> for a tool no scope serves.</summary>
    public static string ToolPolicy(string? toolName) =>
        toolName is not null && McpToolScopes.AnyCaller.Contains(toolName)
            ? Caller
            : McpToolScopes.ScopeOf(toolName) is { } scope ? PolicyName(scope) : Fallback;

    /// <summary>
    /// The policy a request to <paramref name="path"/> must pass on the listener: the OTLP logs
    /// route <see cref="McpScopes.TelemetryWrite"/>, the hook telemetry route
    /// <see cref="McpScopes.RunsWrite"/>, the wait route <see cref="McpScopes.SessionsDispatch"/>,
    /// the hook script <see cref="McpScopes.NotifyAsk"/>, <c>/mcp</c> <see cref="Caller"/>, and
    /// any other path <see cref="Fallback"/>. A path matches itself or its trailing-slash
    /// spelling, without regard to case, as ASP.NET routes it.
    /// </summary>
    public static string RoutePolicy(string? path)
    {
        var trimmed = (path ?? string.Empty).TrimEnd('/');

        bool Is(string route) => string.Equals(trimmed, route, StringComparison.OrdinalIgnoreCase);

        if (Is(McpLoopbackGuard.McpPath)) return Caller;
        if (Is(ClaudeCodeLogsEndpoint.RoutePath)) return PolicyName(McpScopes.TelemetryWrite);
        if (Is(BacklogTelemetryEndpoint.RoutePath)) return PolicyName(McpScopes.RunsWrite);
        if (Is(SessionRequestWaitEndpoint.RoutePath)) return PolicyName(McpScopes.SessionsDispatch);
        if (Is(AskBacklogHook.RoutePath)) return PolicyName(McpScopes.NotifyAsk);

        return Fallback;
    }

    /// <summary>Whether <paramref name="user"/> may call <paramref name="toolName"/>.</summary>
    public static async Task<bool> AuthorizeToolAsync(IAuthorizationService authorization, ClaimsPrincipal? user, string? toolName)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        var result = await authorization.AuthorizeAsync(user ?? Anonymous(), ToolPolicy(toolName)).ConfigureAwait(false);
        return result.Succeeded;
    }

    /// <summary>Whether <paramref name="user"/> may reach <paramref name="path"/>.</summary>
    public static async Task<bool> AuthorizeRouteAsync(IAuthorizationService authorization, ClaimsPrincipal? user, string? path)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        var result = await authorization.AuthorizeAsync(user ?? Anonymous(), RoutePolicy(path)).ConfigureAwait(false);
        return result.Succeeded;
    }

    /// <summary>Who <paramref name="user"/> is for the audit log: its <see cref="CallerClaim"/>,
    /// or null when it carries none.</summary>
    public static string? CallerOf(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true && user.FindFirst(CallerClaim)?.Value is { Length: > 0 } caller
            ? caller
            : null;

    /// <summary>How a grant names itself in the audit log: its name and its id.</summary>
    public static string GrantCaller(McpGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        return $"{grant.Name} ({grant.Id})";
    }

    /// <summary>The scopes <paramref name="user"/> holds: every space-separated value of its
    /// <see cref="McpGrantClaims.Scope"/> claims, as RFC 8693 writes them.</summary>
    public static IReadOnlySet<string> ScopesOf(ClaimsPrincipal? user) =>
        user is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : user.FindAll(McpGrantClaims.Scope)
                .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .ToHashSet(StringComparer.Ordinal);

    /// <summary>A principal for a caller that presented no grant: its name, and the scopes it
    /// is held to.</summary>
    public static ClaimsPrincipal Static(string caller, IEnumerable<string> scopes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caller);
        ArgumentNullException.ThrowIfNull(scopes);

        return new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(CallerClaim, caller),
                new Claim(McpGrantClaims.Scope, string.Join(' ', scopes.Where(McpScopes.IsGrantable)))
            ],
            StaticAuthenticationType));
    }

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    /// <summary>The requirement one scope's policy adds.</summary>
    internal sealed class ScopeRequirement(string scope) : IAuthorizationRequirement
    {
        public string Scope { get; } = scope;
    }

    /// <summary>The requirement nobody meets: <see cref="Fallback"/>'s.</summary>
    internal sealed class DenyRequirement : IAuthorizationRequirement;

    /// <summary>Meets a <see cref="ScopeRequirement"/> when the principal holds the scope, and
    /// never one for <see cref="McpScopes.DevbookDecide"/>, whatever a token claims.</summary>
    internal sealed class ScopeHandler : AuthorizationHandler<ScopeRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ScopeRequirement requirement)
        {
            if (McpScopes.IsGrantable(requirement.Scope) && ScopesOf(context.User).Contains(requirement.Scope))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
