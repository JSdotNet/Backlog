using System.Security.Cryptography;

using Backlog.Infrastructure.FileSystem.Inbox;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// An inbox item's files on disk: one folder per item under the inbox folder's
/// <c>attachments</c>, a write that lands whole, a digest check that is the whole
/// of idempotency, and nothing read or opened outside that folder.
/// </summary>
public sealed class WorkspaceInboxAttachmentFilesTests : IDisposable
{
    private static readonly Guid Item = Guid.Parse("0199a3f2-7c41-7d1a-9d0f-3d2c5e6f7a8b");

    private readonly string _inbox = Path.Combine(Path.GetTempPath(), "backlog-inbox-files-tests", Guid.NewGuid().ToString("n"), "_inbox");

    [Fact]
    public async Task A_file_is_written_into_the_items_folder_and_read_back()
    {
        var files = new WorkspaceInboxAttachmentFiles(() => _inbox);
        byte[] bytes = [1, 2, 3, 4];

        var path = await files.WriteAsync(Item, "board.jpg", bytes, TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(Path.GetFullPath(_inbox), "attachments", Item.ToString("D"), "board.jpg"), path);
        Assert.Equal(files.FolderFor(Item), Path.GetDirectoryName(path));
        Assert.Equal(bytes, await files.ReadAsync(path, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(path + ".partial"));
    }

    /// <summary>A write that fails leaves nothing half-written in the item's
    /// folder — the folder the task is later handed.</summary>
    [Fact]
    public async Task A_write_that_fails_leaves_no_partial_file_behind()
    {
        var files = new WorkspaceInboxAttachmentFiles(() => _inbox);
        var target = files.PathFor(Item, "blocked.pdf");

        // A folder where the file should go: the move over it fails.
        Directory.CreateDirectory(target);

        // Access denied on Windows, an IO error elsewhere: which one is the
        // operating system's to say, so any failure will do.
        var failure = await Record.ExceptionAsync(() => files.WriteAsync(Item, "blocked.pdf", [1, 2, 3], TestContext.Current.CancellationToken));

        Assert.NotNull(failure);
        Assert.False(File.Exists(target + ".partial"));
    }

    [Fact]
    public async Task Holds_is_true_only_for_the_file_with_the_right_digest()
    {
        var files = new WorkspaceInboxAttachmentFiles(() => _inbox);
        byte[] bytes = [5, 6, 7];
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var path = files.PathFor(Item, "a.bin");

        Assert.False(await files.HoldsAsync(path, digest, TestContext.Current.CancellationToken));

        await files.WriteAsync(Item, "a.bin", bytes, TestContext.Current.CancellationToken);

        Assert.True(await files.HoldsAsync(path, digest, TestContext.Current.CancellationToken));
        Assert.True(await files.HoldsAsync(path, digest.ToUpperInvariant(), TestContext.Current.CancellationToken));
        Assert.False(await files.HoldsAsync(path, new string('0', 64), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Nothing_outside_the_attachments_folder_is_read_or_opened()
    {
        var files = new WorkspaceInboxAttachmentFiles(() => _inbox);
        Directory.CreateDirectory(_inbox);
        var outside = Path.Combine(_inbox, "elsewhere.txt");
        await File.WriteAllTextAsync(outside, "not an attachment", TestContext.Current.CancellationToken);

        Assert.Null(await files.ReadAsync(outside, TestContext.Current.CancellationToken));
        Assert.False(await files.HoldsAsync(outside, new string('0', 64), TestContext.Current.CancellationToken));
        Assert.True(files.Open(outside).IsFailure);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("sub/file.txt")]
    public void A_file_name_that_names_a_folder_is_refused(string fileName)
    {
        var files = new WorkspaceInboxAttachmentFiles(() => _inbox);

        Assert.Throws<ArgumentException>(() => files.PathFor(Item, fileName));
    }

    [Fact]
    public void The_folder_follows_the_inbox_folder_it_is_asked_for_each_time()
    {
        var current = _inbox;
        var files = new WorkspaceInboxAttachmentFiles(() => current);
        var before = files.FolderFor(Item);

        current = Path.Combine(Path.GetTempPath(), "moved-workspace", "_inbox");

        Assert.NotEqual(before, files.FolderFor(Item));
        Assert.StartsWith(Path.GetFullPath(current), files.FolderFor(Item), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try
        {
            var root = Path.GetDirectoryName(_inbox)!;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
