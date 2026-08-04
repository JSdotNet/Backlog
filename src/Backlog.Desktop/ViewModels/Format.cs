using Backlog.Domain;

namespace Backlog_Desktop.ViewModels;

/// <summary>Friendly display strings for domain enums.</summary>
public static class Format
{
    public static string Status(EntryStatus s) => s switch
    {
        EntryStatus.Draft => "Draft",
        EntryStatus.Ready => "Ready",
        EntryStatus.InProgress => "In progress",
        EntryStatus.Done => "Done",
        EntryStatus.Archived => "Archived",
        _ => s.ToString()
    };

    public static string Type(EntryType t) => t switch
    {
        EntryType.Prompt => "Prompt",
        EntryType.Task => "Task",
        EntryType.Idea => "Idea",
        EntryType.FollowUp => "Follow-up",
        _ => t.ToString()
    };

    public static string Priority(Priority p) => p.ToString();
}
