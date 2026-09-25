using Backlog.Modules.Capture.Abstractions;

namespace Backlog.Modules.Capture.UnitTests;

/// <summary>
/// The spelling the vocabulary is written in on disk and in the Inbox.
/// <para>
/// The slugs are the strings <c>.devbook/domain/capture/domain.md#capture-source</c>
/// lists, and they are also what the Inbox pane's <c>InboxSource.Channel</c>
/// carries — a raw string there by design, since Inbox may not reference this
/// project. Two spellings would be a badge that no longer names its channel.
/// </para>
/// </summary>
public sealed class CaptureSourceKindsTests
{
    [Theory]
    [InlineData(CaptureSourceKind.Mobile, "mobile")]
    [InlineData(CaptureSourceKind.YouTube, "youtube")]
    [InlineData(CaptureSourceKind.Website, "website")]
    [InlineData(CaptureSourceKind.Email, "email")]
    [InlineData(CaptureSourceKind.WebClipper, "web_clipper")]
    [InlineData(CaptureSourceKind.Ide, "ide")]
    [InlineData(CaptureSourceKind.Manual, "manual")]
    public void Slugs_match_the_domain_spelling(CaptureSourceKind kind, string slug)
    {
        Assert.Equal(slug, CaptureSourceKinds.Slug(kind));
    }

    [Fact]
    public void TryParse_round_trips()
    {
        foreach (var kind in Enum.GetValues<CaptureSourceKind>())
        {
            Assert.True(CaptureSourceKinds.TryParse(CaptureSourceKinds.Slug(kind), out var parsed));
            Assert.Equal(kind, parsed);
        }

        Assert.False(CaptureSourceKinds.TryParse("rss", out _));
        Assert.False(CaptureSourceKinds.TryParse(string.Empty, out _));
    }

    /// <summary>The three a person can point at something and wait on. The
    /// rest arrive on their own — a share sheet, a clipper, an IDE — and have
    /// nothing to poll.</summary>
    [Fact]
    public void The_monitorable_sources_are_the_three_that_poll()
    {
        Assert.Equal(
            [CaptureSourceKind.YouTube, CaptureSourceKind.Website, CaptureSourceKind.Email],
            CaptureSourceKinds.Monitorable);
    }

    [Fact]
    public void Labels_read_the_way_the_inbox_badge_reads_them()
    {
        Assert.Equal("YouTube", CaptureSourceKinds.Label(CaptureSourceKind.YouTube));
        Assert.Equal("Website", CaptureSourceKinds.Label(CaptureSourceKind.Website));
        Assert.Equal("Email", CaptureSourceKinds.Label(CaptureSourceKind.Email));
        Assert.Equal("Web clipper", CaptureSourceKinds.Label(CaptureSourceKind.WebClipper));
        Assert.Equal("IDE", CaptureSourceKinds.Label(CaptureSourceKind.Ide));
    }
}
