using System.Diagnostics;
using System.Text.Json;

using Backlog.SharedKernel.Mcp;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// The call-tool filter that writes one <see cref="McpAuditRecord"/> per
/// <c>tools/call</c> (local ADR 0052, Phase 0), installed by
/// <c>BacklogMcpServerRegistration</c> in both hosts.
/// <para>
/// <b>Outermost.</b> It is registered before the feature gates and the argument check, so
/// a call those refuse is recorded too. A refusal is an <see cref="McpException"/>, the
/// SDK's way of handing a reason to the client; the gates, the argument check and the
/// tools' own refusals (<c>RepositoryScope.Failure</c>) all throw one, and the SDK turns it
/// into the <c>isError</c> answer only outside this filter. So an
/// <see cref="McpException"/> is <see cref="McpCallOutcome.Refused"/>, and anything else
/// the call ends in — an answer already marked <c>isError</c>, which is how the SDK answers
/// any other exception a tool throws, or such an exception itself — is
/// <see cref="McpCallOutcome.Failed"/>.
/// </para>
/// <para>
/// <b>A write that fails never fails the call.</b> The log is written after the answer is
/// known and before it is returned, and a refused write goes to the host's log instead:
/// an audit file that is locked or full is not a reason to stop every session's tools.
/// </para>
/// </summary>
public static class McpCallAudit
{
    /// <summary>The longest target id a record keeps; anything longer is cut, so an
    /// argument a client filled with prose cannot grow the log without bound.</summary>
    internal const int MaxTargetLength = 200;

    /// <summary>Arguments that name the caller rather than what the call is about: a
    /// session passes its own id to <c>link_session</c>, <c>ask</c> and the delivery
    /// surface, and it is never the call's target.</summary>
    private static readonly HashSet<string> CallerIds = new(StringComparer.Ordinal) { "sessionId" };

    /// <summary>Wraps <paramref name="next"/> so its outcome is recorded against the
    /// caller the request's principal names (<see cref="McpScopePolicies.CallerOf"/>) — a
    /// grant by its name and id, a static token by its own name — or
    /// <paramref name="caller"/> when it names none. A host whose container has no
    /// <see cref="IMcpAuditLog"/> passes the call on unrecorded.</summary>
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        string caller)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentException.ThrowIfNullOrWhiteSpace(caller);

        return async (request, cancellationToken) =>
        {
            var log = request.Services?.GetService<IMcpAuditLog>();
            if (log is null) return await next(request, cancellationToken).ConfigureAwait(false);

            // Read before the call: the argument check may replace the arguments.
            var tool = request.Params?.Name ?? string.Empty;
            var target = TargetId(request.Params?.Arguments);
            var correlation = CorrelationId();
            var outcome = McpCallOutcome.Failed;

            try
            {
                var result = await next(request, cancellationToken).ConfigureAwait(false);
                outcome = result.IsError == true ? McpCallOutcome.Failed : McpCallOutcome.Succeeded;
                return result;
            }
            catch (McpException)
            {
                outcome = McpCallOutcome.Refused;
                throw;
            }
            finally
            {
                await WriteAsync(
                    log,
                    new McpAuditRecord(DateTimeOffset.UtcNow, McpScopePolicies.CallerOf(request.User) ?? caller, tool, target, outcome, correlation),
                    request.Services).ConfigureAwait(false);
            }
        };
    }

    /// <summary>
    /// The id a call is about: its <c>id</c> argument, else the first argument whose name
    /// ends in <c>Id</c>, as text. Null when it named none, or only an empty one.
    /// <para>
    /// The tools name their subject <c>id</c> where they have one (an entry, a goal, an
    /// impediment) and <c>runId</c>, <c>noteId</c> and the like where they do not, so the
    /// two rules cover them without a per-tool list to drift from the tools.
    /// </para>
    /// </summary>
    internal static string? TargetId(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null || arguments.Count == 0) return null;

        if (arguments.TryGetValue("id", out var id) && Text(id) is { } named) return named;

        foreach (var (name, value) in arguments)
        {
            if (name.Length > 2
                && name.EndsWith("Id", StringComparison.Ordinal)
                && !CallerIds.Contains(name)
                && Text(value) is { } text)
            {
                return text;
            }
        }

        return null;
    }

    /// <summary>The request's W3C trace id when a trace is running, so a record joins
    /// the request's logs and spans; else a fresh id that is at least unique.</summary>
    internal static string CorrelationId() =>
        Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity
            ? activity.TraceId.ToHexString()
            : Guid.NewGuid().ToString("N");

    private static string? Text(JsonElement value)
    {
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(text)) return null;

        text = text.Trim();
        return text.Length <= MaxTargetLength ? text : text[..MaxTargetLength];
    }

    private static async Task WriteAsync(IMcpAuditLog log, McpAuditRecord record, IServiceProvider? services)
    {
        try
        {
            // Not the call's token: a call its client gave up on still happened.
            await log.AppendAsync(record, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The audit write must never be the reason a call fails.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            services?.GetService<ILoggerFactory>()?
                .CreateLogger(typeof(McpCallAudit))
                .LogError(ex, "The MCP call to {Tool} could not be written to the audit log.", record.Tool);
        }
    }
}
