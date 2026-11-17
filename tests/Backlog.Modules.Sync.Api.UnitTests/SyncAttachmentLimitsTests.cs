using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Api.Options;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// The allowlist both ends read (local ADR 0014): a meeting recording of local
/// ADR 0048 — AAC in an ADTS stream or in MPEG-4 — is on it, so the phone queues
/// it and the service takes it, and a configured list still replaces it.
/// </summary>
public sealed class SyncAttachmentLimitsTests
{
    [Theory]
    [InlineData("audio/aac")]
    [InlineData("audio/mp4")]
    [InlineData("audio/x-m4a")]
    [InlineData("audio/mp4; codecs=mp4a.40.2")]
    [InlineData("AUDIO/AAC")]
    public void A_meeting_recording_is_allowed_by_default(string mediaType) =>
        Assert.True(SyncAttachmentLimits.AllowsByDefault(mediaType));

    [Theory]
    [InlineData("audio/aac")]
    [InlineData("audio/mp4")]
    [InlineData("audio/x-m4a")]
    public void With_no_list_configured_the_service_takes_a_meeting_recording(string mediaType) =>
        Assert.True(new SyncAttachmentOptions().Allows(mediaType));

    [Fact]
    public void A_configured_list_replaces_the_default_rather_than_adding_to_it()
    {
        var options = new SyncAttachmentOptions { AllowedContentTypes = ["application/pdf"] };

        Assert.False(options.Allows("audio/aac"));
    }
}
