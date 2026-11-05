using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Backlog.Infrastructure.AzureFoundry;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Options;
using Backlog.Modules.Sync.Api.Security;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features.TranscribeDictation;
using Backlog.Modules.Sync.Ports;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// The dictation relay (local ADR 0045): one recording in, its text out, behind the same
/// bearer and owner scope as every other sync route. The cap, the audio allowlist and the
/// language hint are refused at the door, and every upstream failure reads as one fixed
/// 502 that quotes nothing Foundry said.
/// </summary>
public sealed class TranscriptionEndpointTests
{
    /// <summary>Small, so a test can go over it without allocating 25 MB.</summary>
    private const long Cap = 1024;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_a_token_the_route_answers_401()
    {
        using var service = Service(new ScriptedTranscriber());

        var response = await service.CreateClient().Transcribe(Bytes(10), "audio/webm");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>The command runs under the scope the owner-scope filter read out of the
    /// caller's token — the device that called, and its owner — not under one taken from
    /// anywhere the caller writes.</summary>
    [Fact]
    public async Task A_recording_is_handled_under_the_calling_devices_owner_scope()
    {
        var handler = new ScopeRecordingCommandHandler();
        using var service = new SyncServiceFactory
        {
            TestServices = services =>
            {
                services.RemoveAll<ICommandHandler<TranscribeDictationCommand, Result<TranscriptionResponse>>>();
                services.AddSingleton<ICommandHandler<TranscribeDictationCommand, Result<TranscriptionResponse>>>(handler);
            },
        };
        var client = service.CreateClient();
        var device = await client.RegisterDevice("Phone");
        client.Bearing(await client.DeviceToken(device));

        var response = await client.Transcribe(Bytes(10), "audio/webm");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var scope = Assert.Single(handler.Scopes);
        Assert.Equal(new OwnerScope(new OwnerId(device.OwnerId), new DeviceId(device.DeviceId)), scope);
    }

    /// <summary>The cap is the host's request-body limit too, so Kestrel refuses a body over
    /// it before the handler reads a byte; and the route takes the paired-device policy.</summary>
    [Fact]
    public void The_route_carries_the_cap_as_its_body_limit_and_the_paired_device_policy()
    {
        using var service = Service(new ScriptedTranscriber());

        var route = Assert.Single(
            service.Services.GetRequiredService<EndpointDataSource>().Endpoints,
            endpoint => endpoint is RouteEndpoint candidate
                && candidate.RoutePattern.RawText?.EndsWith(SyncRoutes.Transcriptions, StringComparison.Ordinal) == true
                && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(HttpMethods.Post) == true);

        var limit = route.Metadata.GetMetadata<IRequestSizeLimitMetadata>();
        Assert.NotNull(limit);
        Assert.Equal(Cap, limit.MaxRequestBodySize);
        Assert.Contains(route.Metadata.GetOrderedMetadata<IAuthorizeData>(), data => data.Policy == SyncPolicies.PairedDevice);
    }

    /// <summary>The Aspire-less shape: no endpoint configured, so the module's stand-in
    /// answers and the phone falls back to on-device.</summary>
    [Fact]
    public async Task With_no_endpoint_configured_the_route_answers_503_unavailable()
    {
        using var service = new SyncServiceFactory();
        var device = await service.CreateClient().RegisteredDevice("Phone");

        var response = await device.Transcribe(Bytes(10), "audio/mp4");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(SyncErrorCodes.TranscriptionUnavailable, (await Problem(response)).Code);
    }

    [Fact]
    public async Task A_recording_is_answered_with_its_text_and_reaches_the_transcriber_intact()
    {
        var transcriber = new ScriptedTranscriber { Answer = DictationTranscription.Transcribed("buy milk") };
        using var service = Service(transcriber);
        var device = await service.CreateClient().RegisteredDevice("Phone");
        var bytes = Bytes(300);

        var response = await device.Transcribe(bytes, "audio/mp4", language: "nl");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new TranscriptionResponse("buy milk"), await response.Content.ReadFromJsonAsync<TranscriptionResponse>(Cancellation));
        var heard = Assert.Single(transcriber.Heard);
        Assert.Equal(bytes, heard.Content.ToArray());
        Assert.Equal("audio/mp4", heard.ContentType);
        Assert.Equal("nl", heard.Language);
    }

