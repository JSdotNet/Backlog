namespace Backlog.Modules.Tasks.Abstractions;

/// <summary>
/// Maps domain enums to and from their ubiquitous-language wire strings
/// (e.g. <c>in_progress</c>).
/// <para>
/// Tokens rather than ordinals, for two reasons. A database somebody opens in a
/// SQLite browser should read as the domain reads, and an ordinal would silently
/// change meaning the day a member is inserted into the middle of an enum.
/// </para>
/// <para>
/// Published from this context rather than kept beside the SQLite adapter that
/// first wrote it, because a second reader arrived: the sync client maps the same
/// tokens on and off the wire, and the tokens the local store holds are exactly
/// the tokens a task travels as. Two copies of a vocabulary drift, and drift here
/// is not a cosmetic difference — a token one side writes and the other cannot
/// parse is a task that throws on load, which is somebody's entry lost.
/// </para>
/// <para>
/// It stays a translation table and nothing more. Nothing here validates,
/// defaults, or decides: an unknown token throws rather than being coerced to a
/// plausible member, because guessing what <c>in_progres</c> meant is how a
/// status silently becomes the wrong one.
/// </para>
/// </summary>
public static class EnumMap
{
    public static string ToWire(EntryType value) => value switch
    {
        EntryType.Prompt => "prompt",
        EntryType.Task => "task",
        EntryType.Idea => "idea",
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
        _ => throw new FormatException($"Unknown task type '{value}'.")
    };

    public static EntryStatus ParseStatus(string value) => Normalize(value) switch
    {
        "draft" => EntryStatus.Draft,
        "ready" => EntryStatus.Ready,
        "inprogress" => EntryStatus.InProgress,
        "done" => EntryStatus.Done,
        "archived" => EntryStatus.Archived,
        _ => throw new FormatException($"Unknown task status '{value}'.")
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
