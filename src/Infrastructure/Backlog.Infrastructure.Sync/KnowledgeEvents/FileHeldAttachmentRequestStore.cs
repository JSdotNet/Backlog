using System.Text.Json;

using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sync.KnowledgeEvents;

/// <summary>
/// The held attachment requests as one small JSON file beside the knowledge event feed's
/// progress — per installation, never under the workspace root, for the reasons
/// <see cref="FileTaskSyncStateStore"/> gives. Each request is the task-feed document it
/// arrived as, in the wire's own JSON shape.
/// <para>
/// A file that cannot be read reads as holding nothing: the requests stay on the replica until
/// some desktop retires them, so a second desktop with the area on, or a rehydrate, still
/// answers them. A write that fails is logged and keeps the request in memory for the process.
/// </para>
/// </summary>
public sealed class FileHeldAttachmentRequestStore : IHeldAttachmentRequestStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Lock _gate = new();
    private readonly string _path;
    private readonly ILogger _log;
    private readonly List<TaskChange> _held;

    public FileHeldAttachmentRequestStore(string path, ILogger<FileHeldAttachmentRequestStore>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        _log = log ?? NullLogger<FileHeldAttachmentRequestStore>.Instance;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _held = Read();
    }

    /// <summary>Where the requests are kept.</summary>
    public string StorePath => _path;

    public IReadOnlyList<TaskChange> ReadAll()
    {
        lock (_gate) return [.. _held];
    }

    public void Hold(TaskChange request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            var at = _held.FindIndex(each => each.Id == request.Id);
            if (at >= 0)
            {
                // The same version deferred again on a later retry: nothing to write.
                if (_held[at].UpdatedAt == request.UpdatedAt) return;
                _held[at] = request;
            }
            else
            {
                _held.Add(request);
            }

            Write();
        }
    }

    public void Release(Guid requestId)
    {
        lock (_gate)
        {
            if (_held.RemoveAll(each => each.Id == requestId) == 0) return;

            Write();
        }
    }

    /// <summary>Through a temporary file and a move, so a crash mid-write leaves the last
    /// complete file rather than a torn one that reads as holding nothing.</summary>
    private void Write()
    {
        var staged = _path + ".tmp";
        try
        {
            File.WriteAllText(staged, JsonSerializer.Serialize(_held, JsonOptions));
            File.Move(staged, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "The held attachment requests could not be written to {Path}; they are held for this run only.", _path);
        }
    }

    private List<TaskChange> Read()
    {
        try
        {
            if (!File.Exists(_path)) return [];

            return JsonSerializer.Deserialize<List<TaskChange>>(File.ReadAllText(_path), JsonOptions)?
                .Where(each => each is { Task: not null })
                .ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or NotSupportedException)
        {
            _log.LogWarning(ex, "The held attachment requests in {Path} could not be read; this desktop starts holding none.", _path);
            return [];
        }
    }
}
