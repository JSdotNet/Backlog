using System.Net;
using System.Text;
using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backlog.Infrastructure.AzureFoundry.UnitTests;

/// <summary>
/// The sync service's call to Foundry's transcription deployment (local ADR 0045 §3): the
/// deployment-style route, a multipart body with an ASCII file name, an Entra token or no
/// credential at all, one retry on a 429 or a 5xx inside a 50-second budget, and a failure
/// logged by its status and Foundry's code — never by Foundry's message, which may quote the
/// input.
/// </summary>
public sealed class AzureFoundryTranscriptionClientTests
{
    private static readonly Uri Endpoint = new("https://backlog-foundry.cognitiveservices.azure.com/");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_recording_goes_to_the_deployments_transcription_route_with_the_api_version()
    {
        var foundry = new Foundry(Text("hello"));
        var client = Client(foundry, options => options.Deployment = "gpt-transcribe");

        var result = await client.TranscribeAsync(Request("audio/mp4"), Cancellation);

        Assert.Equal(AzureFoundryTranscriptionResult.Success("hello"), result);
        var request = Assert.Single(foundry.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://backlog-foundry.cognitiveservices.azure.com/openai/deployments/gpt-transcribe/audio/transcriptions?api-version=2025-03-01-preview",
            request.Uri.ToString());
    }

    [Theory]
    [InlineData("audio/mp4", "dictation.m4a")]
    [InlineData("audio/x-m4a", "dictation.m4a")]
    [InlineData("audio/webm", "dictation.webm")]
    [InlineData("audio/wav", "dictation.wav")]
    [InlineData("audio/mpeg", "dictation.mp3")]
    [InlineData("AUDIO/WEBM", "dictation.webm")]
    public async Task The_file_part_carries_an_ascii_name_derived_from_the_type(string contentType, string fileName)
    {
        var foundry = new Foundry(Text("x"));
        var audio = Encoding.ASCII.GetBytes("RIFF fake audio");

        await Client(foundry).TranscribeAsync(new AzureFoundryTranscriptionRequest(audio, contentType, null), Cancellation);

        var file = Assert.Single(foundry.Requests).Parts["file"];
        Assert.Equal(fileName, file.FileName);
        Assert.Equal(audio, file.Bytes);
    }

    [Fact]
    public async Task The_body_asks_for_json_and_passes_the_language_only_when_given()
    {
        var foundry = new Foundry(_ => Text("x"));
        var client = Client(foundry);

        await client.TranscribeAsync(Request("audio/webm", language: "nl"), Cancellation);
        await client.TranscribeAsync(Request("audio/webm"), Cancellation);

        var withHint = foundry.Requests[0].Parts;
        Assert.Equal("json", withHint["response_format"].Text);
        Assert.Equal("nl", withHint["language"].Text);

        var withoutHint = foundry.Requests[1].Parts;
        Assert.Equal("json", withoutHint["response_format"].Text);
        Assert.False(withoutHint.ContainsKey("language"));
    }

    [Fact]
    public async Task With_a_credential_the_call_carries_an_entra_token_for_cognitive_services()
    {
        var foundry = new Foundry(Text("x"));
        var credential = new RecordingCredential("entra-token");
        var client = Client(foundry, credential: credential);

        await client.TranscribeAsync(Request("audio/mp4"), Cancellation);

        Assert.Equal("Bearer entra-token", Assert.Single(foundry.Requests).Authorization);
        Assert.Equal(["https://cognitiveservices.azure.com/.default"], Assert.Single(credential.Scopes));
    }

    /// <summary>The local stand-in takes no credential, and there is no key to send.</summary>
    [Fact]
    public async Task Without_a_credential_the_call_carries_no_authorization()
    {
        var foundry = new Foundry(Text("x"));

        await Client(foundry).TranscribeAsync(Request("audio/mp4"), Cancellation);

        var request = Assert.Single(foundry.Requests);
        Assert.Null(request.Authorization);
        Assert.False(request.HasApiKey);
    }

