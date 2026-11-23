namespace Backlog.AzureFoundry.TestService;

/// <summary>
/// The stand-in for Foundry's transcription deployment that the Aspire run points the sync
/// service at (local ADR 0045 §5). It transcribes nothing: it checks the request has the
/// shape Foundry wants and answers a sentence naming what it was sent, so the phone's Cloud
/// engine can be walked end to end with no Azure account.
/// <para>
/// Two deployment names switch it to a failure, for the phone's two other paths:
/// <see cref="UnavailableDeployment"/> answers 503 and <see cref="FailingDeployment"/> 500.
/// </para>
/// </summary>
public static class LocalAzureFoundryTranscription
{
    /// <summary>The deployment name that answers 503.</summary>
    public const string UnavailableDeployment = "unavailable";

    /// <summary>The deployment name that answers 500.</summary>
    public const string FailingDeployment = "fail";

    /// <summary>Answers one transcription request.</summary>
    public static async Task<IResult> Answer(string deployment, HttpRequest request, CancellationToken cancellationToken)
    {
        if (string.Equals(deployment, UnavailableDeployment, StringComparison.OrdinalIgnoreCase))
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "ServiceUnavailable", "The local stand-in was asked to be unavailable.");
        }

        if (string.Equals(deployment, FailingDeployment, StringComparison.OrdinalIgnoreCase))
        {
            return Error(StatusCodes.Status500InternalServerError, "InternalServerError", "The local stand-in was asked to fail.");
        }

        if (!request.HasFormContentType)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", "The body has to be multipart/form-data.");
        }

        var form = await request.ReadFormAsync(cancellationToken);

        if (form.Files.GetFile("file") is not { } file)
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", "The multipart body has no 'file' part.");
        }

        // The real model refuses AAC in ADTS ("other formats return an error"), which is why
        // the desktop remuxes a meeting recording to M4A first (local ADR 0049 §3); the
        // stand-in refuses it too, so a recording sent unremuxed fails here as it would there.
        if (string.Equals(file.ContentType, "audio/aac", StringComparison.OrdinalIgnoreCase)
            || file.FileName.EndsWith(".aac", StringComparison.OrdinalIgnoreCase))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_value", "Unsupported file format: AAC in ADTS is not accepted.");
        }

        if (!string.Equals(form["response_format"], "json", StringComparison.Ordinal))
        {
            return Error(StatusCodes.Status400BadRequest, "invalid_request", "The stand-in answers only response_format=json.");
        }

        return Results.Ok(new { text = $"Local stand-in heard {file.Length} bytes of {file.ContentType}." });
    }

    private static IResult Error(int status, string code, string message) =>
        Results.Json(new { error = new { code, message } }, statusCode: status);
}
