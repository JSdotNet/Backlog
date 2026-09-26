using Backlog.Modules.Inbox.DomainModels;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// The invariants an item keeps about its files, one test each: distinct by id,
/// the capture's metadata never edited, a file either on this machine or not —
/// never a path and an error at once — and one stable, safe file name per file.
/// </summary>
public sealed class InboxAttachmentTests
{
    private static readonly string Digest = new('a', 64);

    [Fact]
    public void A_file_is_recorded_once_by_id()
    {
        var item = Items.FromPhone();
        var id = Guid.CreateVersion7();

        Assert.Equal(1, item.RecordAttachments([InboxAttachment.Named(id, "a.jpg", "image/jpeg", 3, Digest)]));
        Assert.Equal(0, item.RecordAttachments([InboxAttachment.Named(id, "renamed.jpg", "image/png", 9, Digest)]));

        var attachment = Assert.Single(item.Attachments);
        Assert.Equal("a.jpg", attachment.Name);
        Assert.Equal("image/jpeg", attachment.ContentType);
        Assert.Equal(3, attachment.SizeBytes);
    }

    [Fact]
    public void Recording_again_does_not_reset_a_downloaded_file()
    {
        var item = Items.FromPhone();
        var named = InboxAttachment.Named(Guid.CreateVersion7(), "a.jpg", "image/jpeg", 3, Digest);
        item.RecordAttachments([named]);
        item.MarkAttachmentDownloaded(named.Id, "folder/a.jpg", Items.Noon);

        item.RecordAttachments([named]);

        Assert.True(Assert.Single(item.Attachments).IsDownloaded);
    }

    [Fact]
    public void A_file_is_downloaded_or_failed_never_both()
    {
        var item = Items.FromPhone();
        var named = InboxAttachment.Named(Guid.CreateVersion7(), "a.pdf", "application/pdf", 3, Digest);
        item.RecordAttachments([named]);

        item.MarkAttachmentDownloaded(named.Id, "folder/a.pdf", Items.Noon);
        item.MarkAttachmentFailed(named.Id, "Gone.", Items.Noon.AddMinutes(1));

        var failed = Assert.Single(item.Attachments);
        Assert.Null(failed.LocalPath);
        Assert.Null(failed.DownloadedAt);
        Assert.Equal("Gone.", failed.LastError);
        Assert.False(failed.IsWaiting);

        item.MarkAttachmentDownloaded(named.Id, "folder/a.pdf", Items.Noon.AddMinutes(2));

        var downloaded = Assert.Single(item.Attachments);
        Assert.True(downloaded.IsDownloaded);
        Assert.Null(downloaded.LastError);
    }

    [Theory]
    [InlineData("", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", 1)]
    [InlineData("x", "short", 1)]
    [InlineData("x", "zz23456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", 1)]
    [InlineData("x", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", -1)]
    public void A_file_no_fetch_could_honour_is_refused(string id, string sha256, long size)
    {
        var attachmentId = id.Length == 0 ? Guid.Empty : Guid.CreateVersion7();

        Assert.ThrowsAny<ArgumentException>(() => InboxAttachment.Named(attachmentId, "a", "image/png", size, sha256));
    }

    [Fact]
    public void A_file_with_no_name_or_type_is_still_a_file()
    {
        var id = Guid.CreateVersion7();

        var attachment = InboxAttachment.Named(id, "  ", null, 0, Digest.ToUpperInvariant());

        Assert.Equal(id.ToString("D"), attachment.Name);
        Assert.Equal("application/octet-stream", attachment.ContentType);
        Assert.Equal(Digest, attachment.Sha256);
    }

    [Fact]
    public void File_names_are_safe_and_the_second_file_under_a_name_gets_its_id()
    {
        var item = Items.FromPhone();
        var first = InboxAttachment.Named(Guid.CreateVersion7(), "IMG 1.jpg", "image/jpeg", 1, Digest);
        var second = InboxAttachment.Named(Guid.CreateVersion7(), "img 1.jpg", "image/jpeg", 1, Digest);
        var unsafeName = InboxAttachment.Named(Guid.CreateVersion7(), "../notes: v2?.txt", "text/plain", 1, Digest);
        item.RecordAttachments([first, second, unsafeName]);

        Assert.Equal("IMG 1.jpg", item.AttachmentFileName(first.Id));
        Assert.Equal($"img 1-{second.Id.ToString("N")[..8]}.jpg", item.AttachmentFileName(second.Id));
        Assert.Equal(".._notes_ v2_.txt", item.AttachmentFileName(unsafeName.Id));

        // The same answer every time, so a retry writes where the first attempt did.
        Assert.Equal(item.AttachmentFileName(second.Id), item.AttachmentFileName(second.Id));
    }
}
