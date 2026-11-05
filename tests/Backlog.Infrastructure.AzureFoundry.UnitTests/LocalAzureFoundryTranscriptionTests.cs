using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backlog.Infrastructure.AzureFoundry.UnitTests;

/// <summary>
/// The transcription stand-in the Aspire run points the sync service at (local ADR 0045 §5).
/// It holds the adapter to the request shape Foundry wants — a <c>file</c> part and
/// <c>response_format=json</c> — and it can be told to be unavailable or to fail, by the
/// deployment name, so the phone's fallback and its failed turn can be shown locally.
/// </summary>
public sealed class LocalAzureFoundryTranscriptionTests : IDisposable
{
    private const string Route = "/openai/deployments/{0}/audio/transcriptions?api-version=2025-03-01-preview";

    private readonly WebApplicationFactory<Program> _standIn = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _standIn.Dispose();

    /// <summary>The real adapter against the stand-in: what the sync service sends, the
    /// stand-in accepts, and its answer names what it heard.</summary>
    [Fact]
    public async Task The_adapters_request_is_answered_with_text()
    {
        var client = new AzureFoundryTranscriptionClient(
            new HttpClient(_standIn.Server.CreateHandler()),
            Options.Create(new AzureFoundryTranscriptionOptions
            {
                Endpoint = new Uri("http://localhost/"),
                Credential = AzureFoundryTranscriptionCredential.None,
            }),
            new AzureFoundryTranscriptionTokens(null),
            NullLogger<AzureFoundryTranscriptionClient>.Instance);

        var result = await client.TranscribeAsync(
            new AzureFoundryTranscriptionRequest(new byte[42], "audio/webm", "en"),
            Cancellation);

        Assert.Equal(AzureFoundryTranscriptionResult.Success("Local stand-in heard 42 bytes of audio/webm."), result);
    }

    [Fact]
    public async Task A_body_without_a_file_part_is_a_400()
    {
        var body = new MultipartFormDataContent { { new StringContent("json"), "response_format" } };

        var response = await _standIn.CreateClient().PostAsync(string.Format(null, Route, "gpt-4o-transcribe"), body, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("text")]
    public async Task A_body_that_does_not_ask_for_json_is_a_400(string? format)
    {
        var body = new MultipartFormDataContent { { new ByteArrayContent(new byte[3]), "file", "dictation.m4a" } };
        if (format is not null) body.Add(new StringContent(format), "response_format");

        var response = await _standIn.CreateClient().PostAsync(string.Format(null, Route, "gpt-4o-transcribe"), body, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_body_that_is_not_multipart_is_a_400()
    {
        var response = await _standIn.CreateClient().PostAsync(
            string.Format(null, Route, "gpt-4o-transcribe"),
            new StringContent("{}", Encoding.UTF8, "application/json"),
            Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("unavailable", HttpStatusCode.ServiceUnavailable)]
    [InlineData("fail", HttpStatusCode.InternalServerError)]
    public async Task The_deployment_name_switches_the_stand_in_to_a_failure(string deployment, HttpStatusCode status)
    {
        var body = new MultipartFormDataContent
        {
            { new ByteArrayContent(new byte[3]), "file", "dictation.m4a" },
            { new StringContent("json"), "response_format" },
        };

        var response = await _standIn.CreateClient().PostAsync(string.Format(null, Route, deployment), body, Cancellation);

        Assert.Equal(status, response.StatusCode);
    }
}
