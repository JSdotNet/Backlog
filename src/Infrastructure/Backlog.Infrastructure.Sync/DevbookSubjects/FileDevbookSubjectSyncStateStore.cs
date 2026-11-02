using System.Text.Json;

namespace Backlog.Infrastructure.Sync.DevbookSubjects;

/// <summary>
/// This device's devbook subject replication progress, as one small JSON file
/// beside the other feeds' — plaintext, per-installation, and never under the
/// workspace root, for the reasons <see cref="FileTaskSyncStateStore"/> gives. A
/// file that cannot be read reads as "got nowhere", and starting over is safe.
/// </summary>
public sealed class FileDevbookSubjectSyncStateStore : IDevbookSubjectSyncStateStore
{
    private static readonly DevbookSubjectSyncState Nowhere = new(DateTimeOffset.MinValue, null);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public FileDevbookSubjectSyncStateStore(string path)
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

    public DevbookSubjectSyncState Current { get; private set; }

    public string StorePath => _path;

    /// <summary>Writes, then adopts, so a write that fails leaves
    /// <see cref="Current"/> as it was.</summary>
    public void Save(DevbookSubjectSyncState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        File.WriteAllText(_path, JsonSerializer.Serialize(state, JsonOptions));

        Current = state;
        Changed?.Invoke();
    }

    private DevbookSubjectSyncState Read()
    {
        try
        {
            if (!File.Exists(_path)) return Nowhere;

            return JsonSerializer.Deserialize<DevbookSubjectSyncState>(File.ReadAllText(_path), JsonOptions) ?? Nowhere;
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
