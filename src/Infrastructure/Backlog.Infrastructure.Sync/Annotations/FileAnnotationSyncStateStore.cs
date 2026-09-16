using System.Text.Json;

namespace Backlog.Infrastructure.Sync.Annotations;

/// <summary>
/// This device's annotation-replication progress, as one small JSON file
/// beside the task and session ones — plaintext, per-installation, and never
/// under the workspace root, for the reasons <see cref="FileTaskSyncStateStore"/>
/// gives at length. A file that cannot be read reads as "got nowhere", and
/// starting over is safe.
/// </summary>
public sealed class FileAnnotationSyncStateStore : IAnnotationSyncStateStore
{
    private static readonly AnnotationSyncState Nowhere = new(DateTimeOffset.MinValue, null);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public FileAnnotationSyncStateStore(string path)
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

    public AnnotationSyncState Current { get; private set; }

    public string StorePath => _path;

    /// <summary>Writes, then adopts, so a write that fails leaves
    /// <see cref="Current"/> as it was.</summary>
    public void Save(AnnotationSyncState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        File.WriteAllText(_path, JsonSerializer.Serialize(state, JsonOptions));

        Current = state;
        Changed?.Invoke();
    }

    private AnnotationSyncState Read()
    {
        try
        {
            if (!File.Exists(_path)) return Nowhere;

            return JsonSerializer.Deserialize<AnnotationSyncState>(File.ReadAllText(_path), JsonOptions) ?? Nowhere;
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