    [Fact]
    public async Task Without_a_language_hint_none_is_passed_on()
    {
        var transcriber = new ScriptedTranscriber();
        using var service = Service(transcriber);
        var device = await service.CreateClient().RegisteredDevice("Phone");

        Assert.Equal(HttpStatusCode.OK, (await device.Transcribe(Bytes(10), "audio/webm")).StatusCode);
        Assert.Null(Assert.Single(transcriber.Heard).Language);
    }

    [Fact]
    public async Task A_Content_Length_over_the_cap_is_a_413_and_the_transcriber_is_never_asked()
    {
        var transcriber = new ScriptedTranscriber();
        using var service = Service(transcriber);
        var device = await service.CreateClient().RegisteredDevice("Phone");

        var response = await device.Transcribe(Bytes((int)Cap + 1), "audio/mp4");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(SyncErrorCodes.TranscriptionTooLarge, (await Problem(response)).Code);
        Assert.Empty(transcriber.Heard);
    }

    /// <summary>A chunked body declares no length, so only counting it can tell — and the
    /// count stops it where it passes the cap.</summary>
    [Fact]
    public async Task An_unsized_body_that_runs_past_the_cap_is_a_413_and_the_transcriber_is_never_asked()
    {
        var transcriber = new ScriptedTranscriber();
        using var service = Service(transcriber);
        var device = await service.CreateClient().RegisteredDevice("Phone");

        var response = await device.Transcribe(Bytes((int)Cap * 3), "audio/webm", unsized: true);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(SyncErrorCodes.TranscriptionTooLarge, (await Problem(response)).Code);
        Assert.Empty(transcriber.Heard);
    }

    [Theory]
    [InlineData("audio/ogg")]
    [InlineData("video/mp4")]
    [InlineData("application/octet-stream")]
    public async Task A_type_off_the_audio_allowlist_is_a_415_naming_it(string contentType)
    {
        var transcriber = new ScriptedTranscriber();
        using var service = Service(transcriber);
        var device = await service.CreateClient().RegisteredDevice("Phone");

        var response = await device.Transcribe(Bytes(10), contentType);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        var problem = await Problem(response);
        Assert.Equal(SyncErrorCodes.TranscriptionTypeNotAllowed, problem.Code);
        Assert.Contains(contentType, problem.Detail, StringComparison.Ordinal);
        Assert.Empty(transcriber.Heard);
    }

    [Fact]
    public async Task A_recording_with_no_Content_Type_is_a_415()
    {
        using var service = Service(new ScriptedTranscriber());
        var device = await service.CreateClient().RegisteredDevice("Phone");

        var response = await device.Transcribe(Bytes(10), contentType: null);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(SyncErrorCodes.TranscriptionTypeNotAllowed, (await Problem(response)).Code);
    }

    /// <summary>A browser's MediaRecorder says <c>audio/webm;codecs=opus</c>; the codec does
    /// not change what the type is, and neither does case.</summary>
    [Theory]
    [InlineData("audio/webm;codecs=opus", "audio/webm")]
    [InlineData("AUDIO/MP4", "AUDIO/MP4")]
    public async Task An_allowed_type_with_parameters_or_in_another_case_is_accepted(string contentType, string passedOn)
    {
        var transcriber = new ScriptedTranscriber();
        using var service = Service(transcriber);
        var device = await service.CreateClient().RegisteredDevice("Phone");

        var response = await device.Transcribe(Bytes(10), contentType);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(passedOn, Assert.Single(transcriber.Heard).ContentType);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("nl-NL")]
    [InlineData("english")]
    public async Task A_language_other_than_nl_or_en_is_a_400_naming_it(string language)
    {
        var transcriber = new ScriptedTranscriber();
        using var service = Service(transcriber);
        var device = await service.CreateClient().RegisteredDevice("Phone");

        var response = await device.Transcribe(Bytes(10), "audio/mp4", language);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await Problem(response);
        Assert.Equal(SyncErrorCodes.TranscriptionInvalid, problem.Code);
        Assert.Contains(language, problem.Detail, StringComparison.Ordinal);
        Assert.Empty(transcriber.Heard);
    }

