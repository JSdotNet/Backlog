using System.Diagnostics;
using System.Security.Cryptography;

using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.FileSystem.Inbox;

/// <summary>
/// Answers the Inbox's <see cref="IInboxAttachmentFiles"/> port on disk:
/// <c>&lt;workspace&gt;/_inbox/attachments/&lt;item id&gt;/&lt;file name&gt;</c>.
/// <para>
/// Under the workspace's inbox folder because that folder is one the app owns
/// (<c>WorkspaceSettingsStore.OwnedRootFolders</c>): moving the workspace carries
/// the files with it, and a backup of the root holds them. The inbox folder is
/// read per call, for the reason <c>RootedSqliteInboxRepository</c> reads its root
/// per call — the workspace can move while the app runs.
/// </para>
/// <para>
/// Every path this adapter is handed back is one it made, so the checks here are
/// against a bug rather than a person: a file name with a directory in it, or a
/// path outside the attachments folder, is refused rather than followed.
/// </para>
/// </summary>
public sealed class WorkspaceInboxAttachmentFiles(Func<string> inboxDirectory) : IInboxAttachmentFiles
{
    /// <summary>The folder under the inbox folder the item folders live in.</summary>
    public const string FolderName = "attachments";

    private readonly Func<string> _inboxDirectory = inboxDirectory ?? throw new ArgumentNullException(nameof(inboxDirectory));

    private string Root => Path.GetFullPath(Path.Combine(_inboxDirectory(), FolderName));

    public string FolderFor(Guid itemId) => Path.Combine(Root, itemId.ToString("D"));

    public string PathFor(Guid itemId, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw new ArgumentException("An attachment's file name may not name a folder.", nameof(fileName));
        }

        return Path.Combine(FolderFor(itemId), fileName);
    }

    public async Task<string> WriteAsync(Guid itemId, string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var path = PathFor(itemId, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Written beside the target and moved over it, so a reader — the pane
        // drawing a thumbnail, the person opening the folder — never sees half a
        // file, and a crash mid-write leaves the old one or none.
        var partial = path + ".partial";

        try
        {
            await File.WriteAllBytesAsync(partial, content, cancellationToken).ConfigureAwait(false);
            File.Move(partial, path, overwrite: true);
        }
        catch
        {
            // Nothing half-written is left in the item's folder: that folder is
            // what the task is handed, and a stray .partial there is a file the
            // person did not send.
            try { File.Delete(partial); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }

        return path;
    }

    public async Task<bool> HoldsAsync(string path, string sha256, CancellationToken cancellationToken = default)
    {
        if (!IsOurs(path) || !File.Exists(path)) return false;

        await using var stream = File.OpenRead(path);
        var digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));

        return string.Equals(digest, sha256, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<byte[]?> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!IsOurs(path) || !File.Exists(path)) return null;

        return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public Result Open(string path)
    {
        if (!IsOurs(path) || !File.Exists(path))
        {
            return Result.Failure(Error.NotFound(
                "inbox.attachment.missing",
                "The file is no longer in the item's folder."));
        }

        try
        {
            // The shell's own association, which is what "open" means to the
            // person: a PDF in their reader, a picture in their viewer. No
            // console window either: the file's application is the window.
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, CreateNoWindow = true });
            return Result.Success();
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return Result.Failure(new Error("inbox.attachment.open_failed", $"The file could not be opened: {failure.Message}"));
        }
    }

    /// <summary>Whether <paramref name="path"/> lies inside the attachments
    /// folder. A stored path from before a workspace move points at the old
    /// root and is not ours any more; the intake then fetches the file again
    /// into the new one.</summary>
    private bool IsOurs(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var full = Path.GetFullPath(path);
        var root = Path.TrimEndingDirectorySeparator(Root) + Path.DirectorySeparatorChar;

        return full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
