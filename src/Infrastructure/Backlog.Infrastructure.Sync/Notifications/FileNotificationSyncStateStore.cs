using System.Text.Json;

namespace Backlog.Infrastructure.Sync.Notifications;

/// <summary>
/// This device's notification-exchange progress as one small JSON file beside the other
/// feeds' — plaintext, per installation, never under the workspace root. A file that cannot be
/// read reads as "got nowhere": every open notification is pushed again, which is harmless,
/// and the rejections recorded so far are lost from the next push, which a viewer reads as its
/// reply still waiting.
/// </summary>
public sealed class FileNotificationSyncStateStore : INotificationSyncStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public FileNotificationSyncStateStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        Current = Read();
    }

    public NotificationSyncState Current { get; private set; }

    public string StorePath => _path;

    /// <summary>Writes, then adopts, so a failed write leaves <see cref="Current"/> as it was.
    /// Written beside the file and moved over it, so a write cut short never leaves a torn
    /// file — which would read back as "got nowhere" and lose the rejections viewers read.</summary>
    public void Save(NotificationSyncState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var temporary = _path + ".tmp";

        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporary); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }

            throw;
        }

        Current = state;
    }

    private NotificationSyncState Read()
    {
        try
        {
            if (!File.Exists(_path)) return NotificationSyncState.Nowhere;

            var state = JsonSerializer.Deserialize<NotificationSyncState>(File.ReadAllText(_path), JsonOptions);
            return state?.Tracked is null ? NotificationSyncState.Nowhere : state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return NotificationSyncState.Nowhere;
        }
    }
}
