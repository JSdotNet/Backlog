namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The numbers that keep the change journal bounded (local ADR 0061 §6): the periods
/// the person may keep it for, and the database size past which the Backup card says
/// the history is something that can be cleared.
/// </summary>
public static class ChangeHistoryRetention
{
    /// <summary>The periods offered, in days.</summary>
    public static IReadOnlyList<int> Offered { get; } = [30, 90, 365];

    /// <summary>The period kept until somebody chooses another.</summary>
    public const int DefaultDays = 90;

    /// <summary>The size of <c>backlog.db</c> above which the Backup card warns: 50 MB.
    /// A backup commits the whole file (local ADR 0010), and the GitHub Contents API
    /// refuses a file far past this.</summary>
    public const long LargeDatabaseBytes = 50L * 1024 * 1024;

    /// <summary>A stored period, or <see cref="DefaultDays"/> when it is absent or
    /// not one of <see cref="Offered"/>.</summary>
    public static int Normalize(int? days) =>
        days is { } value && Offered.Contains(value) ? value : DefaultDays;

    /// <summary>Whether a database of <paramref name="bytes"/> is past
    /// <see cref="LargeDatabaseBytes"/>.</summary>
    public static bool IsLarge(long bytes) => bytes > LargeDatabaseBytes;
}
