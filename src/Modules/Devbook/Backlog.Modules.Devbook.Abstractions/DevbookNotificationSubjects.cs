namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// How the Devbook context names a remark a notification can be about, and the reason it
/// closes with when the remark is resolved (<c>.devbook/domain/notifications/domain.md#subject</c>).
/// <para>
/// Plain strings, because this project may not see the Notifications context's
/// abstractions (<c>ModuleBoundaryTests</c>). The adapter that does — the remark store's
/// decorator in <c>Backlog.Infrastructure.Devbook</c> — builds the subject from these, and
/// so does whatever publishes about a remark.
/// </para>
/// </summary>
public static class DevbookNotificationSubjects
{
    /// <summary>The subject context a remark is named under.</summary>
    public const string Context = "devbook";

    /// <summary>A remark, by <see cref="DevbookAnnotation.Id"/> in the <c>D</c> format.</summary>
    public const string Remark = "remark";

    /// <summary>Why a notification about a remark closes: it was resolved, or deleted.</summary>
    public const string ResolvedReason = "Remark resolved";

    /// <summary>A remark's subject id.</summary>
    public static string Id(Guid remarkId) => remarkId.ToString("D");

    /// <summary>
    /// The deep link to the chapter a remark is on:
    /// <c>backlog://devbook/&lt;repository alias&gt;/&lt;chapter path&gt;</c>, each segment
    /// escaped and the path's slashes kept, so any channel that has a Devbook view can
    /// open the chapter.
    /// </summary>
    public static string ChapterLink(string? repositoryAlias, string chapterPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chapterPath);

        var alias = string.IsNullOrWhiteSpace(repositoryAlias) ? NoRepository :Uri.EscapeDataString(repositoryAlias.Trim());
        var path = string.Join('/', chapterPath.Trim().Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

        return $"{ChapterLinkPrefix}{alias}/{path}";
    }

    /// <summary>
    /// The repository alias and chapter path a <see cref="ChapterLink"/> names — the alias
    /// null where the link was built with none (<c>_</c>) — or null for a link that is not
    /// a chapter link. What a reader that holds only the notification uses to tell which
    /// repository a remark is about.
    /// </summary>
    public static (string? RepositoryAlias, string ChapterPath)? ParseChapterLink(string? link)
    {
        if (string.IsNullOrWhiteSpace(link) || !link.StartsWith(ChapterLinkPrefix, StringComparison.Ordinal)) return null;

        var rest = link[ChapterLinkPrefix.Length..];
        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0 || slash == rest.Length - 1) return null;

        var alias = rest[..slash];
        var path = string.Join('/', rest[(slash + 1)..].Split('/').Select(Uri.UnescapeDataString));

        return (alias == NoRepository ? null : Uri.UnescapeDataString(alias), path);
    }

    /// <summary>What every chapter link starts with.</summary>
    private const string ChapterLinkPrefix = "backlog://devbook/";

    /// <summary>The alias segment of a chapter link built with no repository.</summary>
    private const string NoRepository = "_";

    /// <summary>Whether a remark is still open: there, not deleted, and not resolved.</summary>
    public static bool IsOpen(DevbookAnnotation? remark) => remark is { IsLive: true, Resolved: false };
}
