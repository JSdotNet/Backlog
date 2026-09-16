using Backlog.Modules.Devbook.Abstractions;
using Backlog.UI.Components.Markdown;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// Between the annotation store and the read view: a stored remark as the
/// <see cref="MarkdownComment"/> the view draws, and back to the id the store
/// knows it by.
/// <para>
/// Both panels that show remarks map them the same way, and this is the one
/// place they do. The author is a machine's label — <see cref="Environment.MachineName"/>
/// on the device that wrote it — and reads as "You" when it is this machine's,
/// because a person's own remark on their own device is theirs, and as the
/// other machine's name when it came from there, which is what tells a reader
/// where a remark was left. The timestamp is formatted here rather than by the
/// view, which is the division <see cref="MarkdownComment"/> asks for.
/// </para>
/// </summary>
public static class DevbookAnnotationComments
{
    /// <summary>What a remark made on this machine is signed as, and what
    /// <see cref="Comments"/> reads back as "You".</summary>
    public static string LocalAuthor => Environment.MachineName;

    /// <summary>The read view's comments for one chapter's remarks.</summary>
    public static IReadOnlyList<MarkdownComment> Comments(IReadOnlyList<DevbookAnnotation> annotations, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(annotations);

        var now = (time ?? TimeProvider.System).GetUtcNow();

        return [.. annotations.Select(annotation => Comment(annotation, now))];
    }

    /// <summary>The store's id behind a comment's, or null for an id this
    /// mapping did not make.</summary>
    public static Guid? AnnotationId(string commentId) =>
        Guid.TryParseExact(commentId, "D", out var id) ? id : null;

    private static MarkdownComment Comment(DevbookAnnotation annotation, DateTimeOffset now) => new(
        annotation.Id.ToString("D"),
        annotation.BlockIndex,
        annotation.Body,
        string.Equals(annotation.Author, LocalAuthor, StringComparison.OrdinalIgnoreCase) ? "You" : annotation.Author,
        Ago(annotation.CreatedAt, now),
        annotation.Resolved);

    /// <summary>"just now" up to a minute, then minutes, hours and days, then
    /// the date — coarse on purpose, because a remark's age is context rather
    /// than data and a figure that ticked would draw the eye to nothing.</summary>
    internal static string Ago(DateTimeOffset at, DateTimeOffset now)
    {
        var elapsed = now - at;

        if (elapsed < TimeSpan.FromMinutes(1)) return "just now";
        if (elapsed < TimeSpan.FromHours(1)) return Plural((int)elapsed.TotalMinutes, "minute");
        if (elapsed < TimeSpan.FromDays(1)) return Plural((int)elapsed.TotalHours, "hour");
        if (elapsed < TimeSpan.FromDays(30)) return Plural((int)elapsed.TotalDays, "day");

        return at.ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Plural(int count, string unit) =>
        count == 1 ? $"1 {unit} ago" : $"{count} {unit}s ago";
}