    [Fact]
    public async Task A_failed_transcription_is_a_502_with_a_fixed_detail()
    {
        using var service = Service(new ScriptedTranscriber { Answer = DictationTranscription.Failed });
        var device = await service.CreateClient().RegisteredDevice("Phone");

        var response = await device.Transcribe(Bytes(10), "audio/mp4");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(SyncErrorCodes.TranscriptionFailed, (await Problem(response)).Code);
    }

    /// <summary>The whole path the deployed service takes, with only Foundry faked at the
    /// HTTP boundary: an endpoint configured puts the Foundry client behind the module's
    /// port, and what Foundry answers comes back as the text.</summary>
    [Fact]
    public async Task With_an_endpoint_configured_the_recording_is_relayed_to_foundry()
    {
        var foundry = new FoundryStub(HttpStatusCode.OK, """{"text":"hello from foundry"}""");
        using var service = Relaying(foundry);
        var device = await service.CreateClient().RegisteredDevice("Phone");

        var response = await device.Transcribe(Bytes(20), "audio/webm", language: "en");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hello from foundry", (await response.Content.ReadFromJsonAsync<TranscriptionResponse>(Cancellation))!.Text);
        Assert.Equal(
            "https://foundry.example/openai/deployments/gpt-4o-transcribe/audio/transcriptions?api-version=2025-03-01-preview",
            foundry.LastUri?.ToString());
        Assert.Null(foundry.LastAuthorization);
    }

