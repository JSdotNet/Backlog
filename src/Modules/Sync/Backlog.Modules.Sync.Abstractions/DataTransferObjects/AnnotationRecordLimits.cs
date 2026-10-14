namespace Backlog.Modules.Sync.Abstractions.DataTransferObjects;

/// <summary>
/// What an <see cref="AnnotationChange"/> must be for the service to store it:
/// how long each field may be, and <see cref="WhyRefused"/>, the one rule both
/// ends run.
/// <para>
/// Shared between the two ends on <see cref="SessionRecordLimits"/>'s precedent:
/// the service refuses a push whole when one remark in it breaks a rule, so the
/// desktop leaves such a remark out before it batches, and two copies that drifted
/// apart would have a remark the desktop thinks fits refuse every batch it is in,
/// on every cycle.
/// </para>
/// </summary>
public static class AnnotationRecordLimits
{
    /// <summary>The longest repository alias a remark may carry. An alias, never
    /// a path (.devbook/arc42/adr/0005 §Session records), so a short name rather
    /// than something that grows with a directory tree.</summary>
    public const int RepositoryAliasLength = 200;

    /// <summary>The longest chapter path. Repository-relative, never absolute, so
    /// this is generous for any devbook folder and far short of anything that
    /// would trouble the store.</summary>
    public const int ChapterPathLength = 1_024;

    /// <summary>The longest author label — a machine name or a person's name.</summary>
    public const int AuthorLength = 255;

    /// <summary>The longest body. Generous for a review note of several
    /// paragraphs, and nowhere near the two-megabyte document ceiling that is the
    /// failure this keeps off the store.</summary>
    public const int BodyLength = 8_000;

    /// <summary>The longest anchor digest. The desktop writes eight hex
    /// characters; the bound is loose enough that lengthening the digest needs no
    /// service deploy first, and tight enough that the field carries nothing
    /// else.</summary>
    public const int BlockHashLength = 128;

    /// <summary>
    /// Why the service would refuse <paramref name="change"/>, or null when it
    /// would store it. The service answers the reason as the detail of its
    /// <see cref="SyncErrorCodes.AnnotationInvalid"/> refusal; the desktop names
    /// it in the warning that says it left the remark out.
    /// <para>
    /// A remark with no repository alias or no chapter path could not be placed
    /// on any device, and a negative block index anchors to nothing. The body may
    /// be empty — a resolved remark can have had its text cleared, and a
    /// tombstone carries none — and the bounds are what stop a caller posting a
    /// megabyte into durable, per-request-billed storage.
    /// </para>
    /// </summary>
    public static string? WhyRefused(AnnotationChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        // Null only when a caller's JSON left it out; the type says otherwise.
        if (change.Annotation is null)
        {
            return "An annotation change needs the annotation it changes.";
        }

        var annotation = change.Annotation;

        if (string.IsNullOrWhiteSpace(annotation.RepositoryAlias))
        {
            return "An annotation needs the alias of the repository its chapter is in.";
        }

        if (string.IsNullOrWhiteSpace(annotation.ChapterPath))
        {
            return "An annotation needs the path of the chapter it is on.";
        }

        if (annotation.BlockIndex < 0)
        {
            return "An annotation's block index cannot be negative.";
        }

        return TooLong("repository alias", annotation.RepositoryAlias, RepositoryAliasLength)
            ?? TooLong("chapter path", annotation.ChapterPath, ChapterPathLength)
            ?? TooLong("author", annotation.Author, AuthorLength)
            ?? TooLong("body", annotation.Body, BodyLength)
            ?? TooLong("block hash", annotation.BlockHash, BlockHashLength);
    }

    private static string? TooLong(string field, string? value, int limit) =>
        value is not null && value.Length > limit
            ? $"An annotation's {field} may be at most {limit} characters; this one was {value.Length}."
            : null;
}
