using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Features.RunCapture;

namespace Backlog.Modules.Capture.UnitTests;

/// <summary>
/// The id a capture is delivered under is what makes a re-run free: the same
/// entry read again must arrive as the same id, and two different entries — or
/// the same entry id at two different kinds of source — must not collide.
/// </summary>
public sealed class CaptureIdsTests
{
    [Fact]
    public void The_same_source_and_entry_always_give_the_same_id()
    {
        var first = CaptureIds.For(CaptureSourceKind.YouTube, "yt:video:abc123");
        var second = CaptureIds.For(CaptureSourceKind.YouTube, "yt:video:abc123");

        Assert.Equal(first, second);
        Assert.NotEqual(Guid.Empty, first);
    }

    [Fact]
    public void A_different_entry_gives_a_different_id()
    {
        Assert.NotEqual(
            CaptureIds.For(CaptureSourceKind.YouTube, "yt:video:abc123"),
            CaptureIds.For(CaptureSourceKind.YouTube, "yt:video:abc124"));
    }

    [Fact]
    public void The_same_entry_id_at_a_different_kind_of_source_gives_a_different_id()
    {
        Assert.NotEqual(
            CaptureIds.For(CaptureSourceKind.YouTube, "https://example.org/post/1"),
            CaptureIds.For(CaptureSourceKind.Website, "https://example.org/post/1"));
    }

    /// <summary>The value is pinned, not just stable within a process: an id
    /// scheme that changed between builds would re-deliver every entry ever
    /// captured. Recompute this only with a migration in hand. The literal is
    /// SHA-256 over the UTF-8 of <c>capture:website:https://example.org/post/1</c>,
    /// truncated to sixteen bytes with the version-8 and variant bits set.</summary>
    [Fact]
    public void The_scheme_is_pinned()
    {
        var id = CaptureIds.For(CaptureSourceKind.Website, "https://example.org/post/1");

        Assert.Equal(Guid.Parse("5cde20b3-28ae-81f3-ab7b-abf248fdf603"), id);
        Assert.Equal(8, id.Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_entry_with_no_id_is_refused(string externalId)
    {
        Assert.Throws<ArgumentException>(() => CaptureIds.For(CaptureSourceKind.Website, externalId));
    }
}
