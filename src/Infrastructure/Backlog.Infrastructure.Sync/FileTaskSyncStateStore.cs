using System.Text.Json;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// This device's replication progress, as one small JSON file.
/// <para>
/// Plaintext, unlike the credential beside it, because neither value is a
/// secret. The watermark is a timestamp off this machine's own tasks, and the
/// cursor is HMAC-signed by the service but not confidential — it names a
/// position in a feed and is useless without a device token that says whose feed
/// it is. Wrapping either in DPAPI would buy nothing and would make the file
/// unreadable to the person whose progress it describes.
/// </para>
/// <para>
/// A file, and deliberately not either of the two places that look more natural.
/// </para>
/// <para>
/// Not a table in <c>backlog.db</c>. ADR 0006 makes every schema shape a
/// statement that runs on the way into every operation, and ADR 0005 warns that
/// adding to that list is what moves the mechanism towards the boundary where it
/// stops being enough. Two scalars describing how far <em>this machine</em> got
/// do not earn a place in the schema that holds the person's work — they are not
/// the person's work, they are this installation's bookkeeping about it.
/// </para>
/// <para>
/// And not workspace settings. Those live under the workspace root, and the
/// syncing of that root is the entire hazard ADR 0005 exists to remove: one
/// device's watermark arriving on the other would make the second machine skip
/// its own unpushed work, silently and with nothing on screen to say so. The
/// failure would look like tasks that simply never travelled.
/// </para>
/// <para>
/// A file that cannot be read — half-written, hand-edited, left by an earlier
/// format — reads as "got nowhere" rather than as an error, the same way
/// <see cref="DpapiDeviceCredentialStore"/> treats an envelope it cannot open.
/// Starting over is the only recovery, and starting over is safe here.
/// </para>
/// </summary>
public sealed class FileTaskSyncStateStore : ITaskSyncStateStore
{
    /// <summary>Where a device that has never synced starts. The minimum instant
    /// rather than null, so the push predicate is a plain comparison with no case
    /// for "no watermark yet" — and so a first run asks for everything, which is
    /// exactly what a device that has pushed nothing owes the replica.</summary>
    private static readonly TaskSyncState Nowhere = new(DateTimeOffset.MinValue, null);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public FileTaskSyncStateStore(string path)
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

    public event Action? Changed;

    public TaskSyncState Current { get; private set; }

    public string StorePath => _path;

    /// <summary>Writes, then adopts. In that order for the reason
    /// <see cref="DpapiDeviceCredentialStore.Save"/> uses it: a write that fails
    /// throws and leaves <see cref="Current"/> as it was, so the run does not
    /// carry a watermark the next run will not find.</summary>
    public void Save(TaskSyncState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        File.WriteAllText(_path, JsonSerializer.Serialize(state, JsonOptions));

        Current = state;
        Changed?.Invoke();
    }

    private TaskSyncState Read()
    {
        try
        {
            if (!File.Exists(_path)) return Nowhere;

            return JsonSerializer.Deserialize<TaskSyncState>(File.ReadAllText(_path), JsonOptions) ?? Nowhere;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException)
        {
            return Nowhere;
        }
    }
}
