using Backlog.Domain;

namespace Backlog.Storage;

/// <summary>
/// Maps domain enums to/from their ubiquitous-language wire strings used in
/// markdown frontmatter and the JSON index (e.g. <c>follow_up</c>, <c>in_progress</c>).
/// </summary>
internal static class EnumMap
{
    public static string ToWire(EntryType value) => value switch
    {
        EntryType.Prompt => "prompt",
        EntryType.Task => "task",
        EntryType.Idea => "idea",
        EntryType.FollowUp => "follow_up",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static string ToWire(EntryStatus value) => value switch
    {
        EntryStatus.Draft => "draft",
        EntryStatus.Ready => "ready",
        EntryStatus.InProgress => "in_progress",
        EntryStatus.Done => "done",
        EntryStatus.Archived => "archived",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static string ToWire(Priority value) => value switch
    {
        Priority.Low => "low",
        Priority.Medium => "medium",
        Priority.High => "high",
        Priority.Critical => "critical",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static string ToWire(SubItemStatus value) => value switch
    {
        SubItemStatus.Pending => "pending",
        SubItemStatus.Done => "done",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    public static EntryType ParseType(string value) => Normalize(value) switch
    {
        "prompt" => EntryType.Prompt,
        "task" => EntryType.Task,
        "idea" => EntryType.Idea,
        "followup" => EntryType.FollowUp,
        _ => throw new FormatException($"Unknown entry type '{value}'.")
    };

    public static EntryStatus ParseStatus(string value) => Normalize(value) switch
    {
        "draft" => EntryStatus.Draft,
        "ready" => EntryStatus.Ready,
        "inprogress" => EntryStatus.InProgress,
        "done" => EntryStatus.Done,
        "archived" => EntryStatus.Archived,
        _ => throw new FormatException($"Unknown entry status '{value}'.")
    };

    public static Priority ParsePriority(string value) => Normalize(value) switch
    {
        "low" => Priority.Low,
        "medium" => Priority.Medium,
        "high" => Priority.High,
        "critical" => Priority.Critical,
        _ => throw new FormatException($"Unknown priority '{value}'.")
    };

    public static SubItemStatus ParseSubItemStatus(string value) => Normalize(value) switch
    {
        "pending" => SubItemStatus.Pending,
        "done" => SubItemStatus.Done,
        _ => throw new FormatException($"Unknown sub-item status '{value}'.")
    };

    private static string Normalize(string value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant().Replace("_", string.Empty).Replace("-", string.Empty);
}
