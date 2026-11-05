using System.Net;

using Backlog.Infrastructure.Sync;
using Backlog.Modules.Sync.Abstractions;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// A cloud dictation turn (local ADR 0045 §6): the phone records until the person stops or
/// five minutes pass, posts the recording to the sync service with a language hint, and
/// shows what comes back — or why nothing did.
/// </summary>
public sealed class CloudSpeechTranscriberTests
{
    private readonly ScriptedAudioRecorder _recorder = new();
    private readonly ScriptedTranscriptionService _service = new();
    private readonly InMemorySpeechSettingsStore _settings = new() { Engine = DictationEngine.Cloud };
    private readonly CloudDictationAvailability _availability = new();

    [Fact]
    public async Task A_stopped_recording_is_posted_to_the_sync_service_and_its_text_heard()
    {
        _recorder.Recording = AudioRecording.Recorded([9, 8, 7], "audio/webm;codecs=opus");
        await using var transcriber = Transcriber();

        var turn = transcriber.ListenAsync(TestContext.Current.CancellationToken);
        await transcriber.StopAsync();
        var result = await turn;

        Assert.Equal(SpeechTranscript.Heard("hello from the cloud"), result);
        var upload = Assert.Single(_service.Uploads);
        Assert.Equal(HttpMethod.Post, upload.Method);
        Assert.Equal(SyncRoutes.Absolute(SyncRoutes.Transcriptions), upload.Path);
        Assert.Equal("audio/webm; codecs=opus", upload.ContentType);
        Assert.Equal([9, 8, 7], upload.Body);
    }

    [Fact]
    public async Task A_recording_is_stopped_at_five_minutes()
    {
        await using var transcriber = Transcriber();

        var turn = transcriber.ListenAsync(TestContext.Current.CancellationToken);
        await transcriber.StopAsync();
        await turn;

        Assert.Equal([TimeSpan.FromMinutes(5)], _recorder.MaxDurations);
    }

    [Theory]
    [InlineData(DictationLanguage.Dutch, "nl")]
    [InlineData(DictationLanguage.English, "en")]
    [InlineData(DictationLanguage.DutchAndEnglish, null)]
    [InlineData(DictationLanguage.PhoneLanguage, null)]
    public async Task The_upload_hints_the_dictation_language(DictationLanguage choice, string? expected)
    {
        _settings.Language = choice;
        await using var transcriber = Transcriber();

        var turn = transcriber.ListenAsync(TestContext.Current.CancellationToken);
        await transcriber.StopAsync();
        await turn;

        Assert.Equal(expected, Assert.Single(_service.Uploads).Language);
    }

