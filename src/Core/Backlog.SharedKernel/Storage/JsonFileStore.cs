using System.Text.Json;

namespace Backlog.SharedKernel.Storage;

/// <summary>
/// The file half of a small per-user JSON settings store: one document of
/// <typeparamref name="T"/> at one path, read whole and written whole. What the document
/// means — its defaults, how a bad value is dropped, the message a failed save shows —
/// stays the store's.
/// </summary>
/// <remarks>
/// <para>A corrupt or unreachable file must never stop the app from opening, so
/// <see cref="Load"/> answers <see langword="null"/> for a missing file and for one the
/// file system refuses (<see cref="IOException"/>, <see cref="UnauthorizedAccessException"/>)
/// or that is not the document (<see cref="JsonException"/>), and the store falls back to
/// its default.</para>
/// <para>A change the file system refuses is still the person's change for this session;
/// only its surviving a restart is lost. So <see cref="TrySave"/> answers whether it was
/// written rather than throwing, and the store decides what to say.</para>
/// </remarks>
/// <typeparam name="T">The document as stored — usually the store's own private DTO.</typeparam>
public sealed class JsonFileStore<T>
    where T : class
{
    private readonly JsonSerializerOptions _options;

    /// <param name="path">The file. Its folder is created when it is missing.</param>
    /// <param name="options">How the document is written and read; the store's own, so
    /// its JSON shape stays what it was.</param>
    public JsonFileStore(string path, JsonSerializerOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);

        Path = path;
        _options = options;

        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>The file this store reads and writes.</summary>
    public string Path { get; }

    /// <summary>The stored document, or <see langword="null"/> when there is no file,
    /// it cannot be read, it is not JSON of <typeparamref name="T"/>'s shape, or it holds
    /// <c>null</c>.</summary>
    public T? Load()
    {
        try
        {
            if (!File.Exists(Path)) return null;

            return JsonSerializer.Deserialize<T>(File.ReadAllText(Path), _options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes <paramref name="document"/> whole, or deletes the file when it is
    /// <see langword="null"/> — so an empty state looks the same on disk whether it was
    /// never set or cleared. <see langword="false"/> when the file system refused.</summary>
    public bool TrySave(T? document)
    {
        try
        {
            if (document is null)
            {
                if (File.Exists(Path)) File.Delete(Path);
            }
            else
            {
                File.WriteAllText(Path, JsonSerializer.Serialize(document, _options));
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
