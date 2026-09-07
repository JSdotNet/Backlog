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
        StatusCodes.Status404NotFound => "There is no such thing here.",
        StatusCodes.Status409Conflict => "The request conflicts with the current state.",
        _ => "The sync service could not complete the request.",
    };
}
