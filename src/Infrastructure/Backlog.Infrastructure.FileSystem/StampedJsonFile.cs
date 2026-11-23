using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backlog.SharedKernel.Storage;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// A small per-user JSON settings file that is also a whole document travelling between a
/// person's desktops: an <c>updatedAt</c> stamp beside the values, every change stamped
/// past the one held, and a copy from another device written whole at the stamp it
/// arrived with, keeping any key this build does not know. The file half that
/// <see cref="Progress.IterationSettingsStore"/> and <see cref="MyDayPreferencesStore"/>
/// share; what the values are, and whether a copy reads as them, stays each store's.
/// </summary>
internal sealed class StampedJsonFile
{
    /// <summary>The key the stamp is kept under. Persisted, so never to be renamed.</summary>
    public const string StampKey = "updatedAt";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>What stamps a change. The system clock outside a test.</summary>
    private readonly TimeProvider _time;

    /// <param name="path">The file; its folder is created when missing.</param>
    /// <param name="time">What stamps a change; the system clock when null.</param>
    public StampedJsonFile(string path, TimeProvider? time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Path = path;
        _time = time ?? TimeProvider.System;

        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
    }

    /// <summary>The per-user location of a file named <paramref name="fileName"/>.</summary>
    public static string DefaultPath(string fileName) =>
        AppDataPaths.Shared(fileName);

    public string Path { get; }

    /// <summary>One writer at a time: a person's change on the renderer's thread and a
    /// pulled copy on the sync loop's must not interleave between the stamp read and the
    /// file write.</summary>
    public Lock Gate { get; } = new();

    public bool Exists => File.Exists(Path);

    /// <summary>
    /// The stamp for a change: now, or one tick past the stamp held when that is not
    /// already later — so a change made after taking a copy from a PC whose clock runs
    /// ahead is still the later version, not one the replica refuses.
    /// </summary>
    public DateTimeOffset Next(DateTimeOffset? held)
    {
        var now = _time.GetUtcNow();
        return held is { } previous && now <= previous ? previous.AddTicks(1) : now;
    }

    /// <summary>The file as stored and its stamp, or <c>null</c> when there is no file
    /// or it is not a JSON object — nothing a device should ask the others to read.
    /// Read from disk rather than from a published value, because the document is the
    /// file's text, keys this build does not read included. Called under
    /// <see cref="Gate"/>.</summary>
    public (string Content, DateTimeOffset UpdatedAt)? ReadStored()
    {
        try
        {
            if (!File.Exists(Path)) return null;

            var content = File.ReadAllText(Path);
            if (JsonNode.Parse(content) is not JsonObject document) return null;

            return (content, StampOf(document));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>The file as a JSON object, or <c>null</c> when it is missing, not JSON,
    /// or not an object.</summary>
    public JsonObject? ReadObject()
    {
        try
        {
            return File.Exists(Path) ? Parse(File.ReadAllText(Path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Text as a JSON object, or <c>null</c> when it is blank, not JSON, or not
    /// an object.</summary>
    public static JsonObject? Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        try
        {
            return JsonNode.Parse(content) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Sets <paramref name="document"/>'s stamp — the one key the file owns the
    /// value of — round-trippable and invariant, so it means the same instant on another
    /// machine. Set before the values, so a new file keeps the stamp first.</summary>
    public static void Stamp(JsonObject document, DateTimeOffset stamp) =>
        document[StampKey] = stamp.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Writes <paramref name="document"/> whole, every key as it is. Throws what
    /// the file system throws, for the caller to answer.</summary>
    public void Write(JsonObject document) =>
        File.WriteAllText(Path, document.ToJsonString(JsonOptions));

    /// <summary>The document's stamp, or the file's last-write time for one that has
    /// none or an unreadable one — when it was last written is when it was last
    /// set.</summary>
    public DateTimeOffset StampOf(JsonObject document) =>
        document.TryGetPropertyValue(StampKey, out var value)
        && value is JsonValue stamp
        && stamp.TryGetValue<string>(out var spelled)
        && DateTimeOffset.TryParse(spelled, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : new DateTimeOffset(File.GetLastWriteTimeUtc(Path), TimeSpan.Zero);
}
