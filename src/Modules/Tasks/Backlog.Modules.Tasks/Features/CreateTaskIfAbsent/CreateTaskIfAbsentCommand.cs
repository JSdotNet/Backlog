using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Features.CreateTaskIfAbsent;

/// <summary>
/// Creates a task from one block of entry text under <paramref name="Id"/>, a caller's id, only
/// when this machine holds no task with that id, live or deleted.
/// <para>
/// For a caller that derives the id from what the task is for, so that two machines asking for
/// the same task ask for the same id: each files it once, and the replica's whole-document latest
/// write folds the two copies into one. A task already held under the id is left as it is — a
/// deleted one stays deleted, because deleting it is how the person said no. Nothing here knows
/// what the id was derived from.
/// </para>
/// </summary>
public sealed record CreateTaskIfAbsentCommand(Guid Id, string RawText, int Order);

/// <summary>Answers <see langword="true"/> when this call created the task, and
/// <see langword="false"/> when a task with the id was already held, live or deleted, and nothing
/// was written. The text is refused on the terms an ordinary create refuses it.</summary>
public sealed class CreateTaskIfAbsentCommandHandler(ITaskRepository entries, IRepositoryDirectory repositories)
    : ICommandHandler<CreateTaskIfAbsentCommand, Result<bool>>
{
    /// <summary>The refusal of an ordinary create with no title, by the same code.</summary>
    public static readonly Error NeedsTitle = Error.Validation(
        "entry.needs_title",
        "An entry needs a title line before it can be saved.");

    /// <summary>The refusal of an ordinary create of a <c>plan</c> entry, by the same code: a
    /// roadmap item comes in through Import alone (ADR 0013 ruling 2).</summary>
    public static readonly Error PlanBelongsToImport = Error.Validation(
        TaskEntryErrorCodes.PlanBelongsToImport,
        "A `plan` entry is a roadmap item, not a task — bring it in through Import.");

    public async Task<Result<bool>> Handle(
        CreateTaskIfAbsentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var parsed = EntryTextParser.Parse(command.RawText);
        if (parsed.Kind == EntryKind.Plan) return PlanBelongsToImport;
        if (string.IsNullOrWhiteSpace(parsed.Title)) return NeedsTitle;

        // The read that sees a tombstone: a deleted task under the id is held, not absent.
        if (await entries.GetIncludingDeletedAsync(command.Id, cancellationToken).ConfigureAwait(false) is not null) return false;

        // Resolved and never registered, as an ordinary text save does it.
        if ((parsed.RepoIds?.Count ?? 0) > 0)
            parsed = parsed with { RepoIds = new RepositoryIdResolver(repositories).Resolve(parsed.RepoIds) };

        var entry = TaskEntryFields.CreateFrom(parsed, command.Order, id: command.Id);
        await entries.SaveAsync(entry, cancellationToken).ConfigureAwait(false);

        return true;
    }
}
