using Backlog.Modules.Devbook.Abstractions;
using Microsoft.AspNetCore.Components;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The sync verdict badges a Devbook pane hangs beside each chapter heading of the
/// document it shows, as the callback <c>FileView.ChapterAdornment</c> takes.
/// <para>
/// Read once per document rather than once per heading: the verdicts of one file are
/// fetched when the callback is built, grouped by anchor, and each heading looks its
/// own up by the anchor the devbook's tools give its title. A verdict filed against the
/// file itself — a unit rooted at a whole file — is drawn at the document's top-level
/// heading, the one a reader takes for the file's title.
/// </para>
/// <para>
/// Null wherever there is nothing to draw — no verdict store composed, no repository
/// the alias resolves to, no verdict for the file — so a pane passing it changes
/// nothing until a sweep has spoken about what it shows.
/// </para>
/// </summary>
internal static class DevbookSyncAdornment
{
    internal static Func<string, int, RenderFragment?>? For(
        IDevbookSyncVerdicts? store,
        IDevbookFolderSource? folders,
        string? repositoryAlias,
        string? documentPath)
    {
        if (store is null || folders is null || string.IsNullOrWhiteSpace(documentPath)) return null;

        var repository = folders.Resolve(".arc42", repositoryAlias).RepositoryFullName;

        if (string.IsNullOrWhiteSpace(repository)) return null;

        var verdicts = store.For(repository, documentPath, folders.Folders(repositoryAlias));

        if (verdicts.Count == 0) return null;

        var byAnchor = verdicts
            .GroupBy(verdict => verdict.Anchor, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<DevbookSyncVerdict>)[.. group.OrderByDescending(verdict => verdict.RecordedAt)],
                StringComparer.OrdinalIgnoreCase);

        return (title, level) =>
        {
            var anchor = DevbookSyncChapters.AnchorOf(title);

            if (!byAnchor.TryGetValue(anchor, out var own) && !(level == 1 && byAnchor.TryGetValue(string.Empty, out own)))
            {
                return null;
            }

            return builder =>
            {
                builder.OpenComponent<DevbookSyncVerdictBadges>(0);
                builder.AddComponentParameter(1, nameof(DevbookSyncVerdictBadges.Verdicts), own);
                builder.CloseComponent();
            };
        };
    }
}
