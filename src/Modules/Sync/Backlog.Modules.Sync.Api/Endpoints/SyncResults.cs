using Backlog.Modules.Sync.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Api.Endpoints;

/// <summary>
/// The one place a <see cref="Result"/> becomes an HTTP response
/// (inherited ADR 0017). Endpoints say what success looks like; what a failure
/// looks like is decided here, so two endpoints cannot disagree about which
/// status a conflict is.
/// </summary>
internal static class SyncResults
{
    /// <summary>The base of the <c>type</c> URI in a problem body. It names the
    /// error rather than resolving to a page today; a client branches on the
    /// code at the end of it.</summary>
    internal const string ProblemTypeBase = "https://backlog.jsdotnet.dev/problems/";

    /// <summary>Renders a result: <paramref name="onSuccess"/> when it
    /// succeeded, a problem body when it did not.</summary>
    internal static IResult From<TValue>(HttpContext context, Result<TValue> result, Func<TValue, IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return result.IsSuccess
            ? onSuccess(result.Value)
            : Problem(context, StatusFor(result.Error), result.Error.Code, result.Error.Message);
    }

    /// <summary>The same, for a use case that has nothing to hand back when it
    /// succeeds — the acknowledgement, which answers 204. Separate overload
    /// rather than a <c>Result&lt;Unit&gt;</c>: inventing a value so one method
    /// could serve both would put a name for "nothing" in every signature that
    /// returns nothing.</summary>
    internal static IResult From(HttpContext context, Result result, Func<IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return result.IsSuccess
            ? onSuccess()
            : Problem(context, StatusFor(result.Error), result.Error.Code, result.Error.Message);
    }

    /// <summary>A problem body in the shape every failure from this service
    /// uses. <c>traceId</c> is added by the customization in
    /// <c>Program</c>, so it is on framework-generated problems too.</summary>
    internal static IResult Problem(HttpContext context, int status, string code, string detail)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Results.Problem(
            type: ProblemTypeBase + code,
            title: TitleFor(status),
            statusCode: status,
            detail: detail,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = code });
    }

    private static int StatusFor(Error error) => error.Code switch
    {
        // A credential that does not match is a failed authentication however
        // the handler classified it, and 401 is what tells a client to go and
        // get a new token rather than to change its request.
        SyncErrorCodes.DeviceCredentialInvalid => StatusCodes.Status401Unauthorized,

        // 403 and not 404, which is the opposite of how this service treats
        // every other "that is not yours". The difference is what the caller
        // had to hold: an inbox id is a guess, and answering 404 keeps a guess
        // from confirming anything, whereas a correctly-signed cursor for
        // another owner's feed can only have been obtained, and pretending it
        // does not exist would hide the one event .arc42/adr/0005 asks to be
        // loud about.
        SyncErrorCodes.SyncCursorNotYours => StatusCodes.Status403Forbidden,

        // The replica is not there yet — locally, the Cosmos emulator still
        // starting. Come back; there is nothing to change about the request.
        SyncErrorCodes.ReplicaUnavailable => StatusCodes.Status503ServiceUnavailable,

        // The store is up and this one document is the problem, so it is the
        // caller's to fix rather than to retry. 413 says which of the two.
        SyncErrorCodes.TaskTooLarge => StatusCodes.Status413PayloadTooLarge,

        // The same answer for the other container. Separate codes because a
        // client told only "too large" would not know which of its two pushes
        // to make smaller.
        SyncErrorCodes.SessionTooLarge => StatusCodes.Status413PayloadTooLarge,

        // More than the store will take from this caller right now. Same shape
        // as the 503 above and a different sentence: the store is answering.
        SyncErrorCodes.ReplicaBusy => StatusCodes.Status429TooManyRequests,

        // A batch larger than this service will accept. Clamped rather than
        // trimmed, because silently dropping the tail of a push would leave the
        // device believing tasks were stored that were not.
        SyncErrorCodes.PushBatchTooLarge => StatusCodes.Status400BadRequest,

        // The same rule over the other container, and the same refusal: a batch
        // trimmed to the cap would leave the machine believing records were
        // appended that were not.
        SyncErrorCodes.SessionBatchTooLarge => StatusCodes.Status400BadRequest,
        _ => error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        },
    };

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "The request was not acceptable.",
        StatusCodes.Status401Unauthorized => "This device is not authenticated.",
        StatusCodes.Status403Forbidden => "This device may not do that.",
        StatusCodes.Status404NotFound => "There is no such thing here.",
        StatusCodes.Status409Conflict => "The request conflicts with the current state.",
        StatusCodes.Status413PayloadTooLarge => "That is more than the sync service will store.",
        StatusCodes.Status429TooManyRequests => "The sync service is busy.",
        StatusCodes.Status503ServiceUnavailable => "The sync service is not ready yet.",
        _ => "The sync service could not complete the request.",
    };
}
