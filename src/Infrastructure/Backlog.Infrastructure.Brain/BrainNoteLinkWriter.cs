using System.Text;

using Backlog.Modules.Brain.Abstractions;

namespace Backlog.Infrastructure.Brain;

/// <summary>
/// Answers <see cref="IBrainNoteLinkWriter"/> over the Brain folder the person
/// picked (local ADR 0052): the link writer beside <see cref="BrainNoteWriter"/>,
/// which only creates notes.
/// <para>
/// A link is read and written as bytes: the file is decoded as strict UTF-8, its
/// byte order mark kept aside, edited by <see cref="BrainNoteLinks.Append"/>, and
/// encoded back the same way, so every byte the edit does not touch is the byte
/// that was read. A file that is not UTF-8 is refused rather than re-encoded. The
/// new text goes to a dot-file beside the note — a dot-folder or dot-file is never
/// indexed — and replaces the note in one move, so a write that fails leaves the
/// note as it was. A value already present writes nothing.
/// </para>
/// <para>
/// One link or move runs at a time: a second append to the same note — a plan
/// writing its task and its Roadmap Tag — waits for the first rather than reading
/// the file the first is about to replace. Just before the replace the note is
/// read again, and one that changed since — a save from the pane, a sync — is
/// refused rather than overwritten. That last check is best effort: a save that
/// lands between it and the replace itself is not seen, since the pane does not
/// take this writer's gate. Cancellation is thrown, never answered as a refusal,
/// and leaves the note as it was.
/// </para>
/// <para>
/// The archive move is one <see cref="File.Move(string, string, bool)"/> that never
/// overwrites: the file's bytes are not read, so they cannot change. It moves the
/// note into the area the conventions name the archive, keeping its path below the
/// area it leaves; a Brain without one, a note already there, or a name already
/// taken there is refused. Which note is a finished project's is the caller's to
/// decide: the move takes any note outside the archive area.
/// </para>
/// <para>
/// A note is named by its path relative to the Brain's root; one that leaves the
/// root, is not a <c>.md</c> file, or is not there is refused. After a write or a
/// move the source is told its content changed, as after a capture.
/// </para>
/// </summary>
public sealed class BrainNoteLinkWriter : IBrainNoteLinkWriter
{
    /// <summary>What a link to a note path that names no note in the Brain answers.</summary>
    public const string NotANote = "That is not a note in the Brain.";

    /// <summary>What an archive move in a Brain without an archive area answers.</summary>
    public const string NoArchive = "This Brain has no archive area.";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly byte[] ByteOrderMark = [0xEF, 0xBB, 0xBF];

    /// <summary>What no segment of a note's path may hold: anything no file name
    /// may, on any platform, and a colon, which on NTFS names a stream.</summary>
    private static readonly char[] UnsafeNameCharacters = [.. Path.GetInvalidFileNameChars().Union([':', '\\', '\0'])];

    /// <summary>What a note that changed while it was being linked answers, after its path.</summary>
    public const string ChangedMeanwhile = "changed while it was being linked, so it is not written. Link it again.";

    private readonly BrainFolderSource _brain;

