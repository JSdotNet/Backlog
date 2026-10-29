using System.Text;

using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Answers <see cref="IBrainNoteWriter"/> over the Brain folder the person picked
/// (local ADR 0026): a new Markdown file in the inbox area its conventions name,
/// with the text <see cref="BrainNoteText.Compose"/> makes.
/// <para>
/// The folder is resolved through <see cref="IBrainFolderSource"/> on every write,
/// so a Brain setting changed a moment ago is the one written to, and a Brain that
/// is unset or missing is refused in that source's own words. The Brain folder is
/// never created here — a missing one is a setting to fix, not a folder to guess —
/// but its inbox area is, the first time a note lands in it.
/// </para>
/// <para>
/// The file is created new, never opened over an existing one: a name already
/// taken gets <c>-2</c>, <c>-3</c> and on — except by a note routed from the
/// Inbox that finds one already holding exactly the text it would write: that is
/// the note an earlier route of the same item wrote, untouched since, so the route
/// answers it rather than writing a copy beside it
/// (<c>.devbook/domain/inbox/features.md#route-to-brain</c>). After a write the source is told its
/// content changed, so an open Devbook pane lists the note at once rather than
/// when the folder watch next rebuilds the database.
/// </para>
/// </summary>
public sealed class BrainNoteWriter : IBrainNoteWriter
{
    /// <summary>What a write without a title answers.</summary>
    public const string NoTitle = "A note needs a title.";

    /// <summary>How many numbered names are tried before giving up.</summary>
    private const int MaxAttempts = 1000;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IBrainFolderSource _brain;

    public BrainNoteWriter(IBrainFolderSource brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        _brain = brain;
    }

    public async Task<BrainNoteWrite> WriteAsync(BrainNoteDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (BrainNoteText.TitleOf(draft.Title) is not { } title) return BrainNoteWrite.Refused(NoTitle);

        var location = _brain.Resolve(BrainConventions.FolderKey);
        if (!location.Available || location.FullPath is not { } root)
        {
            return BrainNoteWrite.Refused(location.Message ?? BrainFolderSource.NoBrainFolder);
        }

        var area = BrainNoteText.InboxArea(_brain.Areas(), BrainConventions.ReadIgnored(root));
        var text = BrainNoteText.Compose(title, draft.BodyMd, draft.Source);
        var stem = BrainNoteText.FileStemFor(title);

        try
        {
            var folder = Path.Combine(root, area.Folder);
            Directory.CreateDirectory(folder);

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var name = (attempt == 1 ? stem : $"{stem}-{attempt}") + ".md";
                var path = Path.Combine(folder, name);

                // An item routed To Brain, undone and routed again finds the note
                // its first route wrote. Still exactly that text, nobody has touched
                // it since, and it is that note again rather than a -2 beside it.
                if (draft.Source == BrainNoteSource.Inbox && IsUnchanged(path, text)) return BrainNoteWrite.To(area.Folder + "/" + name);
                if (File.Exists(path) || Directory.Exists(path)) continue;

                FileStream stream;
                try
                {
                    stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
                }
                catch (IOException) when (File.Exists(path) || Directory.Exists(path))
                {
                    // Taken between the look and the create — by the folder's sync,
                    // or a second capture. The next name, as if it had been seen.
                    continue;
                }

                // Created, so the name is this note's: a write that fails from here
                // on takes its own file away rather than moving to the next name and
                // leaving an empty one behind.
                try
                {
                    await using (stream.ConfigureAwait(false))
                    {
                        await stream.WriteAsync(Utf8NoBom.GetBytes(text), cancellationToken).ConfigureAwait(false);
                    }
                }
                catch
                {
                    TryDelete(path);
                    throw;
                }

                _brain.NotifyContentChanged();
                return BrainNoteWrite.To(area.Folder + "/" + name);
            }

            return BrainNoteWrite.Refused($"Every name for \"{title}\" in {area.Folder} is taken.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return BrainNoteWrite.Refused($"Could not write the note into {area.Folder}: {exception.Message}");
        }
    }

    /// <summary>Whether a note is already at <paramref name="path"/> holding exactly
    /// <paramref name="text"/>, line endings aside — a sync client may have rewritten
    /// those and nothing else. A file that cannot be read is not that note.</summary>
    private static bool IsUnchanged(string path, string text)
    {
        try
        {
            if (!File.Exists(path)) return false;

            var existing = File.ReadAllText(path, Utf8NoBom).Replace("\r\n", "\n", StringComparison.Ordinal);
            return string.Equals(existing, text, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left for the person to see: the refusal says the write failed.
        }
    }
}
