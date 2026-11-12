using Backlog.Infrastructure.Devbook;
using Backlog.Modules.Brain.Abstractions;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Brain;

/// <summary>
/// Tasks' <see cref="IDevbookReferenceResolver"/> with a task's Brain notes answered
/// from the owner's own Brain (local ADR 0053 §2 and §3).
/// <para>
/// The repository resolver answers every reference first and leaves each
/// <c>#brain:</c> one <see cref="DevbookReferenceState.Unverified"/>, never looking it
/// up in a repository's devbook. This decorator then reads the Brain's database once
/// for the list and answers those: <see cref="DevbookReferenceState.Page"/> or
/// <see cref="DevbookReferenceState.Chapter"/> titled as the Brain titles it, or
/// <see cref="DevbookReferenceState.UnknownPage"/> where this Brain holds no such
/// note. No Brain set, no database yet, or one that will not answer leaves them
/// unverified: nobody could look, which is how a reader who is not the Brain's owner
/// sees the reference.
/// </para>
/// <para>
/// The picker's targets are the repository's alone. A Brain note is typed, as an
/// address; offering the Brain in a list of a repository's chapters would mix the two
/// scopes ADR 0027 keeps apart.
/// </para>
/// </summary>
public sealed class BrainDevbookReferenceResolver : IDevbookReferenceResolver
{
    private readonly IDevbookReferenceResolver _repositories;
    private readonly Func<string?> _brainRoot;
    private readonly Func<string, string?> _databaseFor;

    /// <param name="repositories">The repository devbook's resolver.</param>
    /// <param name="brain">The Brain's folder, or null in a host that composes no
    /// Brain — every Brain note then stays unverified.</param>
    public BrainDevbookReferenceResolver(IDevbookReferenceResolver repositories, IBrainFolderSource? brain)
        : this(repositories, () => brain?.BrainRoot, BrainDatabaseLocation.ForBrainFolder)
    {
    }

    /// <param name="repositories">The repository devbook's resolver.</param>
    /// <param name="brainRoot">The Brain's folder as it stands, asked on every
    /// resolve so a moved setting is followed at once.</param>
    /// <param name="databaseFor">Where that folder's database is.</param>
    internal BrainDevbookReferenceResolver(
        IDevbookReferenceResolver repositories,
        Func<string?> brainRoot,
        Func<string, string?> databaseFor)
    {
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        _brainRoot = brainRoot ?? throw new ArgumentNullException(nameof(brainRoot));
        _databaseFor = databaseFor ?? throw new ArgumentNullException(nameof(databaseFor));
    }

    public async Task<IReadOnlyList<ResolvedDevbookReference>> ResolveAsync(
        string? repositoryAlias,
        IReadOnlyList<string> references,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(references);

        var answers = await _repositories.ResolveAsync(repositoryAlias, references, cancellationToken).ConfigureAwait(false);
        if (!answers.Any(answer => answer.IsBrainNote)) return answers;

        // An index with no note in it is one still being built, not a Brain that
        // lacks the note: nobody has looked yet, so the answers stay unverified.
        var notes = await Task.Run(ReadNotes, cancellationToken).ConfigureAwait(false);
        if (notes is not { Count: > 0 }) return answers;

        return [.. answers.Select(answer => answer.IsBrainNote ? FromBrain(answer, notes) : answer)];
    }

    public Task<IReadOnlyList<DevbookReferenceTarget>> ListTargetsAsync(
        string? repositoryAlias,
        CancellationToken cancellationToken = default) =>
        _repositories.ListTargetsAsync(repositoryAlias, cancellationToken);

    /// <summary>
    /// One Brain reference against the notes this Brain holds: the note as a whole by
    /// its path, or by its top heading's slug, which names the same note; a chapter by
    /// its slug, exactly first and then without regard to case. A path is matched
    /// without regard to case, as the file system the Brain lives on reads one.
    /// </summary>
    internal static ResolvedDevbookReference FromBrain(ResolvedDevbookReference answer, IReadOnlyList<RoadmapBrainNote> notes)
    {
        if (!BrainNoteAddress.TryParse(answer.Reference, out var address))
        {
            return ResolvedDevbookReference.BrainNote(answer.Reference, DevbookReferenceState.UnknownPage);
        }

        var inNote = notes.Where(note => string.Equals(note.Path, address.Path, StringComparison.OrdinalIgnoreCase)).ToList();
        var whole = inNote.FirstOrDefault(note => note.Slug is null);

        if (whole is null) return ResolvedDevbookReference.BrainNote(answer.Reference, DevbookReferenceState.UnknownPage);

        if (address.Slug is null || string.Equals(whole.TitleSlug, address.Slug, StringComparison.OrdinalIgnoreCase))
        {
            return ResolvedDevbookReference.BrainNote(answer.Reference, DevbookReferenceState.Page, whole.Title);
        }

        var chapter = inNote.FirstOrDefault(note => string.Equals(note.Slug, address.Slug, StringComparison.Ordinal))
            ?? inNote.FirstOrDefault(note => string.Equals(note.Slug, address.Slug, StringComparison.OrdinalIgnoreCase));

        return chapter is null
            ? ResolvedDevbookReference.BrainNote(answer.Reference, DevbookReferenceState.UnknownHeading, whole.Title)
            : ResolvedDevbookReference.BrainNote(answer.Reference, DevbookReferenceState.Chapter, chapter.Title);
    }

    private IReadOnlyList<RoadmapBrainNote>? ReadNotes()
    {
        if (_brainRoot() is not { Length: > 0 } root) return null;

        try
        {
            using var database = DevbookDatabase.TryOpen(_databaseFor(root));
            return database is null ? null : BrainRoadmapNoteReader.Notes(database);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            // Busy, replaced under the read, or not a database any more: the
            // references stay unverified rather than the task's chips failing.
            return null;
        }
    }
}
