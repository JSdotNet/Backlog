using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Features.ListPendingNotes;
using Backlog.Modules.Inbox.Features.ReceiveNote;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Inbox.Services;

/// <summary>
/// The published <see cref="IInboxNoteReplication"/> port over the slices behind
/// it. None of the handlers fails, so the <see cref="Result"/> is unwrapped here
/// and the caller sees the value alone, as <see cref="InboxIntake"/> does.
/// </summary>
internal sealed class InboxNoteReplication(
    ICommandHandler<ReceiveNoteCommand, Result<InboxIntakeOutcome>> receive,
    IQueryHandler<ListPendingNotesQuery, Result<IReadOnlyList<InboxNoteDto>>> pending,
    ICommandHandler<MarkNotesPushedCommand, Result> pushed)
    : IInboxNoteReplication
{
    public async Task<InboxIntakeOutcome> ReceiveAsync(InboxNoteDto note, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(note);

        var result = await receive.Handle(new ReceiveNoteCommand(note), cancellationToken).ConfigureAwait(false);

        return result.Value;
    }

    public async Task<IReadOnlyList<InboxNoteDto>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        var result = await pending.Handle(new ListPendingNotesQuery(), cancellationToken).ConfigureAwait(false);

        return result.Value;
    }

    public Task MarkPushedAsync(IReadOnlyList<InboxNoteDto> notes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notes);

        return pushed.Handle(new MarkNotesPushedCommand(notes), cancellationToken);
    }
}
