using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Observability;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Api.Endpoints;

/// <summary>
/// Turns the two failures the replica raises as exceptions into the same
/// problem body every other failure gets.
/// <para>
/// They arrive as exceptions rather than results because they are not answers
/// to the question a handler asked: "the store is still starting" and "that
/// continuation is older than the feed still reaches" say nothing about the
/// owner's documents, and threading a failure union through the port to carry
/// them would put noise in four signatures for a condition no handler can act
/// on. Caught once here so the endpoints stay about their own use cases, and
/// caught here rather than by <c>UseExceptionHandler</c> because these are
/// expected conditions with codes a client branches on, not the unhandled 500
/// that middleware is for.
/// </para>
/// </summary>
internal sealed class ReplicaFaultFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (SyncReplicaException fault)
        {
            if (fault.Code is SyncErrorCodes.SyncCursorExpired)
            {
                // Counted beside the codec's own rejections, so one query
                // answers "how often is a cursor turned away, and why" without
                // having to know which layer noticed.
                SyncTelemetry.CursorRejected.Add(1, new KeyValuePair<string, object?>(
                    SyncTelemetry.ReasonTag, fault.Code));
            }

            return SyncResults.From(
                context.HttpContext,
                Result.Failure(new Error(fault.Code, fault.Message, ErrorTypeFor(fault.Code))),
                () => Results.Empty);
        }
    }

    /// <summary>An expired cursor is input the caller can fix by dropping it;
    /// an unavailable replica is not, and its status comes from the code rather
    /// than from the type — there is no <c>ErrorType</c> for "try again
    /// later" and adding one for a single case would be a shared-kernel change
    /// for a host's convenience.</summary>
    private static ErrorType ErrorTypeFor(string code) =>
        code is SyncErrorCodes.SyncCursorExpired ? ErrorType.Validation : ErrorType.Unexpected;
}