    /// <summary>Foundry's message may quote what it was sent, so it never reaches the
    /// answer: the phone reads the code, and the detail is the service's own sentence.</summary>
    [Fact]
    public async Task A_foundry_refusal_is_a_502_that_quotes_nothing_foundry_said()
    {
        var foundry = new FoundryStub(
            HttpStatusCode.BadRequest,
            """{"error":{"code":"invalid_value","message":"the audio said: my secret plans"}}""");
        using var service = Relaying(foundry);
        var device = await service.CreateClient().RegisteredDevice("Phone");

        var response = await device.Transcribe(Bytes(20), "audio/mp4");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Cancellation);
        Assert.Contains(SyncErrorCodes.TranscriptionFailed, body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("invalid_value", body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_options_default_to_the_adr_limits()
    {
        var options = new SyncTranscriptionOptions();

        Assert.Equal(26214400, options.MaxBytes);
        Assert.Equal("gpt-4o-transcribe", options.Deployment);
        Assert.Equal("2025-03-01-preview", options.ApiVersion);
        Assert.Equal(AzureFoundryTranscriptionCredential.ManagedIdentity, options.Credential);
        Assert.Null(options.Endpoint);
        foreach (var type in new[] { "audio/mp4", "audio/x-m4a", "audio/webm", "audio/wav", "audio/mpeg" })
        {
            Assert.True(options.Allows(type), type);
        }

        Assert.False(options.Allows("audio/ogg"));
    }

    [Fact]
    public void A_configured_allowlist_replaces_the_default()
    {
        using var service = new SyncServiceFactory
        {
            MoreConfiguration = [($"{SyncTranscriptionOptions.SectionName}:ContentTypes:0", "audio/ogg")],
        };

        var options = service.Services.GetRequiredService<IOptions<SyncTranscriptionOptions>>().Value;

        Assert.True(options.Allows("audio/ogg"));
        Assert.False(options.Allows("audio/mp4"));
    }

    [Theory]
    [InlineData("Endpoint", "not a url")]
    [InlineData("Endpoint", "/relative/path")]
    [InlineData("MaxBytes", "0")]
    [InlineData("MaxBytes", "26214401")]
    [InlineData("Deployment", " ")]
    [InlineData("ApiVersion", " ")]
    public void A_bad_transcription_setting_stops_the_start(string key, string value)
    {
        using var misconfigured = new SyncServiceFactory
        {
            Configuration = ($"{SyncTranscriptionOptions.SectionName}:{key}", value),
        };

        Assert.ThrowsAny<OptionsValidationException>(() => misconfigured.CreateClient());
    }

    /// <summary>A managed-identity token is a bearer for every Cognitive Services account
    /// the identity may call, so it never travels in clear text; only the local stand-in,
    /// which takes no credential, is reached over http.</summary>
    [Fact]
    public void An_http_endpoint_with_the_managed_identity_stops_the_start()
    {
        using var misconfigured = new SyncServiceFactory
        {
            Configuration = ($"{SyncTranscriptionOptions.SectionName}:Endpoint", "http://foundry.example/"),
            MoreConfiguration = [($"{SyncTranscriptionOptions.SectionName}:Credential", "ManagedIdentity")],
        };

        var refusal = Assert.ThrowsAny<OptionsValidationException>(() => misconfigured.CreateClient());
        Assert.Contains("https", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_http_endpoint_without_a_credential_is_the_local_stand_in_and_starts()
    {
        using var standIn = new SyncServiceFactory
        {
            Configuration = ($"{SyncTranscriptionOptions.SectionName}:Endpoint", "http://localhost:5099/"),
            MoreConfiguration = [($"{SyncTranscriptionOptions.SectionName}:Credential", "None")],
        };

        using var client = standIn.CreateClient();

        Assert.Equal("http://localhost:5099/", standIn.Services.GetRequiredService<IOptions<SyncTranscriptionOptions>>().Value.Endpoint);
    }

    private static SyncServiceFactory Service(IDictationTranscriber transcriber) => new()
    {
        Configuration = ($"{SyncTranscriptionOptions.SectionName}:MaxBytes", Cap.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        TestServices = services =>
        {
            services.RemoveAll<IDictationTranscriber>();
            services.AddSingleton(transcriber);
        },
    };

    private static SyncServiceFactory Relaying(FoundryStub foundry) => new()
    {
        Configuration = ($"{SyncTranscriptionOptions.SectionName}:Endpoint", "https://foundry.example/"),
        MoreConfiguration = [($"{SyncTranscriptionOptions.SectionName}:Credential", "None")],
        TestServices = services => services
            .AddHttpClient(nameof(IAzureFoundryTranscriptionClient))
            .ConfigurePrimaryHttpMessageHandler(() => foundry),
    };

    private static byte[] Bytes(int length)
    {
        var bytes = new byte[length];
        new Random(length).NextBytes(bytes);
        return bytes;
    }

    private static async Task<ProblemBody> Problem(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemBody>(Cancellation))!;

    /// <summary>The module's port, answering what a test scripts and keeping what it
    /// heard.</summary>
    private sealed class ScriptedTranscriber : IDictationTranscriber
    {
        public DictationTranscription Answer { get; init; } = DictationTranscription.Transcribed("heard");

        public List<DictationAudio> Heard { get; } = [];

        public Task<DictationTranscription> TranscribeAsync(DictationAudio audio, CancellationToken cancellationToken = default)
        {
            Heard.Add(audio);
            return Task.FromResult(Answer);
        }
    }

    /// <summary>The command handler, standing in to keep the scope each command carried and
    /// to answer a fixed text.</summary>
    private sealed class ScopeRecordingCommandHandler : ICommandHandler<TranscribeDictationCommand, Result<TranscriptionResponse>>
    {
        public List<OwnerScope> Scopes { get; } = [];

        public Task<Result<TranscriptionResponse>> Handle(TranscribeDictationCommand command, CancellationToken cancellationToken = default)
        {
            Scopes.Add(command.Scope);
            return Task.FromResult<Result<TranscriptionResponse>>(new TranscriptionResponse("heard"));
        }
    }

    /// <summary>Foundry at the HTTP boundary: one fixed answer, and the URI and credential
    /// of the last request.</summary>
    private sealed class FoundryStub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        public AuthenticationHeaderValue? LastAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastAuthorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}

/// <summary>A dictation upload in one line.</summary>
internal static class TranscriptionClientExtensions
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <param name="unsized">Send with no Content-Length, as a chunked body, so only
    /// reading it can tell how large it is.</param>
    internal static Task<HttpResponseMessage> Transcribe(
        this HttpClient client,
        byte[] bytes,
        string? contentType,
        string? language = null,
        bool unsized = false)
    {
        HttpContent content = unsized ? new UnsizedAudio(bytes) : new ByteArrayContent(bytes);
        if (contentType is not null)
        {
            content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }

        var path = SyncRoutes.Absolute(SyncRoutes.Transcriptions)
                   + (language is null ? string.Empty : $"?{SyncRoutes.TranscriptionLanguageQuery}={Uri.EscapeDataString(language)}");

        return client.PostAsync(path, content, Cancellation);
    }

    private sealed class UnsizedAudio(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
