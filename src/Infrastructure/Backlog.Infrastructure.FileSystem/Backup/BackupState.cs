using System.Text.Json;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// What the last backup attempt did, for the schedule to count from and the
/// settings screen to show.
/// </summary>
/// <param name="AttemptedAt">When the last attempt ran, whether or not it got
/// anywhere. The catch-up compares this against the last slot that went by:
/// a slot that fell between this and now is a backup that is owed.</param>
/// <param name="SucceededAt">When a backup last reached the repository, or null
/// while none has. Kept apart from <paramref name="AttemptedAt"/> so a machine
/// that has been failing since Tuesday still says when it last worked.</param>
/// <param name="Committed">Whether the last success wrote a commit, or found
/// the repository already holding these exact bytes.</param>
/// <param name="Error">Why the last attempt failed, or null when it did not.</param>
public sealed record BackupState(
    DateTimeOffset? AttemptedAt,
    DateTimeOffset? SucceededAt,
    bool Committed,
    string? Error)
{
    /// <summary>A machine that has never tried.</summary>
    public static BackupState None { get; } = new(null, null, false, null);
}

/// <summary>Where <see cref="BackupState"/> is kept between runs.</summary>
public interface IBackupStateStore
{
    BackupState Current { get; }

    string StorePath { get; }

    void Save(BackupState state);
}

/// <summary>
/// <see cref="BackupState"/> as one small JSON file beside the per-user
/// settings.
/// <para>
/// Not in <c>settings.json</c>, although that is where the repository and the
/// schedule are: the worker writes this from a thread-pool thread at the end of
/// a run, and the settings store is a single-threaded thing the settings screen
/// owns. And not under the workspace root, for the reason every other piece of
/// this installation's bookkeeping stays out of it — see
/// <c>FileTaskSyncStateStore</c>. A file that cannot be read reads as
/// <see cref="BackupState.None"/>: the worst that follows is one backup taken
/// that the schedule did not strictly owe, which the unchanged-content check
/// turns into no commit at all.
/// </para>
/// </summary>
public sealed class FileBackupStateStore : IBackupStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public FileBackupStateStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Current = Read();
    }

    public BackupState Current { get; private set; }

    public string StorePath => _path;

    /// <summary>Adopts, then writes. The other order — the one the sync stores
    /// use — exists so a failed write leaves a watermark alone; here the value
    /// is a report rather than a position, and the screen should show what the
    /// run just did even when the disk would not take it.</summary>
    public void Save(BackupState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        Current = state;

        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next start will count from the run before this one and may
            // take one backup the schedule did not owe. That is the whole cost.
        }
    }

    private BackupState Read()
    {
        try
        {
            if (!File.Exists(_path)) return BackupState.None;

            return JsonSerializer.Deserialize<BackupState>(File.ReadAllText(_path), JsonOptions) ?? BackupState.None;
        }
        catch (Exception)
        {
            return BackupState.None;
        }
    }
}
