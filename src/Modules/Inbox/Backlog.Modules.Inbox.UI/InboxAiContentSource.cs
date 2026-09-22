using System.Text;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Ai;

namespace Backlog.Desktop.UI.Inbox;

/// <summary>
/// The Inbox's answer to <see cref="IAiContentSource"/>: everything captured and
/// not yet dismissed, whichever list it was filed in.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="InboxDesktopState.Items"/> and not the visible rows. The side menu
/// picks a slice and the chips pick a kind, and both are the reader arranging
/// the screen rather than changing what was captured — the question "what did I
/// capture about the sync service" is answered from the whole inbox whether or
/// not the reader is looking at the unfiled slice. Archived items are the one
/// exception, and they are content rather than screen: archived is the terminal
/// state, the reader's own decision that the item is over, and the state's own
/// slices leave them out for the same reason.
/// </para>
/// <para>
/// A record is the item's title, its kind, its tags and where it came from, then
/// the captured body — the same facts the detail pane shows, in the order a
/// reader scans them. The selected item is pinned, not sent alone: the question
/// is likeliest to be about it, and the rest of the inbox is what "is there
/// anything else like this" needs.
/// </para>
/// </remarks>
internal sealed class InboxAiContentSource(InboxDesktopState state) : IAiContentSource
{
    public string AreaKey => "inbox";

    public string AreaTitle => "Inbox";

    public Task<AiContent> ComposeAsync(AiContentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var selectedId = state.SelectedItemId;

        // Newest capture first, the order the pane lists every slice in, so the
        // body reads the way the inbox does and ties on relevance fall to the
        // recent.
        IReadOnlyList<InboxItemDto> items =
        [
            .. state.Items
                .Where(item => item.Status != InboxStatus.Archived)
                .OrderByDescending(item => item.CapturedAt)
        ];

        return Task.FromResult(AiContentBudget.Compose(
            AreaKey,
            AreaTitle,
            items,
            Text,
            request.Question,
            request.BudgetCharacters,
            pinned: item => selectedId is { } id && item.Id == id));
    }

    /// <summary>One item as the prompt sees it. Empty facts are left out rather
    /// than written as "Tags: (none)", so a bare capture is two lines and not
    /// five.</summary>
    internal static string Text(InboxItemDto item)
    {
        var text = new StringBuilder();
        text.Append(string.IsNullOrWhiteSpace(item.Title) ? "(untitled)" : item.Title.Trim());
        text.Append("\nKind: ").Append(item.KindSlug);

        if (item.Tags.Count > 0)
        {
            text.Append("\nTags: ").Append(string.Join(", ", item.Tags.Select(tag => tag.Name)));
        }

        if (!string.IsNullOrWhiteSpace(item.SourceUrl))
        {
            text.Append("\nSource: ").Append(item.SourceUrl.Trim());
        }

        if (!string.IsNullOrWhiteSpace(item.BodyMd))
        {
            text.Append("\n\n").Append(item.BodyMd.Trim());
        }

        return text.ToString();
    }
}
