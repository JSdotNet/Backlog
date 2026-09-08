using System.Text.Json;

namespace Backlog.Infrastructure.Sync.Sessions;

/// <summary>
/// This device's session-replication progress, as one small JSON file.
/// <para>
/// Plaintext, and beside the task one rather than inside it — see
/// <see cref="ISessionSyncStateStore"/> for why the two exchanges do not share a
/// file. Neither value is a secret: the watermark is a timestamp off this
/// machine's own sessions, and the cursor is HMAC-signed by the service but not
/// confidential, since it names a position in a feed and is useless without a
/// device token that says whose feed it is.
/// </para>
/// <para>
/// Not a table in <c>backlog.db</c> and not workspace settings, for the two
/// reasons <see cref="FileTaskSyncStateStore"/> sets out at length. The second is
/// the sharper one here: a workspace root can be pointed at a folder some
/// file-sync product carries, and one device adopting another's session watermark
/// would make it skip its own unpushed records with nothing on screen to say so.
/// </para>
/// <para>
/// A file that cannot be read — half-written, hand-edited, left by an earlier
/// format — reads as "got nowhere" rather than as an error, the same way
/// <see cref="FileTaskSyncStateStore"/> treats its own. Starting over is the only
/// recovery, and starting over is safe: a re-pushed record lands on the document
/// it already wrote.
/// </para>
/// </summary>
public sealed class FileSessionSyncStateStore : ISessionSyncStateStore
{
    /// <summary>Where a device that has never synced sessions starts. The minimum
    /// instant rather than null, so the push predicate is a plain comparison with
    /// no case for "no watermark yet" — and so a first run offers every session on
    /// the machine, which is what a device that has pushed nothing owes the
    /// replica.</summary>
    private static readonly SessionSyncState Nowhere = new(DateTimeOffset.MinValue, null);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public FileSessionSyncStateStore(string path)
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

    public SessionSyncState Current { get; private set; }

    public string StorePath => _path;

    /// <summary>Writes, then adopts. In that order for the reason
    /// <see cref="FileTaskSyncStateStore.Save"/> uses it: a write that fails
    /// throws and leaves <see cref="Current"/> as it was, so the run does not
    /// carry a watermark the next run will not find.</summary>
    public void Save(SessionSyncState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        File.WriteAllText(_path, JsonSerializer.Serialize(state, JsonOptions));

        Current = state;
        Changed?.Invoke();
    }

    private SessionSyncState Read()
    {
        try
        {
            if (!File.Exists(_path)) return Nowhere;

            return JsonSerializer.Deserialize<SessionSyncState>(File.ReadAllText(_path), JsonOptions) ?? Nowhere;
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
