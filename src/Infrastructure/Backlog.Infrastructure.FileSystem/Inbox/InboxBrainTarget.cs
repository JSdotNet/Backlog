using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.FileSystem.Inbox;

/// <summary>
/// Answers the Inbox's <see cref="IInboxBrainTarget"/> port over Devbook's
/// <see cref="IBrainNoteWriter"/>: an item routed To Brain becomes a note in the
/// Brain's inbox area, through the same writer the Devbook pane's Capture uses,
/// so the two write the same kind of note under the same guard on the text
/// (<c>.devbook/domain/brain/features.md#capture-into-the-brain</c>).
/// <para>
/// The note keeps the item's title and body. A capture that came from a link
/// keeps the link too — as the body's last line, unless the body already holds
/// it — because a saved article whose note lost its address has lost the article.
/// Its source is <see cref="BrainNoteSource.Inbox"/>.
/// </para>
/// </summary>
public sealed class InboxBrainTarget(IBrainNoteWriter writer, IBrainFolderSource? brain = null) : IInboxBrainTarget
{
    /// <summary>The Brain folder's own refusal while it cannot be resolved — the
    /// words the writer would refuse in — else null. Without a folder source to ask,
    /// the writer is the only judge, and a route is offered.</summary>
    public string? UnavailableReason =>
        brain?.Resolve(BrainConventions.FolderKey) is { Available: false } location
            ? location.Message ?? BrainFolderSource.NoBrainFolder
            : null;

    public async Task<Result<string>> WriteNoteAsync(InboxBrainNoteRequestDto request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var written = await writer
            .WriteAsync(new BrainNoteDraft(request.Title, BodyOf(request), BrainNoteSource.Inbox), cancellationToken)
            .ConfigureAwait(false);

        return written.Written
            ? Result.Success(written.RelativePath!)
            : Result.Failure<string>(InboxErrors.BrainUnavailable(written.Error!));
    }

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

/// <summary>The target of a host composed without a Brain: every route is
/// refused in words, and nothing is written.</summary>
internal sealed class NoBrainTarget : IInboxBrainTarget
{
    public Task<Result<string>> WriteNoteAsync(InboxBrainNoteRequestDto request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<string>(InboxErrors.BrainUnavailable(InboxErrors.NoBrainHere)));

    public string? UnavailableReason => InboxErrors.NoBrainHere;
}
