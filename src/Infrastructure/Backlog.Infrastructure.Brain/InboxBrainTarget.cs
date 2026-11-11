using Backlog.Modules.Brain.Abstractions;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Brain;

/// <summary>
/// Answers the Inbox's <see cref="IInboxBrainTarget"/> port over the Brain's
/// <see cref="IBrainNoteWriter"/>: an item routed To Brain becomes a note in the
/// Brain's inbox area, or the area or project its picker named, through the same writer the Devbook pane's Capture uses,
/// so the two write the same kind of note under the same guard on the text
/// (<c>.devbook/domain/brain/features.md#capture-into-the-brain</c>).
/// <para>
/// The note keeps the item's title and body. A capture that came from a link
/// keeps the link too — as the body's last line, unless the body already holds
/// it — because a saved article whose note lost its address has lost the article.
/// Its source is <see cref="BrainNoteSource.Inbox"/>.
/// </para>
/// </summary>
public sealed class InboxBrainTarget(IBrainNoteWriter writer, IDevbookPersonalSource? brain = null) : IInboxBrainTarget
{
    /// <summary>The Brain folder's own refusal while it cannot be resolved — the
    /// words the writer would refuse in — else null. Without a folder source to ask,
    /// the writer is the only judge, and a route is offered.</summary>
    public string? UnavailableReason =>
        brain?.Resolve(BrainConventions.FolderKey) is { Available: false } location
            ? location.Message ?? BrainFolderSource.NoBrainFolder
            : null;

    public async Task<Result<InboxBrainNoteWrittenDto>> WriteNoteAsync(InboxBrainNoteRequestDto request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var written = await writer
            .WriteAsync(new BrainNoteDraft(request.Title, BodyOf(request), BrainNoteSource.Inbox, request.Folder), cancellationToken)
            .ConfigureAwait(false);

        return written.Written
            ? Result.Success(new InboxBrainNoteWrittenDto(written.RelativePath!, written.Area ?? AreaOf(written.RelativePath!)))
            : Result.Failure<InboxBrainNoteWrittenDto>(InboxErrors.BrainUnavailable(written.Error!));
    }

    public async Task<IReadOnlyList<InboxBrainDestinationDto>> ListDestinationsAsync(CancellationToken cancellationToken = default)
    {
        if (UnavailableReason is not null) return [];

        var destinations = await writer.ListDestinationsAsync(cancellationToken).ConfigureAwait(false);
        return [.. destinations.Select(destination => new InboxBrainDestinationDto(destination.Folder, destination.Label, destination.IsInbox, destination.IsProject))];
    }

    /// <summary>A writer that did not name the area: the note's top folder, as a
    /// tab is named after a discovered one.</summary>
    private static string AreaOf(string relativePath) => BrainConventions.NameFor(relativePath.Split('/')[0]);

    /// <summary>The note's body: the item's, with its source link as the last line
    /// when it has one the body does not already carry.</summary>
    internal static string BodyOf(InboxBrainNoteRequestDto request)
    {
        var body = (request.BodyMd ?? string.Empty).TrimEnd();
        if (request.SourceUrl is not { Length: > 0 } link || body.Contains(link, StringComparison.Ordinal)) return body;

        // An autolink ends at a space or an angle bracket, so those are written
        // percent-encoded, as a browser would send them.
        var source = $"Source: <{link.Replace(" ", "%20", StringComparison.Ordinal).Replace("<", "%3C", StringComparison.Ordinal).Replace(">", "%3E", StringComparison.Ordinal)}>";
        return body.Length == 0 ? source : body + "\n\n" + source;
    }
}
