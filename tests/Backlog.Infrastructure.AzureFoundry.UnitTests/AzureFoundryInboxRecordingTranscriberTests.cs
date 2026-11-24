using System.Net;
using System.Text;
using System.Text.Json;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.AzureFoundry.UnitTests;

/// <summary>
/// The Inbox's recording transcriber over Foundry (local ADR 0049): ADTS and M4A alike go
/// out remuxed as M4A parts of at most five minutes, each part's text headed by its offset
/// and prompted with the last words of the part before, and the joined
/// transcript alone goes to the chat deployment for the summary. Any failure is a failed
/// result with this adapter's words, never Foundry's.
/// </summary>
public sealed class AzureFoundryInboxRecordingTranscriberTests : IDisposable
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"foundry-recording-{Guid.NewGuid():N}.json");
    private readonly FakeTranscription _transcription = new();
    private readonly FakeSummary _summary = new();

    public void Dispose() => File.Delete(_path);

    [Fact]
    public async Task An_adts_recording_goes_out_as_m4a_parts_with_offsets_and_running_prompts()
    {
        // 4688 frames of 64 ms are one frame past five minutes: two parts.
        var answer = await Transcriber().TranscribeAsync(new InboxRecordingDto(Adts(4688), "audio/aac"), Cancellation);

        var ok = Assert.IsType<Ok<InboxRecordingTranscriptDto>>(answer.Value);
        Assert.Equal(2, _transcription.Sent.Count);
        Assert.All(_transcription.Sent, request =>
        {
            Assert.Equal("audio/mp4", request.ContentType);
            Assert.Equal("ftyp", Encoding.ASCII.GetString(request.Audio.Span[4..8]));
            Assert.Null(request.Language);
        });
        Assert.Null(_transcription.Sent[0].Prompt);
        Assert.Equal("part 1 words", _transcription.Sent[1].Prompt);
        Assert.Equal("[00:00:00] part 1 words\n\n[00:04:59] part 2 words", ok.Value.TranscriptMd);
        Assert.Equal(ok.Value.TranscriptMd, Assert.Single(_summary.Asked));
        Assert.Equal("Summary.", ok.Value.SummaryMd);
    }

    [Theory]
    [InlineData("audio/mp4")]
    [InlineData("audio/x-m4a")]
    public async Task A_long_m4a_recording_goes_out_in_five_minute_parts_with_offsets_and_running_prompts(string contentType)
    {
        // One M4A file of 4688 frames of 64 ms, written whole: one frame past five minutes.
        var m4a = Assert.Single(AdtsRemuxer.Remux(Adts(4688), TimeSpan.FromMinutes(10))).M4a;

        var answer = await Transcriber().TranscribeAsync(new InboxRecordingDto(m4a, contentType), Cancellation);

        var ok = Assert.IsType<Ok<InboxRecordingTranscriptDto>>(answer.Value);
        Assert.Equal(2, _transcription.Sent.Count);
        Assert.All(_transcription.Sent, request =>
        {
            Assert.Equal("audio/mp4", request.ContentType);
            Assert.Equal("ftyp", Encoding.ASCII.GetString(request.Audio.Span[4..8]));
        });
        Assert.Null(_transcription.Sent[0].Prompt);
        Assert.Equal("part 1 words", _transcription.Sent[1].Prompt);
        Assert.Equal("[00:00:00] part 1 words\n\n[00:04:59] part 2 words", ok.Value.TranscriptMd);
    }

    [Fact]
    public async Task An_m4a_that_cannot_be_read_fails_with_a_reason_and_sends_nothing()
    {
        var m4a = Encoding.ASCII.GetBytes("\0\0\0\u0010ftypM4A \0\0\0\0rest of the file");

        var answer = await Transcriber().TranscribeAsync(new InboxRecordingDto(m4a, "audio/mp4"), Cancellation);

        var error = Assert.IsType<Error>(answer.Value);
        Assert.Equal("inbox.transcription.failed", error.Code);
        Assert.Equal("The recording could not be read as an M4A file.", error.Message);
        Assert.Empty(_transcription.Sent);
    }

    [Fact]
    public async Task An_m4a_whose_frame_outlasts_a_part_fails_with_a_reason_instead_of_throwing()
    {
        var m4a = Mp4AacReaderTests.Classic(delta: 0xFFFF_FFFF, chunkOffset: null);

        var answer = await Transcriber().TranscribeAsync(new InboxRecordingDto(m4a, "audio/mp4"), Cancellation);

        var error = Assert.IsType<Error>(answer.Value);
        Assert.Equal("The recording could not be read as an M4A file.", error.Message);
        Assert.Empty(_transcription.Sent);
    }

    [Fact]
    public async Task A_failed_part_fails_the_whole_and_asks_for_no_summary()
    {
        _transcription.FailOn = 2;

        var answer = await Transcriber().TranscribeAsync(new InboxRecordingDto(Adts(4688), "audio/aac"), Cancellation);

        var error = Assert.IsType<Error>(answer.Value);
        Assert.Equal("inbox.transcription.failed", error.Code);
        Assert.Equal("Part 2 of 2 could not be transcribed.", error.Message);
        Assert.Empty(_summary.Asked);
    }

    [Fact]
    public async Task A_failed_summary_fails_without_foundrys_words()
    {
        _summary.Throw = new AzureFoundryException("Azure Foundry returned 500: the transcript said something private", 500);

        var answer = await Transcriber().TranscribeAsync(new InboxRecordingDto(Adts(3), "audio/aac"), Cancellation);

        var error = Assert.IsType<Error>(answer.Value);
        Assert.DoesNotContain("private", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_is_sent_while_foundry_is_not_configured_or_the_stream_holds_no_frame()
    {
        var unconfigured = Transcriber(configured: false);
        Assert.False(unconfigured.IsAvailable);
        Assert.IsType<Error>((await unconfigured.TranscribeAsync(new InboxRecordingDto(Adts(3), "audio/aac"), Cancellation)).Value);

        Assert.IsType<Error>((await Transcriber().TranscribeAsync(new InboxRecordingDto(new byte[] { 0xFF, 0xF1, 0x60 }, "audio/aac"), Cancellation)).Value);
        Assert.Empty(_transcription.Sent);
    }

    [Theory]
    [InlineData(0, "00:00:00")]
    [InlineData(299.968, "00:04:59")]
    [InlineData(3725, "01:02:05")]
    public void An_offset_is_stamped_as_hours_minutes_seconds(double seconds, string stamp) =>
        Assert.Equal(stamp, AzureFoundryInboxRecordingTranscriber.Stamp(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void The_prompt_is_the_tail_cut_on_a_word()
    {
        var text = string.Join(' ', Enumerable.Range(0, 100).Select(i => $"word{i}"));

        var tail = AzureFoundryInboxRecordingTranscriber.Tail(text)!;

        Assert.True(tail.Length <= AzureFoundryInboxRecordingTranscriber.PromptTailLength);
        Assert.StartsWith("word", tail);
        Assert.EndsWith("word99", tail);
    }

    [Fact]
    public async Task The_summary_asks_the_chat_deployment_with_the_transcript_alone()
    {
        RecordedChat? seen = null;
        var store = Store();
        var chat = new AzureFoundryChatClient(new HttpClient(new ChatHandler(body => seen = body)), store);

        var summary = await chat.SummarizeTranscriptAsync("[00:00:00] We agreed.", Cancellation);

        Assert.Equal("- [ ] Do it", summary);
        Assert.NotNull(seen);
        Assert.Equal("[00:00:00] We agreed.", seen!.User);
        Assert.Contains("Action items:", seen.System, StringComparison.Ordinal);
    }

    private AzureFoundryInboxRecordingTranscriber Transcriber(bool configured = true) =>
        new(_transcription, _summary, Store(configured), NullLogger<AzureFoundryInboxRecordingTranscriber>.Instance);

    private AzureFoundrySettingsStore Store(bool configured = true)
    {
        var store = new AzureFoundrySettingsStore(_path);
        if (configured) store.SetConnection("https://foundry.example.com", "chat", "key", null);
        return store;
    }

    /// <summary>ADTS frames as AdtsRemuxerTests writes them: AAC-LC, 16 kHz, mono.</summary>
    private static byte[] Adts(int frames) => Enumerable.Range(0, frames).SelectMany(_ =>
    {
        const int length = 7 + 4;
        return new byte[] { 0xFF, 0xF1, 0x60, 0x40, (length >> 3) & 0xFF, ((length & 0x07) << 5) | 0x1F, 0xFC, 1, 2, 3, 4 };
    }).ToArray();

    private sealed class FakeTranscription : IAzureFoundryTranscriptionClient
    {
        public List<AzureFoundryTranscriptionRequest> Sent { get; } = [];

        public int? FailOn { get; set; }

        public Task<AzureFoundryTranscriptionResult> TranscribeAsync(AzureFoundryTranscriptionRequest request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            return Task.FromResult(Sent.Count == FailOn
                ? AzureFoundryTranscriptionResult.Failure
                : AzureFoundryTranscriptionResult.Success($"part {Sent.Count} words"));
        }
    }

    private sealed class FakeSummary : IAzureFoundrySummaryClient
    {
        public List<string> Asked { get; } = [];

        public Exception? Throw { get; set; }

        public Task<string> SummarizeTranscriptAsync(string transcript, CancellationToken cancellationToken = default)
        {
            Asked.Add(transcript);
            return Throw is null ? Task.FromResult("Summary.") : Task.FromException<string>(Throw);
        }
    }

    private sealed record RecordedChat(string System, string User);

    private sealed class ChatHandler(Action<RecordedChat> seen) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var messages = body.RootElement.GetProperty("messages");
            seen(new RecordedChat(messages[0].GetProperty("content").GetString()!, messages[1].GetProperty("content").GetString()!));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":"- [ ] Do it"}}]}""", Encoding.UTF8, "application/json"),
            };
        }
    }
}
