using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.UI.Components.Badges;

namespace Backlog.Desktop.UI.Inbox;

/// <summary>
/// What a suggestion chip says, as triage draws it: the tag as a tag, the
/// repository by the alias Settings gives it, the destination as the act it
/// would be. The column's detail words its chips the same way.
/// </summary>
internal static class InboxSuggestionWords
{
    public static string Text(InboxDesktopState state, InboxSuggestionDto suggestion) => suggestion.Kind switch
    {
        InboxSuggestionKind.Tag => TagText.Display(suggestion.Value),
        InboxSuggestionKind.Repository => $"Repository {state.RepositoryAlias(suggestion.Value)}",
        _ => suggestion.Value switch
        {
            "tasks" => "Move to backlog",
            "archive" => "Archive",
            "devbook" => "Keep as knowledge",
            var other => other
        }
    };

    /// <summary>The act and the reason, for the accessible name and the tooltip —
    /// and for a chip that cannot be taken, why not.</summary>
    public static string Label(InboxDesktopState state, InboxSuggestionDto suggestion)
    {
        var act = suggestion.Kind switch
        {
            InboxSuggestionKind.Tag => $"Add {TagText.Display(suggestion.Value)}",
            InboxSuggestionKind.Repository => $"Assign {state.RepositoryAlias(suggestion.Value)}",
            _ => Text(state, suggestion)
        };

        return suggestion.UnavailableReason is { } unavailable
            ? $"{act}: {suggestion.Reason} {unavailable}"
            : $"{act}: {suggestion.Reason}";
    }

    /// <summary>A test id fragment that names the suggestion: <c>tag-sync</c>,
    /// <c>repository-owner-name</c>, <c>destination-tasks</c>.</summary>
    public static string TestId(InboxSuggestionDto suggestion) =>
        suggestion.Key.Replace(':', '-').Replace('/', '-');
}
