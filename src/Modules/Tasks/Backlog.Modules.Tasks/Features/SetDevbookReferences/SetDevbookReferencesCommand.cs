using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Features.SetDevbookReferences;

/// <summary>
/// Points a task at the Devbook pages and chapters it is about, replacing the
/// whole list — an empty one clears it.
/// <para>
/// A use case of its own rather than a token in the text, because the references
/// are the task's own field: the text save neither writes nor clears them, so
/// without this the only way in would be Import. Existence is not checked here
/// either; see <see cref="TaskDevbookReference"/>.
/// </para>
/// </summary>
public sealed record SetDevbookReferencesCommand(Guid Id, IReadOnlyList<string> References);

public sealed class SetDevbookReferencesCommandHandler(ITaskRepository entries)
    : ICommandHandler<SetDevbookReferencesCommand, Result<TaskItemDto>>
{
    /// <summary>The code a refused reference fails with. Displayed rather than
    /// branched on, so it is not one of <c>TaskEntryErrorCodes</c>.</summary>
    public const string InvalidReferenceCode = "entry.invalid_devbook_reference";

    public static readonly Error NotFound = Error.NotFound(
        "entry.not_found",
        "That entry no longer exists.");

    public async Task<Result<TaskItemDto>> Handle(
        SetDevbookReferencesCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Read before the entry is loaded, so a list the aggregate would refuse
        // fails as a value naming what was wrong rather than as its exception.
        var references = command.References ?? [];
        foreach (var reference in references)
        {
            if (!TaskDevbookReference.TryParse(reference, out _)) return InvalidReference(reference);
        }

        var entry = await entries.GetAsync(command.Id, cancellationToken);
        if (entry is null) return NotFound;

        entry.SetDevbookReferences(references);

        await entries.SaveAsync(entry, cancellationToken);
        return entry.ToDto();
    }

    private static Error InvalidReference(string? reference) => Error.Validation(
        InvalidReferenceCode,
        $"`{reference}` is not a Devbook reference — write a page path with a folder or a .md ending, optionally followed by #anchor.");
}
