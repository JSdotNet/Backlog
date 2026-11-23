using Backlog.Infrastructure.Devbook;
using Backlog.Modules.Brain.Abstractions;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Brain;

/// <summary>
/// <see cref="IBrainReviewDates"/> over the Brain's own database in the app's
/// storage (local ADR 0054 §6, ADR 0026 §4): the <c>review</c> values the builder
/// kept, as written, on each note's file node, read as dates here.
/// <para>
/// A value that is no date is a warning in the answer, never a failure — of the
/// build, which stores it, or of the question. Opened per question, as
/// <see cref="BrainTaskLinkReader"/> is, so a rebuild landing underneath is never
/// blocked; no Brain, no database, or one that will not answer all read as
/// <see cref="BrainReviewsDue.None"/>. Read-only: nothing here, nor any writer of
/// the Brain, writes the field.
/// </para>
/// </summary>
public sealed class BrainReviewDateReader : IBrainReviewDates
{
    private readonly Func<string?> _brainRoot;
    private readonly Func<string, string?> _databaseFor;

    public BrainReviewDateReader(IBrainFolderSource brain)
        : this(() => brain.BrainRoot)
    {
    }

    /// <param name="brainRoot">The Brain's folder as it stands, asked on every
    /// question so a moved setting is followed at once.</param>
    public BrainReviewDateReader(Func<string?> brainRoot)
        : this(brainRoot, BrainDatabaseLocation.ForBrainFolder)
    {
    }

    /// <param name="brainRoot">The Brain's folder as it stands.</param>
    /// <param name="databaseFor">Where that folder's database is.</param>
    internal BrainReviewDateReader(Func<string?> brainRoot, Func<string, string?> databaseFor)
    {
        _brainRoot = brainRoot ?? throw new ArgumentNullException(nameof(brainRoot));
        _databaseFor = databaseFor ?? throw new ArgumentNullException(nameof(databaseFor));
    }

    public Task<BrainReviewsDue> DueAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        if (_brainRoot() is not { Length: > 0 } root) return Task.FromResult(BrainReviewsDue.None);

        return Task.Run(
            () =>
            {
                try
                {
                    using var database = DevbookDatabase.TryOpen(_databaseFor(root));
                    return database is null ? BrainReviewsDue.None : Due(database, today);
                }
                catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
                {
                    // Busy, replaced under the read, or not a database any more: the
                    // review is not worth failing over the Brain's index.
                    return BrainReviewsDue.None;
                }
            },
            cancellationToken);
    }

    /// <summary>The answer over a database already open. The builder keeps one
    /// value per file, so each note comes once.</summary>
    internal static BrainReviewsDue Due(DevbookDatabase database, DateOnly today)
    {
        var due = new List<BrainReviewNote>();
        var unreadable = new List<BrainReviewWarning>();

        foreach (var row in database.ReviewDates())
        {
            var title = string.IsNullOrWhiteSpace(row.NoteTitle) ? Path.GetFileNameWithoutExtension(row.NotePath) : row.NoteTitle;
            if (BrainReviewDate.Parse(row.Value) is not { } date)
            {
                unreadable.Add(new BrainReviewWarning(row.NotePath, title, row.Value));
            }
            else if (date <= today)
            {
                due.Add(new BrainReviewNote(row.NotePath, title, date));
            }
        }

        return new BrainReviewsDue(
            [.. due
                .OrderBy(note => note.Review)
                .ThenBy(note => note.NotePath, StringComparer.OrdinalIgnoreCase)],
            unreadable);
    }
}
