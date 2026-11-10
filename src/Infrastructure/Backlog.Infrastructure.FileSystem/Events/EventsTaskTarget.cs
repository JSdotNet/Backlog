using System.Text.RegularExpressions;

using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.FileSystem.Events;

/// <summary>
/// Answers Events' <see cref="IEventTaskTarget"/> over Tasks' published <see cref="ITaskItems"/>
/// and <see cref="ITaskChangeSignal"/>: the one place an event's prep task becomes entry text.
/// <para>
/// Here and not in either module, for the reason the Inbox's backlog target is: Events may not
/// see Tasks, and Tasks never learns of the event (local ADR 0050 §3). The text is the title
/// line and the type word, nothing else — no <c>repo:</c> token, so the task lands under the
/// Brain scope (local ADR 0027 §2), and no tag, body or link naming the event.
/// </para>
/// <para>
/// The title is written as one line, every whitespace run one space, so nothing in it can start
/// a step, a second entry or a fence. A <c>#word</c> in it is a tag, as on any typed title line.
/// </para>
/// </summary>
internal sealed partial class EventsTaskTarget(ITaskItems tasks, ITaskChangeSignal signal) : IEventTaskTarget
{
    public event Action? Changed
    {
        add => signal.Changed += value;
        remove => signal.Changed -= value;
    }

    public async Task<Result<string>> FileAsync(string title, CancellationToken cancellationToken = default)
    {
        // Appended after everything the person has ranked, as a row typed at the bottom is.
        var order = await tasks.CountAsync(cancellationToken).ConfigureAwait(false);

        var saved = await tasks.SaveFromTextAsync(null, Compose(title), order, cancellationToken: cancellationToken).ConfigureAwait(false);

        return saved.IsFailure
            ? Result.Failure<string>(saved.Error)
            : Result.Success(saved.Value.Entry.Id.ToString("D"));
    }

    public async Task<IReadOnlyDictionary<string, EventTaskFactsDto>> LookupAsync(
        IReadOnlyCollection<string> taskIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(taskIds);

        var found = new Dictionary<string, EventTaskFactsDto>(StringComparer.Ordinal);
        foreach (var taskId in taskIds.Distinct(StringComparer.Ordinal))
        {
            if (!Guid.TryParse(taskId, out var id)) continue;

            if (await tasks.GetAsync(id, cancellationToken).ConfigureAwait(false) is { } entry)
            {
                found[taskId] = new EventTaskFactsDto(taskId, entry.Title, StatusWord(entry.Status));
            }
        }

        return found;
    }

    /// <summary>A prep task's whole text: its title as one heading line, then the type word.</summary>
    internal static string Compose(string title) =>
        "# " + WhitespaceRun().Replace((title ?? string.Empty).Trim(), " ") + "\n`task`\n";

    private static string StatusWord(EntryStatus status) => status switch
    {
        EntryStatus.Draft => "Draft",
        EntryStatus.Ready => "Ready",
        EntryStatus.InProgress => "In progress",
        EntryStatus.Done => "Done",
        EntryStatus.Archived => "Archived",
        _ => status.ToString()
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
