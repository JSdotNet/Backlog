using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Tasks.Features.DeleteTask;

/// <summary>Takes an entry out of the backlog. Deliberate and explicit — the app
/// asks before it gets here.
/// <para>
/// "For good" as far as anybody using the app is concerned, but not by removing
/// the row: the entry is tombstoned, because a row that is simply gone from this
/// machine is indistinguishable from one the person's other machine has never
/// seen, and the deletion would be undone by the next reconciliation. Every read
/// hides it from the moment this runs.
/// </para></summary>
public sealed record DeleteTaskCommand(Guid Id);

public sealed class DeleteTaskCommandHandler(ITaskRepository entries)
    : ICommandHandler<DeleteTaskCommand>
{
    public async Task Handle(DeleteTaskCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var entry = await entries.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);

        // An entry that is not there is success, not a failure to report. Deleting
        // something already gone is the caller's intent either way, and this runs
        // behind a confirmation the person has already given — so there is nothing
        // for them to do about a "no longer exists". Said out loud because a bare
        // early return is otherwise indistinguishable from a swallowed bug, and
        // because the two neighbouring handlers that load-then-save do return
        // Error.NotFound here; they differ in that their caller still has an edit
        // to place somewhere.
        //
        // Note this is also the second-delete path: GetAsync hides a tombstone, so
        // deleting an already-deleted entry lands here rather than restamping it.
        if (entry is null) return;

        // Stamped by the aggregate rather than by the adapter. When the same task
        // is edited on two machines the later edit wins whole, and "later" is a
        // question only the task can answer — so the instant is a domain decision,
        // not a persistence detail.
        entry.MarkDeleted();

        await entries.SaveAsync(entry, cancellationToken).ConfigureAwait(false);
    }
}