    [Fact]
    public async Task A_refusal_is_a_failure_logged_by_status_and_foundry_code_never_its_message()
    {
        var foundry = new Foundry(_ => Json(
            HttpStatusCode.BadRequest,
            """{"error":{"code":"invalid_audio","message":"I heard: the launch code is 0000"}}"""));
        var logger = new ListLogger<AzureFoundryTranscriptionClient>();

        var result = await Client(foundry, logger: logger).TranscribeAsync(Request("audio/mp4"), Cancellation);

        Assert.Equal(AzureFoundryTranscriptionResult.Failure, result);
        var logged = string.Join('\n', logger.Messages);
        Assert.Contains("400", logged, StringComparison.Ordinal);
        Assert.Contains("invalid_audio", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("launch code", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("I heard", logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_transcript_is_never_logged()
    {
        var logger = new ListLogger<AzureFoundryTranscriptionClient>();

        await Client(new Foundry(Text("my private thought")), logger: logger).TranscribeAsync(Request("audio/mp4"), Cancellation);

        Assert.DoesNotContain(logger.Messages, message => message.Contains("private thought", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_answer_without_text_is_a_failure()
    {
        var result = await Client(new Foundry(_ => Json(HttpStatusCode.OK, "{}"))).TranscribeAsync(Request("audio/mp4"), Cancellation);

        Assert.Equal(AzureFoundryTranscriptionResult.Failure, result);
    }

    [Fact]
    public async Task An_unreachable_foundry_is_a_failure_rather_than_a_throw()
    {
        var foundry = new Foundry((HttpRequestMessage _) => throw new HttpRequestException("connection refused"));

        var result = await Client(foundry).TranscribeAsync(Request("audio/mp4"), Cancellation);

        Assert.Equal(AzureFoundryTranscriptionResult.Failure, result);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task A_429_or_5xx_is_retried_once_and_the_second_answer_counts(HttpStatusCode first)
    {
        var answers = new Queue<HttpResponseMessage>([Json(first, "{}"), Text("second time")]);
        var foundry = new Foundry(_ => answers.Dequeue());
        using var provider = Registered(foundry);

        var result = await provider.GetRequiredService<IAzureFoundryTranscriptionClient>().TranscribeAsync(Request("audio/mp4"), Cancellation);

        Assert.Equal(AzureFoundryTranscriptionResult.Success("second time"), result);
        Assert.Equal(2, foundry.Requests.Count);

        // Each attempt is serialised afresh by the stub, never buffered, so a body that could
        // be read only once would arrive empty the second time and fail here.
        var sent = Request("audio/mp4").Audio.ToArray();
        Assert.Equal(sent, foundry.Requests[0].Parts["file"].Bytes);
        Assert.Equal(sent, foundry.Requests[1].Parts["file"].Bytes);
    }

    [Fact]
    public async Task A_second_5xx_is_a_failure_after_exactly_one_retry()
    {
        var foundry = new Foundry(_ => Json(HttpStatusCode.InternalServerError, "{}"));
        using var provider = Registered(foundry);

        var result = await provider.GetRequiredService<IAzureFoundryTranscriptionClient>().TranscribeAsync(Request("audio/mp4"), Cancellation);

        Assert.Equal(AzureFoundryTranscriptionResult.Failure, result);
        Assert.Equal(2, foundry.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task Any_other_refusal_is_not_retried(HttpStatusCode status)
    {
        var foundry = new Foundry(_ => Json(status, "{}"));
        using var provider = Registered(foundry);

        var result = await provider.GetRequiredService<IAzureFoundryTranscriptionClient>().TranscribeAsync(Request("audio/mp4"), Cancellation);

        Assert.Equal(AzureFoundryTranscriptionResult.Failure, result);
        Assert.Single(foundry.Requests);
    }

    /// <summary>The whole call, retry included, is bounded — shrunk here from 50 seconds so
    /// the test does not wait for it.</summary>
    [Fact]
    public async Task A_foundry_that_does_not_answer_within_the_budget_is_a_failure()
    {
        var foundry = new Foundry(async cancellationToken =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return Text("too late");
        });
        using var provider = Registered(foundry, options => options.TotalBudget = TimeSpan.FromMilliseconds(300));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await provider.GetRequiredService<IAzureFoundryTranscriptionClient>().TranscribeAsync(Request("audio/mp4"), Cancellation);

        Assert.Equal(AzureFoundryTranscriptionResult.Failure, result);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Took {watch.Elapsed}.");
    }

    /// <summary>The token is part of the call: an identity endpoint that is slow to answer
    /// spends the same budget, so the route still answers before the phone gives up.</summary>
    [Fact]
    public async Task A_token_that_does_not_arrive_within_the_budget_is_a_failure()
    {
        var foundry = new Foundry(Text("never asked"));
        var client = Client(
            foundry,
            options => options.TotalBudget = TimeSpan.FromMilliseconds(300),
            credential: new SlowCredential(TimeSpan.FromSeconds(30)));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await client.TranscribeAsync(Request("audio/mp4"), Cancellation);

        Assert.Equal(AzureFoundryTranscriptionResult.Failure, result);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Took {watch.Elapsed}.");
        Assert.Empty(foundry.Requests);
    }

    [Fact]
    public void The_budget_and_the_retry_default_to_the_adr()
    {
        var options = new AzureFoundryTranscriptionOptions();

        Assert.Equal(TimeSpan.FromSeconds(50), options.TotalBudget);
        Assert.Equal("gpt-4o-transcribe", options.Deployment);
        Assert.Equal("2025-03-01-preview", options.ApiVersion);
        Assert.Equal(AzureFoundryTranscriptionCredential.ManagedIdentity, options.Credential);
    }

    /// <summary>The sync host calls <c>AddServiceDefaults()</c>, whose standard pipeline would
    /// cut an upload off at ten seconds and send it three more times. This client takes it
    /// off and carries its own.</summary>
    [Fact]
    public void The_client_carries_its_own_pipeline_instead_of_the_hosts_standard_one()
    {
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
        services.AddAzureFoundryTranscriptionClient(options =>
        {
            options.Endpoint = Endpoint;
            options.Credential = AzureFoundryTranscriptionCredential.None;
        });

        using var provider = services.BuildServiceProvider();
        var chain = HandlerChain(provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(nameof(IAzureFoundryTranscriptionClient)));

        var resilience = Assert.Single(chain.OfType<ResilienceHandler>());
        Assert.IsType<AzureFoundryTranscriptionClient>(provider.GetRequiredService<IAzureFoundryTranscriptionClient>());
        Assert.NotNull(resilience);
    }

    [Fact]
    public void Managed_identity_registers_a_credential_and_none_registers_none()
    {
        using var managed = Provider(AzureFoundryTranscriptionCredential.ManagedIdentity);
        using var none = Provider(AzureFoundryTranscriptionCredential.None);

        Assert.NotNull(managed.GetRequiredService<AzureFoundryTranscriptionTokens>().Credential);
        Assert.Null(none.GetRequiredService<AzureFoundryTranscriptionTokens>().Credential);

        static ServiceProvider Provider(AzureFoundryTranscriptionCredential credential)
        {
            var services = new ServiceCollection();
            services.AddAzureFoundryTranscriptionClient(options =>
            {
                options.Endpoint = Endpoint;
                options.Credential = credential;
                options.ManagedIdentityClientId = "00000000-0000-0000-0000-000000000001";
            });
            return services.BuildServiceProvider();
        }
    }

    private static AzureFoundryTranscriptionClient Client(
        Foundry foundry,
        Action<AzureFoundryTranscriptionOptions>? configure = null,
        TokenCredential? credential = null,
        ILogger<AzureFoundryTranscriptionClient>? logger = null)
    {
        var options = new AzureFoundryTranscriptionOptions { Endpoint = Endpoint };
        configure?.Invoke(options);

        return new AzureFoundryTranscriptionClient(
            new HttpClient(foundry),
            Options.Create(options),
            new AzureFoundryTranscriptionTokens(credential),
            logger ?? NullLogger<AzureFoundryTranscriptionClient>.Instance);
    }

    private static ServiceProvider Registered(Foundry foundry, Action<AzureFoundryTranscriptionOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddAzureFoundryTranscriptionClient(options =>
            {
                options.Endpoint = Endpoint;
                options.Credential = AzureFoundryTranscriptionCredential.None;
                options.RetryDelay = TimeSpan.FromMilliseconds(10);
                configure?.Invoke(options);
            })
            .ConfigurePrimaryHttpMessageHandler(() => foundry);

        return services.BuildServiceProvider();
    }

    private static AzureFoundryTranscriptionRequest Request(string contentType, string? language = null) =>
        new(Encoding.ASCII.GetBytes("fake audio bytes"), contentType, language);

    private static HttpResponseMessage Text(string text) =>
        Json(HttpStatusCode.OK, $$"""{"text":"{{text}}"}""");

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static List<HttpMessageHandler> HandlerChain(HttpMessageHandler handler)
    {
        var chain = new List<HttpMessageHandler>();
        for (var current = handler; current is not null; current = (current as DelegatingHandler)?.InnerHandler)
        {
            chain.Add(current);
        }

        return chain;
    }

    /// <summary>One request as Foundry received it, its multipart body read apart.</summary>
    internal sealed record SeenRequest(
        HttpMethod Method,
        Uri Uri,
        string? Authorization,
        bool HasApiKey,
        IReadOnlyDictionary<string, SeenPart> Parts);

    internal sealed record SeenPart(string? FileName, byte[] Bytes)
    {
        public string Text => Encoding.UTF8.GetString(Bytes);
    }

    /// <summary>Foundry at the HTTP boundary: answers as scripted and keeps every request,
    /// its multipart parts read out by name.
    /// <para>
    /// Every read is a <c>CopyToAsync</c> into a fresh stream, never
    /// <c>ReadAsByteArrayAsync</c>: that one buffers the content on its first read and serves
    /// the buffer after, which would hide a body that cannot be sent twice.
    /// </para></summary>
    private sealed class Foundry : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public Foundry(HttpResponseMessage answer)
            : this(_ => answer)
        {
        }

        public Foundry(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = (request, _) => Task.FromResult(respond(request));
        }

        public Foundry(Func<CancellationToken, Task<HttpResponseMessage>> respond)
        {
            _respond = (_, cancellationToken) => respond(cancellationToken);
        }

        public List<SeenRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var parts = new Dictionary<string, SeenPart>(StringComparer.Ordinal);
            if (request.Content is MultipartFormDataContent multipart)
            {
                foreach (var part in multipart)
                {
                    var disposition = part.Headers.ContentDisposition!;
                    parts[disposition.Name!.Trim('"')] = new SeenPart(
                        disposition.FileName?.Trim('"'),
                        await Serialised(part, cancellationToken));
                }
            }

            Requests.Add(new SeenRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Headers.Contains("api-key"),
                parts));

            return await _respond(request, cancellationToken);
        }

        private static async Task<byte[]> Serialised(HttpContent content, CancellationToken cancellationToken)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            return copy.ToArray();
        }
    }

    /// <summary>A credential whose token takes <paramref name="delay"/> to arrive — an
    /// identity endpoint on a cold start — and that honours cancellation.</summary>
    private sealed class SlowCredential(TimeSpan delay) : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            GetTokenAsync(requestContext, cancellationToken).AsTask().GetAwaiter().GetResult();

        public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new AccessToken("late-token", DateTimeOffset.UtcNow.AddHours(1));
        }
    }

    /// <summary>A credential that answers a fixed token and keeps the scopes it was asked
    /// for.</summary>
    private sealed class RecordingCredential(string token) : TokenCredential
    {
        public List<string[]> Scopes { get; } = [];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Scopes.Add(requestContext.Scopes);
            return new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    /// <summary>Every message logged, formatted, so a test can say what never appears.</summary>
    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
        }
    }
}