    /// <summary>One write or move at a time in the process, however many writers
    /// it holds, so two never read the same file.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public BrainNoteLinkWriter(BrainFolderSource brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        _brain = brain;
    }

    public Task<BrainLinkWrite> AppendTaskAsync(string notePath, string taskId, CancellationToken cancellationToken = default) =>
        AppendAsync(notePath, BrainLinkField.Tasks, taskId, cancellationToken);

    public Task<BrainLinkWrite> AppendRoadmapTagAsync(string notePath, string roadmapTag, CancellationToken cancellationToken = default) =>
        AppendAsync(notePath, BrainLinkField.Roadmap, roadmapTag, cancellationToken);

    private async Task<BrainLinkWrite> AppendAsync(string notePath, BrainLinkField field, string value, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await AppendLockedAsync(notePath, field, value, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<BrainLinkWrite> AppendLockedAsync(string notePath, BrainLinkField field, string value, CancellationToken cancellationToken)
    {
        if (Locate(notePath) is not { } note) return Refusal(notePath);
        if (note.Error is { } error) return BrainLinkWrite.Refused(error);

        try
        {
            var bytes = await File.ReadAllBytesAsync(note.FullPath, cancellationToken).ConfigureAwait(false);
            var bom = bytes.AsSpan().StartsWith(ByteOrderMark);

            string text;
            try
            {
                text = StrictUtf8.GetString(bytes, bom ? ByteOrderMark.Length : 0, bytes.Length - (bom ? ByteOrderMark.Length : 0));
            }
            catch (DecoderFallbackException)
            {
                return BrainLinkWrite.Refused($"{note.RelativePath} is not UTF-8 text, so it is not written.");
            }

            var edit = BrainNoteLinks.Append(text, field, value);
            switch (edit.Outcome)
            {
                case BrainLinkOutcome.Refused:
                    return BrainLinkWrite.Refused($"{note.RelativePath} is not linked: {edit.Error}");
                case BrainLinkOutcome.AlreadyPresent:
                    return BrainLinkWrite.AlreadyPresent(note.RelativePath);
            }

            var body = StrictUtf8.GetBytes(edit.Text!);
            var written = bom ? [.. ByteOrderMark, .. body] : body;

            var temporary = Path.Combine(Path.GetDirectoryName(note.FullPath)!, "." + Path.GetFileName(note.FullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await File.WriteAllBytesAsync(temporary, written, cancellationToken).ConfigureAwait(false);

                var now = await File.ReadAllBytesAsync(note.FullPath, cancellationToken).ConfigureAwait(false);
                if (!now.AsSpan().SequenceEqual(bytes)) return BrainLinkWrite.Refused($"{note.RelativePath} {ChangedMeanwhile}");

                // Replace rather than move, so the note keeps its own creation time
                // and attributes and a sync client sees one file changed.
                File.Replace(temporary, note.FullPath, destinationBackupFileName: null);
            }
            finally
            {
                TryDelete(temporary);
            }

            _brain.NotifyContentChanged();
            return BrainLinkWrite.Appended(note.RelativePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return BrainLinkWrite.Refused($"Could not write {note.RelativePath}: {exception.Message}");
        }
    }

    public async Task<BrainLinkWrite> ArchiveAsync(string notePath, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return Archive(notePath);
        }
        finally
        {
            Gate.Release();
        }
    }

    private BrainLinkWrite Archive(string notePath)
    {
        if (Locate(notePath) is not { } note) return Refusal(notePath);
        if (note.Error is { } error) return BrainLinkWrite.Refused(error);

        var archive = _brain.Areas().FirstOrDefault(IsArchive);
        if (archive is null) return BrainLinkWrite.Refused(NoArchive);

        var segments = note.RelativePath.Split('/');
        if (string.Equals(segments[0], archive.Folder, StringComparison.OrdinalIgnoreCase))
        {
            return BrainLinkWrite.Refused($"{note.RelativePath} is in the archive already.");
        }

        var below = segments.Length > 1 ? segments[1..] : segments;
        var target = archive.Folder + "/" + string.Join('/', below);
        var targetPath = Path.Combine([note.Root, archive.Folder, .. below]);

        try
        {
            if (File.Exists(targetPath) || Directory.Exists(targetPath))
            {
                return BrainLinkWrite.Refused($"{target} is taken, so {note.RelativePath} is not moved.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Move(note.FullPath, targetPath, overwrite: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return BrainLinkWrite.Refused($"Could not move {note.RelativePath} into {archive.Folder}: {exception.Message}");
        }

        _brain.NotifyContentChanged();
        return BrainLinkWrite.Moved(target);
    }

    private static bool IsArchive(BrainArea area) =>
        string.Equals(area.Name.Trim(), "Archive", StringComparison.OrdinalIgnoreCase)
        || string.Equals(area.Folder, "archive", StringComparison.OrdinalIgnoreCase);

    private sealed record BrainLinkTarget(string Root, string RelativePath, string FullPath, string? Error);

    private static BrainLinkWrite Refusal(string? notePath) =>
        BrainLinkWrite.Refused(string.IsNullOrWhiteSpace(notePath) ? NotANote : $"{notePath} is not a note in the Brain.");

    /// <summary>The note <paramref name="notePath"/> names, inside the Brain's root:
    /// null when the path cannot be one, a <see cref="BrainLinkTarget.Error"/> when the Brain
    /// is not set up.</summary>
    private BrainLinkTarget? Locate(string? notePath)
    {
        var location = _brain.Resolve(BrainConventions.FolderKey);
        if (!location.Available || location.FullPath is not { } root)
        {
            return new BrainLinkTarget(string.Empty, string.Empty, string.Empty, location.Message ?? BrainFolderSource.NoBrainFolder);
        }

        if (string.IsNullOrWhiteSpace(notePath) || Path.IsPathRooted(notePath)) return null;

        var segments = notePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s.StartsWith('.') || s.IndexOfAny(UnsafeNameCharacters) >= 0)) return null;
        if (!segments[^1].EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return null;

        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine([fullRoot, .. segments]));
        var inside = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(inside, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath)) return null;

        return new BrainLinkTarget(fullRoot, string.Join('/', segments), fullPath, null);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A dot-file is never indexed; it is left rather than failing a write that landed.
        }
    }
}
