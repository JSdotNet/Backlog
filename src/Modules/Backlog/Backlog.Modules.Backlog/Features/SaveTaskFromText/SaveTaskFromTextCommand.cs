using Backlog.Modules.Backlog.Abstractions;
using Backlog.Modules.Backlog.Abstractions.DataTransferObjects;
using Backlog.Modules.Backlog.Abstractions.Services;
using Backlog.Modules.Backlog.DomainModels;
using Backlog.Modules.Backlog.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Backlog.Features.SaveTaskFromText;

/// <summary>
/// Writes one block of entry markdown down as an entry — creating it when
/// <paramref name="Id"/> is null, updating it otherwise.
/// <para>
/// The whole editing model of this product is that an entry <em>is</em> its
/// text, so this is the use case behind nearly every keystroke. It is the module
/// that reads that text, not the editor: the format is the context's published
/// language and the rules for what a token means belong with the aggregate that
/// has to honour them.
/// </para>
/// </summary>
public sealed record SaveTaskFromTextCommand(Guid? Id, string RawText, int Order);

public sealed class SaveTaskFromTextCommandHandler(ITaskRepository entries, IRepositoryDirectory repositories)
    : ICommandHandler<SaveTaskFromTextCommand, Result<SavedTaskDto>>
{
    /// <summary>An entry needs a title before it can exist. Somebody halfway
    /// through typing one has not failed at anything, so this is an ordinary
    /// outcome the editor holds on to rather than an error it reports.</summary>
    public static readonly Error NeedsTitle = Error.Validation(
        "entry.needs_title",
        "An entry needs a title line before it can be saved.");

    public static readonly Error NotFound = Error.NotFound(
        "entry.not_found",
        "That entry no longer exists.");

    public async Task<Result<SavedTaskDto>> Handle(
        SaveTaskFromTextCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var parsed = ResolveRepos(EntryTextParser.Parse(command.RawText));

        return command.Id is { } id
            ? await UpdateAsync(id, parsed, cancellationToken)
            : await CreateAsync(parsed, command.Order, cancellationToken);
    }

    /// <summary>
    /// Replaces the repository names the text wrote with the ids the registry
    /// says they are, before any of them reaches the aggregate.
    /// <para>
    /// Here rather than in <see cref="TaskEntryFields"/> or the parser, because
    /// this is the one step that needs to ask something outside the text. The
    /// parser may not see a registry (ADR 0002) and the field applier is about
    /// what a parsed value means to an entry, not about what a name means to the
    /// workspace — so the value that arrives at <c>SetRepoIds</c> is already
    /// canonical and that line stays exactly as it was.
    /// </para>
    /// <para>
    /// This path never registers. That is the guard that keeps ADR 0004's
    /// sentence true: Import triggers registration, and a `repo:` token somebody
    /// typed does not. A name the registry has never seen is stored as it was
    /// typed and reads as "No repo" until somebody configures it.
    /// </para>
    /// </summary>
    private EntryTextParser.ParsedEntry ResolveRepos(EntryTextParser.ParsedEntry parsed)
    {
        if ((parsed.RepoIds?.Count ?? 0) == 0) return parsed;

        return parsed with { RepoIds = new RepositoryIdResolver(repositories).Resolve(parsed.RepoIds) };
    }

    private async Task<Result<SavedTaskDto>> CreateAsync(
        EntryTextParser.ParsedEntry parsed,
        int order,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(parsed.Title)) return NeedsTitle;

        var entry = TaskEntryFields.CreateFrom(parsed, order);

        await entries.SaveAsync(entry, cancellationToken);

        // Deliberately no successor here, even for an entry typed straight in as
        // `!done` with a repeat on it. Spawning is what happens when a save
        // *completes* an occurrence, and a create has no previous state for the
        // save to have moved it from — an entry arriving already finished is a
        // record of something done, not an occurrence just now finishing.
        return new SavedTaskDto(entry.ToDto());
    }

    private async Task<Result<SavedTaskDto>> UpdateAsync(
        Guid id,
        EntryTextParser.ParsedEntry parsed,
        CancellationToken cancellationToken)
    {
        var entry = await entries.GetAsync(id, cancellationToken);
        if (entry is null) return NotFound;

        // Read before anything is applied, because "this save completed the
        // entry" is a statement about the step from one status to another. An
        // entry that was already Done stays Done and spawns nothing: without this
        // the next keystroke on a finished repeating entry would spawn a second
        // successor, and the one after that a third.
        var wasDone = entry.Status is EntryStatus.Done;

        TaskEntryFields.ApplyToExisting(entry, parsed);

        if (parsed.Status is { } targetStatus) entry.SetStatus(targetStatus);

        await entries.SaveAsync(entry, cancellationToken);

        if (!wasDone && entry.Status is EntryStatus.Done && entry.Recurrence is not null)
        {
            // Saved after the completed occurrence, so a failure here cannot lose
            // the completion that has already been recorded.
            var successor = RecurrencePolicy.NextOccurrence(entry);
            await entries.SaveAsync(successor, cancellationToken);

            // Named in the result rather than left for the caller to notice. A
            // list that only ever refreshes the row it saved has no other way to
            // learn that a second entry now exists.
            return new SavedTaskDto(entry.ToDto(), successor.Id);
        }

        return new SavedTaskDto(entry.ToDto());
    }
}
