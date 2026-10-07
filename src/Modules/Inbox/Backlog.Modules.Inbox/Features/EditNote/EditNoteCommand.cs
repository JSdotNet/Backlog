using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.DomainModels;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Features.EditNote;

/// <summary>Changes a note's title and body on the desktop
/// (<c>.devbook/domain/inbox/features.md#notes-on-the-desktop</c>). The edit is
/// stamped now and reaches the phone on the next push.</summary>
public sealed record EditNoteCommand(Guid Id, string Title, string? BodyMd);

public sealed class EditNoteCommandHandler(IInboxItemRepository items, TimeProvider clock)
    : ICommandHandler<EditNoteCommand, Result>
{
    public async Task<Result> Handle(EditNoteCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Title)) return Result.Failure(InboxErrors.ItemNeedsTitle);

        var item = await items.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (item is null) return Result.Failure(InboxErrors.ItemNotFound);
        if (!item.IsNote) return Result.Failure(InboxErrors.NotANote);

        try
        {
            item.EditNote(command.Title, command.BodyMd, clock.GetUtcNow());
        }
        catch (InvalidInboxTransitionException refused)
        {
            return Result.Failure(InboxErrors.InvalidTransition(refused.Message));
        }

        await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
