using System.Security.Claims;
using System.Text.Json;

using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Changes;

using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// Names the change set an MCP write runs under (local ADR 0061 §3): actor agent, the caller
/// the request's principal names (<see cref="McpScopePolicies.CallerOf"/>) — a grant by its
/// name and id, <c>unauthenticated</c>, or the host's own caller — the session its
/// <c>sessionId</c> argument names, and the correlation id of the audit record of the call that
/// opened it.
/// <para>
/// <b>Inside the audit filter</b>, so the correlation id it reads is the one that call's
/// record carries, and last, after every check, so a call a filter refuses opens no change
/// set. One the tool itself refuses has already opened or joined it. A
/// read-only tool runs with none, and so does every call in a host that composes no
/// <see cref="McpChangeSets"/>.
/// </para>
/// <para>
/// <b>Grouping is <see cref="McpChangeSets"/>'</b>: a write joins the change set of the
/// previous one from the same caller and session — or, with no session id, the same caller —
/// when it comes within thirty minutes of it. The first write names it: its tool and its
/// target, the entry's title when the target is an entry, e.g.
/// <c>transition on Ship the journal</c>.
/// </para>
/// </summary>
public static class McpChangeSetFilter
{
    /// <summary>The argument a write tool takes the caller's session id in.</summary>
    internal const string SessionIdArgument = "sessionId";

    /// <summary>Wraps <paramref name="next"/> so a write runs under its change set;
    /// <paramref name="caller"/> is who the call comes from when its principal names nobody,
    /// as <see cref="McpCallAudit.Filter"/> takes it.</summary>
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        string caller)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentException.ThrowIfNullOrWhiteSpace(caller);

        return async (request, cancellationToken) =>
        {
            if (request.MatchedPrimitive is not McpServerTool tool
                || !AgentCheckpointFilter.Writes(tool)
                || request.Services?.GetService<McpChangeSets>() is not { } changeSets)
            {
                return await next(request, cancellationToken).ConfigureAwait(false);
            }

            var changeSet = await ChangeSetForAsync(
                changeSets,
                CallerOf(request.User, caller),
                tool.ProtocolTool.Name,
                request.Params?.Arguments,
                request.Services,
                cancellationToken).ConfigureAwait(false);

            using (ChangeContext.Enter(changeSet))
            {
                return await next(request, cancellationToken).ConfigureAwait(false);
            }
        };
    }

    /// <summary>The change set a write by <paramref name="caller"/> to
    /// <paramref name="tool"/> with <paramref name="arguments"/> joins, or the one it opens.
    /// </summary>
    internal static async ValueTask<ChangeSet> ChangeSetForAsync(
        McpChangeSets changeSets,
        string caller,
        string tool,
        IDictionary<string, JsonElement>? arguments,
        IServiceProvider? services,
        CancellationToken cancellationToken)
    {
        var sessionId = SessionIdOf(arguments);

        // Named only when it may open one: the label costs an entry lookup. JoinOrOpen decides
        // under its lock, so a first write sent beside this one still shares its change set.
        var label = changeSets.Peek(caller, sessionId) is null
            ? await LabelAsync(tool, McpCallAudit.TargetId(arguments), services, cancellationToken).ConfigureAwait(false)
            : null;

        return changeSets.JoinOrOpen(
            caller,
            sessionId,
            McpCallAudit.CurrentCorrelationId ?? McpCallAudit.CorrelationId(),
            label);
    }

    /// <summary>Who a write comes from: the caller <paramref name="user"/> names — a grant by
    /// its name and id, or <c>unauthenticated</c> — else the host's <paramref name="fallback"/>.
    /// </summary>
    internal static string CallerOf(ClaimsPrincipal? user, string fallback) =>
        McpScopePolicies.CallerOf(user) ?? fallback;

    /// <summary>The session id a call names, or null when it names none.</summary>
    internal static string? SessionIdOf(IDictionary<string, JsonElement>? arguments) =>
        arguments is not null
        && arguments.TryGetValue(SessionIdArgument, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString() is { } text
        && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    /// <summary>
    /// <c>&lt;tool&gt; on &lt;target&gt;</c>: the entry's title when the target is an entry
    /// this backlog holds, else the target id as the call gave it; the tool alone when the
    /// call named no target. A title that cannot be read leaves the id: a label is never a
    /// reason for a write to fail.
    /// </summary>
    internal static async ValueTask<string> LabelAsync(
        string tool,
        string? target,
        IServiceProvider? services,
        CancellationToken cancellationToken)
    {
        if (target is null) return tool;

        if (Guid.TryParse(target, out var id) && services?.GetService<ITaskItems>() is { } entries)
        {
            try
            {
                if (await entries.GetAsync(id, cancellationToken).ConfigureAwait(false) is { } entry
                    && !string.IsNullOrWhiteSpace(entry.Title))
                {
                    return $"{tool} on {entry.Title.Trim()}";
                }
            }
#pragma warning disable CA1031 // The label is a courtesy; the write goes on with the id.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
            }
        }

        return $"{tool} on {target}";
    }
}
