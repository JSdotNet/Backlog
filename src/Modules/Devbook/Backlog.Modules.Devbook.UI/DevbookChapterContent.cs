using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// What a panel hands the editing surface: the chapter, and the chapter as it is
/// on disk right now.
/// <para>
/// The two travel together because neither is any use without the other, and
/// because "there is no chapter here" and "the chapter is empty" must never be
/// the same state. A resolved ref with no text would open an editor on an empty
/// buffer, and the first debounce would write that emptiness over the file — so
/// a read that fails takes the ref down with it and the reader is offered no way
/// in, which is the same answer the resolver gives for a selection it cannot
/// place.
/// </para>
/// <para>
/// Loaded per selection rather than carried by the stores. Four of the five
/// areas parse their folder into a read model and none of them keeps the raw
/// markdown; widening them to hold it would keep every chapter of every area in
/// memory to serve the one on screen, so the file is read again here for the one
/// that is.
/// </para>
/// </summary>
internal sealed record DevbookChapterContent(DevbookChapterRef? Chapter, string? Text)
{
    /// <summary>Nothing selected, or nothing editable about what is. Panels hold
    /// this rather than two nullable fields so the pair cannot drift apart.</summary>
    internal static DevbookChapterContent None { get; } = new(null, null);

    /// <summary>Loads against a folder the devbook-folder port located.</summary>
    internal static Task<DevbookChapterContent> LoadAsync(
        string areaKey,
        DevbookFolderLocation? location,
        string? selection,
        CancellationToken cancellationToken = default) =>
        LoadAsync(DevbookChapterResolver.TryResolve(areaKey, location, selection), cancellationToken);

    /// <summary>Loads against a root a store already holds.
    /// <para>
    /// <paramref name="canEdit"/> is the store's answer to whether that root may be
    /// written to, and a caller that holds a read model holds it. Left to default
    /// here, a chapter read out of a branch snapshot arrived marked editable —
    /// the store had said read-only, the status control was hidden on that word,
    /// and the edit control beside it was offered anyway.
    /// </para></summary>
    internal static Task<DevbookChapterContent> LoadAsync(
        string areaKey,
        string? rootPath,
        string? selection,
        bool canEdit = true,
        CancellationToken cancellationToken = default) =>
        LoadAsync(DevbookChapterResolver.TryResolve(areaKey, rootPath, selection, canEdit: canEdit), cancellationToken);

    private static async Task<DevbookChapterContent> LoadAsync(DevbookChapterRef? chapter, CancellationToken cancellationToken)
    {
        if (chapter is null) return None;

        try
        {
            return new DevbookChapterContent(chapter, await File.ReadAllTextAsync(chapter.FullPath, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A chapter the panel can list and cannot read is a chapter nobody
            // should be invited to edit. The panel keeps rendering whatever it
            // parsed before the file became unreadable, which is the habit every
            // one of these panels already has.
            return None;
        }
    }
}
