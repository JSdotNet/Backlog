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
    [InlineData(CaptureSourceKind.Import, "import")]
    [InlineData(CaptureSourceKind.EventPage, "event_page")]
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

    /// <summary>The four a person can point at something and wait on, the
    /// event page beside the website it is a kind of. The rest arrive on their
    /// own — a share sheet, a clipper, an IDE — and have nothing to poll.</summary>
    [Fact]
    public void The_monitorable_sources_are_the_four_that_poll()
    {
        Assert.Equal(
            [CaptureSourceKind.YouTube, CaptureSourceKind.Website, CaptureSourceKind.EventPage, CaptureSourceKind.Email],
            CaptureSourceKinds.Monitorable);
    }

    /// <summary>The kind is stored by its slug, but it was appended rather than
    /// slotted in beside Website all the same, so a number anything wrote for
    /// an earlier kind still names that kind.</summary>
    [Fact]
    public void The_event_page_kind_was_appended_so_earlier_kinds_keep_their_numbers()
    {
        Assert.Equal(7, (int)CaptureSourceKind.Import);
        Assert.Equal(8, (int)CaptureSourceKind.EventPage);
    }

    [Fact]
    public void Labels_read_the_way_the_inbox_badge_reads_them()
    {
        Assert.Equal("YouTube", CaptureSourceKinds.Label(CaptureSourceKind.YouTube));
        Assert.Equal("Website", CaptureSourceKinds.Label(CaptureSourceKind.Website));
        Assert.Equal("Email", CaptureSourceKinds.Label(CaptureSourceKind.Email));
        Assert.Equal("Web clipper", CaptureSourceKinds.Label(CaptureSourceKind.WebClipper));
        Assert.Equal("IDE", CaptureSourceKinds.Label(CaptureSourceKind.Ide));
        Assert.Equal("Event page", CaptureSourceKinds.Label(CaptureSourceKind.EventPage));
    }
}