    [Fact]
    public async Task The_turn_says_it_records_then_transcribes()
    {
        var stages = new List<SpeechTurnStage>();
        await using var transcriber = Transcriber();

        var turn = transcriber.ListenAsync(stages.Add, TestContext.Current.CancellationToken);
        Assert.Equal([SpeechTurnStage.Recording], stages);

        await transcriber.StopAsync();
        await turn;

        Assert.Equal([SpeechTurnStage.Recording, SpeechTurnStage.Transcribing], stages);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, SyncErrorCodes.TranscriptionFailed, CloudSpeechTranscriber.TranscriptionFailed)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, SyncErrorCodes.TranscriptionTooLarge, CloudSpeechTranscriber.RecordingTooLarge)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, SyncErrorCodes.TranscriptionTypeNotAllowed, CloudSpeechTranscriber.RecordingTypeRefused)]
    [InlineData(HttpStatusCode.Unauthorized, null, CloudSpeechTranscriber.PairingRefused)]
    [InlineData((HttpStatusCode)426, SyncErrorCodes.ClientOutdated, SyncCompatibility.UpdateRequiredMessage)]
    public async Task A_refused_upload_is_a_failed_turn_and_the_cloud_stays_in_use(HttpStatusCode status, string? code, string message)
    {
        _service.Answer = (_, _) => Task.FromResult(ScriptedTranscriptionService.Problem(status, code));
        await using var transcriber = Transcriber();

        var turn = transcriber.ListenAsync(TestContext.Current.CancellationToken);
        await transcriber.StopAsync();

        Assert.Equal(SpeechTranscript.Failed(message), await turn);
        Assert.True(_availability.IsAvailable);
    }

    [Fact]
    public async Task A_failed_recording_is_a_failed_turn_with_nothing_posted()
    {
        _recorder.Recording = AudioRecording.Failed("Microphone access was denied.");
        await using var transcriber = Transcriber();

        var turn = transcriber.ListenAsync(TestContext.Current.CancellationToken);
        await transcriber.StopAsync();

        Assert.Equal(SpeechTranscript.Failed("Microphone access was denied."), await turn);
        Assert.Empty(_service.Uploads);
    }

    [Fact]
    public async Task An_empty_recording_is_nothing_heard_with_nothing_posted()
    {
        _recorder.Recording = AudioRecording.Recorded([], "audio/webm");
        await using var transcriber = Transcriber();

        var turn = transcriber.ListenAsync(TestContext.Current.CancellationToken);
        await transcriber.StopAsync();

        Assert.Equal(SpeechTranscript.Heard(string.Empty), await turn);
        Assert.Empty(_service.Uploads);
    }

    [Fact]
    public async Task A_service_without_the_route_makes_cloud_dictation_unavailable()
    {
        _service.Answer = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        await using var transcriber = Transcriber();

        var turn = transcriber.ListenAsync(TestContext.Current.CancellationToken);
        await transcriber.StopAsync();

        Assert.Equal(SpeechTranscript.Failed(CloudSpeechTranscriber.UnavailableNotice), await turn);
        Assert.False(_availability.IsAvailable);
    }

    [Fact]
    public async Task A_service_with_no_model_configured_makes_cloud_dictation_unavailable()
    {
        _service.Answer = (_, _) => Task.FromResult(
            ScriptedTranscriptionService.Problem(HttpStatusCode.ServiceUnavailable, SyncErrorCodes.TranscriptionUnavailable));
        await using var transcriber = Transcriber();

        var turn = transcriber.ListenAsync(TestContext.Current.CancellationToken);
        await transcriber.StopAsync();

        Assert.Equal(SpeechTranscript.Failed(CloudSpeechTranscriber.UnavailableNotice), await turn);
        Assert.False(_availability.IsAvailable);
    }

    [Fact]
    public async Task An_unreachable_service_makes_cloud_dictation_unavailable()
    {
        _service.Answer = (_, _) => throw new HttpRequestException("No route to host.");
        await using var transcriber = Transcriber();

        var turn = transcriber.ListenAsync(TestContext.Current.CancellationToken);
        await transcriber.StopAsync();

        Assert.Equal(SpeechTranscript.Failed(CloudSpeechTranscriber.UnavailableNotice), await turn);
        Assert.False(_availability.IsAvailable);
    }

    [Fact]
    public async Task No_answer_within_sixty_seconds_makes_cloud_dictation_unavailable()
    {
        var clock = new FakeTimeProvider();
        _service.Answer = (_, ct) => ScriptedTranscriptionService.Never(ct);
        await using var transcriber = Transcriber(clock);

        var turn = transcriber.ListenAsync(TestContext.Current.CancellationToken);
        await transcriber.StopAsync();
        await WaitUntil(() => _service.Uploads.Count == 1);

        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.False(turn.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(SpeechTranscript.Failed(CloudSpeechTranscriber.UnavailableNotice), await turn);
        Assert.False(_availability.IsAvailable);
    }

    [Fact]
    public async Task A_cancelled_turn_is_cancelled_not_unavailable()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _service.Answer = (_, ct) => ScriptedTranscriptionService.Never(ct);
        await using var transcriber = Transcriber();

        var turn = transcriber.ListenAsync(cancel.Token);
        await transcriber.StopAsync();
        await WaitUntil(() => _service.Uploads.Count == 1);
        await cancel.CancelAsync();

        Assert.True((await turn).IsError);
        Assert.True(_availability.IsAvailable);
    }

    [Fact]
    public async Task The_cloud_engine_is_supported_where_the_phone_can_record()
    {
        await using var transcriber = Transcriber();

        _recorder.Supported = false;
        Assert.False(await transcriber.IsSupportedAsync(TestContext.Current.CancellationToken));

        _recorder.Supported = true;
        Assert.True(await transcriber.IsSupportedAsync(TestContext.Current.CancellationToken));
    }

    private CloudSpeechTranscriber Transcriber(TimeProvider? clock = null) =>
        new(_recorder, _service.Client(clock), _settings, _availability);

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(condition());
    }
}
