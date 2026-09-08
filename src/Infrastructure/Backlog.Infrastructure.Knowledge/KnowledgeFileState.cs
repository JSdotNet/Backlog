using System.Globalization;
using System.Security.Cryptography;

namespace Backlog.Infrastructure.Knowledge;

/// <summary>
/// The file-level facts the writer recorded for one Markdown file, and the drift
/// check they exist for.
///
/// <para>This is the second rung of ADR 0004's ladder. A generated database is
/// allowed to lag the Markdown beside it — refresh is an optimisation, never a
/// precondition — so before a panel serves a row it asks whether the file behind
/// that row still looks the way it did when the row was written. It does not, the
/// panel reads that one file's Markdown and serves that: correct content, one
/// file's parse, no re-read of the corpus.</para>
///
/// <para>Cheapest question first. <see cref="Size"/> and <see cref="Mtime"/> come
/// from one <c>stat</c>, and only when one of them disagrees is
/// <see cref="SourceHash"/> earned by reading the file — hashing every file on
/// every open would be exactly the corpus re-read this decision exists to avoid.
/// The hash is what keeps the cheap answer honest: a file touched by a branch
/// switch, or restored to its old content, changes its modification time without
/// changing a word, and re-parsing it would be a correct answer paid for
/// twice.</para>
/// </summary>
/// <param name="Path">Repository-relative, <c>/</c>-separated, as the writer
/// spells it.</param>
/// <param name="Size">Bytes, as the writer read them.</param>
/// <param name="Mtime">Milliseconds since the Unix epoch.</param>
/// <param name="SourceHash">Lowercase hex SHA-256 of the file's bytes.</param>
public sealed record KnowledgeFileState(string Path, long Size, long Mtime, string SourceHash)
{
    /// <summary>
    /// Whether the file at <paramref name="fullPath"/> has moved on from what this
    /// row records.
    ///
    /// <para>A file that is no longer on disk is not drifted, it is gone, and the
    /// caller answers that question separately — reporting it as drift would send
    /// a panel off to parse a Markdown file that is not there.</para>
    ///
    /// <para>Anything that goes wrong while asking counts as drift. An unreadable
    /// file makes the database's row unverifiable, and serving an unverifiable row
    /// is the one thing the ladder never does.</para>
    /// </summary>
    public bool HasDrifted(string fullPath)
    {
        try
        {
            var file = new FileInfo(fullPath);
            if (!file.Exists) return false;

            if (file.Length != Size) return true;
            if (UnixMilliseconds(file.LastWriteTimeUtc) != Mtime) return HashDiffers(fullPath);

            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// Whether the file at <paramref name="fullPath"/> still looks the way this
    /// row records it, asked from the <c>stat</c> alone.
    ///
    /// <para><see cref="HasDrifted"/> without the hash, for the one caller that
    /// must not read a Markdown file to answer: the knowledge menu's rail. A file
    /// whose modification time moved without its length changing is reported as
    /// stale here, where <see cref="HasDrifted"/> would hash it and often find
    /// nothing changed. So this over-reports drift and never under-reports it —
    /// the same trade the JSON rung makes, and the caller pays for it in a
    /// generated title it does not adopt rather than in a wrong one it does.</para>
    /// </summary>
    public bool LooksStale(string fullPath)
    {
        try
        {
            var file = new FileInfo(fullPath);
            if (!file.Exists) return false;

            return file.Length != Size || UnixMilliseconds(file.LastWriteTimeUtc) != Mtime;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// A file's modification time in the units the writer recorded, rounded the
    /// same way it rounds.
    /// <para>
    /// Both sides read the same filesystem timestamp and both reduce it to whole
    /// milliseconds, so the rounding has to match or every comparison disagrees by
    /// a millisecond and every file gets hashed. Node's <c>Math.round</c> is
    /// half-up, which for a positive epoch offset is
    /// <see cref="MidpointRounding.AwayFromZero"/>.
    /// </para>
    /// </summary>
    internal static long UnixMilliseconds(DateTime lastWriteUtc)
    {
        var ticks = new DateTimeOffset(DateTime.SpecifyKind(lastWriteUtc, DateTimeKind.Utc)).UtcTicks
            - DateTimeOffset.UnixEpoch.UtcTicks;

        return (long)Math.Round((double)ticks / TimeSpan.TicksPerMillisecond, MidpointRounding.AwayFromZero);
    }

    private bool HashDiffers(string fullPath)
    {
        using var stream = File.OpenRead(fullPath);
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));

        return !string.Equals(hash, SourceHash, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Which retrieval tier a repository has, which is the one place ADR 0004's
/// ladder reaches the user rather than degrading quietly behind them.
///
/// <para>Browsing degrades to Markdown because browsing touches the handful of
/// files on screen. Search cannot: scanning the corpus per query is not a
/// fallback, it is a hang. So a repository with no database has no search, and
/// the surface that offers it has to say so — see
/// <see cref="KnowledgeRetrieval.UnavailableMessage"/>.</para>
/// </summary>
public enum KnowledgeRetrievalTier
{
    /// <summary>No database, so no index to search. Named, not empty.</summary>
    Unavailable,

    /// <summary>A database with no embeddings in it: full-text search answers,
    /// and search by meaning is absent rather than broken. This is the expected
    /// state — the semantic tier is optional and needs a model.</summary>
    FullText,

    /// <summary>A database carrying vectors as well, so both tiers answer.</summary>
    FullTextAndSemantic
}

/// <summary>What a retrieval surface says about the tier it has.</summary>
public static class KnowledgeRetrieval
{
    /// <summary>The command that builds the database, named wherever its absence
    /// is reported so the answer to "search is unavailable" is one line away.</summary>
    public const string BuildCommand = "node tools/knowledge/build-database.mjs";

    /// <summary>
    /// The tier available from an open database, or
    /// <see cref="KnowledgeRetrievalTier.Unavailable"/> when there is none.
    /// </summary>
    public static KnowledgeRetrievalTier TierFor(KnowledgeDatabase? database) =>
        database is null
            ? KnowledgeRetrievalTier.Unavailable
            : database.HasEmbeddings
                ? KnowledgeRetrievalTier.FullTextAndSemantic
                : KnowledgeRetrievalTier.FullText;

    /// <summary>
    /// The words a surface shows for <see cref="KnowledgeRetrievalTier.Unavailable"/>.
    /// Modelled on the atlas's "not generated yet" message, which is the one place
    /// this repository already tells a reader that a generated artifact is missing
    /// rather than showing them an empty panel.
    /// </summary>
    public static string UnavailableMessage(string surface) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0} needs the generated knowledge index, which has not been written yet. Run {1} to build it.",
            surface,
            BuildCommand);
}
