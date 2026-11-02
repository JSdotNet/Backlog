using Backlog.Infrastructure.Devbook;

namespace Backlog.Infrastructure.Brain;

/// <summary>
/// Where the Brain's database is, and the record that the Brain folder is read
/// under <see cref="BrainCorpusProfile"/>: the Brain's side of Devbook's corpus
/// registry (<see cref="DevbookDatabaseLocation.RegisterCorpus"/>, local ADR 0040
/// §2). The database is keyed by the folder's absolute path like a repository's,
/// in the app's storage and never in the folder (local ADR 0026, ADR 0015).
/// </summary>
public static class BrainDatabaseLocation
{
    /// <summary>
    /// The Brain's database: the one for the Brain folder at
    /// <paramref name="brainRoot"/>. Records the folder as the Brain, so
    /// <see cref="DevbookDatabaseLocation.ForDevbookFolder"/> treats it as its own
    /// root and the refresher builds it under the Brain's profile, and tells the
    /// configured observer.
    /// </summary>
    public static string? ForBrainFolder(string? brainRoot)
    {
        RegisterBrainRoot(brainRoot);
        return DevbookDatabaseLocation.ForRepositoryRoot(brainRoot);
    }

    /// <summary>Records <paramref name="brainRoot"/> as the Brain folder, in place
    /// of any recorded before, without asking for its database; null or blank
    /// records none.</summary>
    public static void RegisterBrainRoot(string? brainRoot) =>
        DevbookDatabaseLocation.RegisterCorpus(BrainCorpusProfile.Instance, brainRoot);

    /// <summary>Whether <paramref name="path"/> is the folder recorded as the Brain.</summary>
    public static bool IsBrainRoot(string? path) =>
        ReferenceEquals(DevbookDatabaseLocation.ProfileFor(path), BrainCorpusProfile.Instance);
}
