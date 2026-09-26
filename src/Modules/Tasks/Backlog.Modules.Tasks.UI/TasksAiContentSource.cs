using Backlog.Modules.Tasks.Abstractions;
using Backlog.SharedKernel.Ai;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// Tasks' answer to <see cref="IAiContentSource"/>: the entries in the repository
/// scope, as they are written.
/// </summary>
/// <remarks>
/// <para>
/// The scope and nothing narrower. <see cref="TasksDesktopState.ScopedRows"/> is
/// every entry of the selected repositories before the status chips, the area
/// filter and My Day cut the list down — the same set the filter chips count.
/// The repository scope is content: it says which projects the reader is
/// working in. The chips are screen: a reader who has pressed "Draft" has not
/// changed what the backlog holds, and a question about a ready entry deserves
/// an answer whether or not that chip is pressed. Ask AI used to read
/// <see cref="TasksDesktopState.FilteredRows"/>, and answered differently for
/// every chip.
/// </para>
/// <para>
/// The entry text as typed, trimmed, and nothing derived from it: the entry
/// language is designed to be read, and a parse rendered back out would be a
/// second spelling of the same fact for the assistant to reconcile. Untouched
/// drafts — the empty row a press on Add makes — are skipped, because a record
/// with no words in it is a separator with nothing between.
/// </para>
/// <para>
/// The selected entry is pinned rather than sent alone. The question is most
/// likely about it, so it goes first; but "what depends on this" needs the rest
/// of the scope in the body too.
/// </para>
/// </remarks>
internal sealed class TasksAiContentSource(TasksDesktopState state) : IAiContentSource
{
    public string AreaKey => "tasks";

    public string AreaTitle => "Tasks";

    public Task<AiContent> ComposeAsync(AiContentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var selected = state.SelectedRow;

        // Copied before ranking: the state's list is the live one the pane binds
        // to, and a row can arrive or go between two reads of it.
        IReadOnlyList<EntryRow> rows = [.. state.ScopedRows.Where(row => !row.IsUntouched)];

        return Task.FromResult(AiContentBudget.Compose(
            AreaKey,
            Title(),
            rows,
            Text,
            request.Question,
            request.BudgetCharacters,
            pinned: row => selected is not null && ReferenceEquals(row, selected),
            note: Totals(rows)));
    }

    /// <summary>
    /// One line of counts over every record in scope, not just the ones that fit.
    /// <para>
    /// The budget sends a slice of a large backlog, and a slice cannot answer
    /// "how many open tasks do I have?" — the assistant said so, correctly. The
    /// counts cost one line whatever the backlog's size, so they go with every
    /// question. "Open" is spelled out because it is the word people ask with and
    /// no status is called that. Every status and type is listed, zeros included:
    /// "0 in progress" is an answer, a missing word is not.
    /// </para>
    /// </summary>
    private static string? Totals(IReadOnlyList<EntryRow> rows)
    {
        if (rows.Count == 0) return null;

        var statuses = rows.CountBy(row => row.PreviewStatus).ToDictionary();
        var types = rows.CountBy(row => row.PreviewType).ToDictionary();
        var open = rows.Count(row => row.PreviewStatus is not (EntryStatus.Done or EntryStatus.Archived));

        return $"Totals across all entries in scope: {open} open (not done or archived). " +
            $"By status: {string.Join(", ", Enum.GetValues<EntryStatus>().Select(status => $"{statuses.GetValueOrDefault(status)} {StatusName(status)}"))}. " +
            $"By type: {string.Join(", ", Enum.GetValues<EntryType>().Select(type => $"{types.GetValueOrDefault(type)} {type.ToString().ToLowerInvariant()}"))}.";
    }

    private static string StatusName(EntryStatus status) => status == EntryStatus.InProgress
        ? "in progress"
        : status.ToString().ToLowerInvariant();

    /// <summary>The area, with the repository scope that defines it: the scope is
    /// content — it says which projects' entries these are — so the first line
    /// names it.</summary>
    private string Title() => state.SelectedRepositoryAliases.Count == 0
        ? AreaTitle
        : $"{AreaTitle} ({string.Join(", ", state.SelectedRepositoryAliases)})";

    private static string Text(EntryRow row) => row.RawText.Trim();
}
