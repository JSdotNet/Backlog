using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// How every client in this project turns an HTTP answer into a
/// <see cref="Result{TValue}"/>.
/// <para>
/// Here rather than in each client because the convention is the thing that has
/// to be identical: an expected 4xx becomes a failed result carrying the
/// ProblemDetails <c>code</c> and <c>detail</c>, and anything that never reached
/// the service becomes <see cref="DevicePairingClient.UnreachableCode"/>. A
/// second copy would go on working while it drifted, and the drift would show up
/// as one screen branching on a code another screen never produces.
/// </para>
/// </summary>
internal static class SyncHttp
{
    /// <summary>
    /// Sends, and reads the body as <typeparamref name="T"/> on success or as a
    /// problem document on anything else.
    /// <para>
    /// Nothing here throws for an answer the service meant to give. A caller
    /// branches on <see cref="Error.Code"/>, and an exception would make the
    /// ordinary outcomes — an expired pairing code, a cursor the store no longer
    /// resumes from — the one shape a screen cannot handle without a catch.
    /// </para>
    /// </summary>
    public static async Task<Result<T>> SendAsync<T>(
        Func<Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await send().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<T>(await ReadProblemAsync(response, cancellationToken).ConfigureAwait(false));
            }

            var value = await response.Content
                .ReadFromJsonAsync<T>(cancellationToken)
                .ConfigureAwait(false);

            return value is null
                ? Result.Failure<T>(Error.Unexpected(DevicePairingClient.UnreachableCode, "The sync service answered with nothing."))
                : Result.Success(value);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException)
        {
            return Result.Failure<T>(Error.Unexpected(
                DevicePairingClient.UnreachableCode,
                $"The sync service could not be reached: {ex.Message}"));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<T>(Error.Unexpected(
                DevicePairingClient.UnreachableCode,
                "The sync service did not answer in time."));
        }
    }

    /// <summary>
    /// The ProblemDetails body, read as the <c>code</c> extension the sync API
    /// puts on every error plus its human-readable <c>detail</c>. Read as a
    /// document rather than into a typed record because <c>code</c> is an
    /// extension member, and a body that is not problem+json at all — a proxy's
    /// HTML error page, say — still has to produce something a screen can show.
    /// </summary>
    private static async Task<Error> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string? code = null;
        string? detail = null;

        try
        {
            using var document = await JsonDocument
                .ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (document.RootElement.TryGetProperty("code", out var codeElement))
                {
                    code = codeElement.GetString();
                }

                if (document.RootElement.TryGetProperty("detail", out var detailElement))
                {
                    detail = detailElement.GetString();
                }
                else if (document.RootElement.TryGetProperty("title", out var titleElement))
                {
                    detail = titleElement.GetString();
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or IOException)
        {
        }

        return new Error(
            string.IsNullOrWhiteSpace(code) ? $"sync.http_{(int)response.StatusCode}" : code,
            string.IsNullOrWhiteSpace(detail)
                ? $"The sync service answered {(int)response.StatusCode} {response.ReasonPhrase}."
                : detail,
            Classify(response.StatusCode));
    }

    private static ErrorType Classify(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest => ErrorType.Validation,
        HttpStatusCode.NotFound => ErrorType.NotFound,
        HttpStatusCode.Conflict => ErrorType.Conflict,
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ErrorType.Failure,
        _ => ErrorType.Unexpected
    };
}
