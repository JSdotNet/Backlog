using Backlog.SharedKernel;

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
/// status silently becomes the wrong one. How a token is normalized and looked
/// up is <see cref="WireTokenMap{TEnum}"/>'s, shared with the Inbox's map; the
/// tokens are this context's.
/// </para>
/// </summary>
public static class EnumMap
{
    private static readonly WireTokenMap<EntryType> Types = new("task type", new Dictionary<EntryType, string>
    {
        [EntryType.Prompt] = "prompt",
        [EntryType.Task] = "task",
        [EntryType.Idea] = "idea",
        [EntryType.Test] = "test"
    });

    private static readonly WireTokenMap<EntryStatus> Statuses = new("task status", new Dictionary<EntryStatus, string>
    {
        [EntryStatus.Draft] = "draft",
        [EntryStatus.Ready] = "ready",
        [EntryStatus.InProgress] = "in_progress",
        [EntryStatus.Done] = "done",
        [EntryStatus.Archived] = "archived"
    });

    private static readonly WireTokenMap<Priority> Priorities = new("priority", new Dictionary<Priority, string>
    {
        [Priority.Low] = "low",
        [Priority.Medium] = "medium",
        [Priority.High] = "high",
        [Priority.Critical] = "critical"
    });

    private static readonly WireTokenMap<SubItemStatus> SubItemStatuses = new("sub-item status", new Dictionary<SubItemStatus, string>
    {
        [SubItemStatus.Pending] = "pending",
        [SubItemStatus.Done] = "done"
    });

    public static string ToWire(EntryType value) => Types.ToWire(value);

    public static string ToWire(EntryStatus value) => Statuses.ToWire(value);

    public static string ToWire(Priority value) => Priorities.ToWire(value);

    public static string ToWire(SubItemStatus value) => SubItemStatuses.ToWire(value);

    public static EntryType ParseType(string value) => Types.Parse(value);

    public static EntryStatus ParseStatus(string value) => Statuses.Parse(value);

    public static Priority ParsePriority(string value) => Priorities.Parse(value);

    public static SubItemStatus ParseSubItemStatus(string value) => SubItemStatuses.Parse(value);
}
