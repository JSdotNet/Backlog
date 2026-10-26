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

        var alias = string.IsNullOrWhiteSpace(repositoryAlias) ? "_" : Uri.EscapeDataString(repositoryAlias.Trim());
        var path = string.Join('/', chapterPath.Trim().Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

        return $"backlog://devbook/{alias}/{path}";
    }

    /// <summary>Whether a remark is still open: there, not deleted, and not resolved.</summary>
    public static bool IsOpen(DevbookAnnotation? remark) => remark is { IsLive: true, Resolved: false };
}
