using System.Security.Claims;

using Backlog.Infrastructure.Mcp;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Mcp;

using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// Windows Hello before a <see cref="McpScopes.TasksAdmin"/> tool runs (local ADR 0058 §6):
/// deleting a goal, and any bulk change that joins that scope. The call waits while the desktop
/// asks the person through <see cref="IUserConsent"/>, runs only when they confirm, and is
/// refused when they cancel or nothing here can ask.
/// <para>
/// <b>Per scope, not per tool.</b> The step-up reads <see cref="McpToolScopes"/>, so a tool
/// mapped to <c>tasks:admin</c> later asks without this file being touched, and a tool under
/// any other scope — <c>transition</c> to Done included — never asks.
/// </para>
/// <para>
/// <b>The web harness asks for nothing</b> (§9): it keeps no grant, broker or step-up, so its
/// own caller passes. A grant's caller, or the debug escape hatch's, is asked on every call,
/// never answered from a confirmation the desktop remembers
/// (<see cref="IUserConsent.VerifyEveryTimeAsync"/>): the person did not start the call.
/// </para>
/// </summary>
public static class McpStepUp
{
    /// <summary>The refusal when the person cancelled the prompt, or it failed.</summary>
    public const string DeclinedMessage =
        "The tool '{0}' needs Windows Hello on the desktop, and it was cancelled, so nothing changed.";

    /// <summary>The refusal when nothing here can ask: no Windows Hello set up, or a host with none.</summary>
    public const string UnavailableMessage =
        "The tool '{0}' needs Windows Hello on the desktop, and it is not available here, so nothing changed.";

    /// <summary>Whether a call to <paramref name="toolName"/> by <paramref name="user"/> asks for
    /// Windows Hello: a <c>tasks:admin</c> tool, from any caller but the web harness's.</summary>
    public static bool Requires(string? toolName, ClaimsPrincipal? user) =>
        McpToolScopes.ScopeOf(toolName) == McpScopes.TasksAdmin
        && McpScopePolicies.CallerOf(user) != McpAudit.HarnessCaller;

    /// <summary>What the Windows Hello prompt says: who asks, and for which tool.</summary>
    public static string PromptFor(string toolName, ClaimsPrincipal? user) =>
        $"{McpScopePolicies.CallerOf(user) ?? "An agent"} wants to run {toolName} in Backlog, which is hard to undo.";

    /// <summary>
    /// The call filter: asks <see cref="IUserConsent"/> before a call <see cref="Requires"/>
    /// names reaches its tool, and throws the refusal the audit records when it is not
    /// confirmed. A request scope with no consent composed answers as not available.
    /// </summary>
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) => async (request, cancellationToken) =>
        {
            var name = request.Params?.Name;

            if (name is not null && Requires(name, request.User))
            {
                var consent = request.Services?.GetService<IUserConsent>();
                var outcome = consent is null
                    ? UserConsentOutcome.NotAvailable
                    : await consent.VerifyEveryTimeAsync(PromptFor(name, request.User), cancellationToken).ConfigureAwait(false);

                if (outcome != UserConsentOutcome.Verified)
                {
                    throw new McpException(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        outcome == UserConsentOutcome.NotAvailable ? UnavailableMessage : DeclinedMessage,
                        name));
                }
            }

            return await next(request, cancellationToken).ConfigureAwait(false);
        };
}
