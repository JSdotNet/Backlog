using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The person's My Day preferences — today one switch, Clear My Day overnight — as a
/// small JSON file beside <c>planning-velocity.json</c> (local ADR 0032, §2), in
/// <see cref="PlanningVelocitySettingsStore"/>'s shape:
/// <code>{ "updatedAt": "2026-10-15T08:00:00.0000000+00:00", "clearsOvernight": false }</code>
/// <para>
/// A file that is not there, or cannot be read, reads as clearing overnight — the rule
/// from before the switch existed — so an install that predates it needs no migration,
/// and a person who never touched the switch has nothing written and nothing to send.
/// </para>
/// <para>
/// <b>The file is also the document that travels</b> between a person's desktops, under
/// the kind token <c>my-day-preferences</c> (ADR 0032, §1). Every change writes an
/// <c>updatedAt</c>, stamped past the one held, and that is the document's stamp. A copy
/// from another device replaces the file whole at the stamp it arrived with, keeping any
/// key this build does not know; an echo, an older copy and a copy that does not read as
/// the preferences leave it alone.
/// </para>
/// <para>
/// One store behind both ports, so the switch on screen and the document the sync sends
/// are one value. Their two <c>Changed</c> events differ on purpose: the screen hears a
/// taken copy so My Day redraws under it, and the sync does not, so a pull never starts a
/// push.
/// </para>
/// </summary>
public sealed class MyDayPreferencesStore : IMyDayPreferences, IMyDayPreferencesReplication
{
    /// <summary>The file's name. Persisted, so never to be renamed (ADR 0032).</summary>
    public const string FileName = "my-day-preferences.json";

    private const string StampKey = "updatedAt";
    private const string ClearsOvernightKey = "clearsOvernight";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    /// <summary>What stamps a change. The system clock outside a test.</summary>
    private readonly TimeProvider _time;

    /// <summary>One writer at a time: a person's change on the renderer's thread and a
    /// pulled copy on the sync loop's must not interleave between the stamp read and the
    /// file write.</summary>
    private readonly Lock _gate = new();

    /// <summary>The published value and its stamp, as one reference so a reader on any
    /// thread sees both halves of the same version.</summary>
    private Preferences _current;

    private Action? _changed;
    private Action? _localChange;

    public MyDayPreferencesStore()
        : this(DefaultPath)
    {
    }

    /// <summary>Names the file separately from the per-user location — the only way to
    /// give a test, or the web harness that scopes its settings to its content root, a
    /// store that does not fight over the real per-user file.</summary>
    /// <param name="time">What stamps a change; the system clock when null.</param>
    public MyDayPreferencesStore(string path, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        _time = time ?? TimeProvider.System;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        _current = Read();
    }

    private static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog",
            FileName);

    public string SettingsPath => _path;

    public bool ClearsOvernight => _current.ClearsOvernight;

    event Action? IMyDayPreferences.Changed
    {
        add => _changed += value;
        remove => _changed -= value;
    }

    event Action? IMyDayPreferencesReplication.Changed
    {
        add => _localChange += value;
        remove => _localChange -= value;
    }

    public string? SetClearsOvernight(bool clearsOvernight)
    {
        string? error = null;

        lock (_gate)
        {
            if (_current.ClearsOvernight == clearsOvernight) return null;

            var stamp = Next(_current.UpdatedAt);
            _current = new Preferences(clearsOvernight, stamp);

            try
            {
                // Over the file's own object, so a key a newer build added rides along.
                var document = ReadObject() ?? [];
                document[StampKey] = FormatStamp(stamp);
                document[ClearsOvernightKey] = clearsOvernight;
                File.WriteAllText(_path, document.ToJsonString(JsonOptions));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = "Changed, but My Day preferences couldn't be saved for next time.";
            }
        }

        _changed?.Invoke();
        _localChange?.Invoke();

        return error;
    }

    /// <summary>The file as stored and its stamp, or <c>null</c> when there is no file
    /// or it is not a JSON object — nothing a device should ask the others to read.
    /// Read from disk rather than from the published value, because the document is the
    /// file's text, keys this build does not read included.</summary>
    public Task<MyDayPreferencesCopyDto?> ReadAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path)) return Task.FromResult<MyDayPreferencesCopyDto?>(null);

                var content = File.ReadAllText(_path);
                if (JsonNode.Parse(content) is not JsonObject document) return Task.FromResult<MyDayPreferencesCopyDto?>(null);

                return Task.FromResult<MyDayPreferencesCopyDto?>(new MyDayPreferencesCopyDto(content, StampOf(document)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return Task.FromResult<MyDayPreferencesCopyDto?>(null);
            }
        }
    }

    public Task<MyDayPreferencesReplicaOutcome> ApplyAsync(
        MyDayPreferencesCopyDto inbound,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inbound);

        if (Parse(inbound.Content) is not { } document || ValueOf(document) is not { } clearsOvernight)
        {
            return Task.FromResult(MyDayPreferencesReplicaOutcome.Unreadable);
        }

        bool moved;

        lock (_gate)
        {
            if (File.Exists(_path) && ReadObject() is { } held)
            {
                var stamp = StampOf(held);
                if (inbound.UpdatedAt == stamp) return Task.FromResult(MyDayPreferencesReplicaOutcome.Echo);
                if (inbound.UpdatedAt < stamp) return Task.FromResult(MyDayPreferencesReplicaOutcome.Refused);
            }

            // The one key this store owns the value of; everything else as it arrived.
            document[StampKey] = FormatStamp(inbound.UpdatedAt);

            try
            {
                File.WriteAllText(_path, document.ToJsonString(JsonOptions));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Taken for this session anyway: the copy is the later choice, and the
                // file is offered again whole at the next flip on any device.
            }

            moved = _current.ClearsOvernight != clearsOvernight;
            _current = new Preferences(clearsOvernight, inbound.UpdatedAt);
        }

        // The screen only: a copy from the replica is not news to tell the replica.
        if (moved) _changed?.Invoke();

        return Task.FromResult(MyDayPreferencesReplicaOutcome.Taken);
    }

    /// <summary>
    /// The stamp for a change: now, or one tick past the stamp held when that is not
    /// already later — so a change made after taking a copy from a PC whose clock runs
    /// ahead is still the later version, not one the replica refuses.
    /// </summary>
    private DateTimeOffset Next(DateTimeOffset? held)
    {
        var now = _time.GetUtcNow();
        return held is { } previous && now <= previous ? previous.AddTicks(1) : now;
    }

    /// <summary>A missing file, an unreadable one and a switch that is not a boolean all
    /// read as clearing overnight: a corrupt preference must never cost the person the
    /// rule they had before it existed.</summary>
    private Preferences Read()
    {
        try
        {
            if (!File.Exists(_path) || ReadObject() is not { } document) return Preferences.Untouched;

            return new Preferences(ValueOf(document) ?? true, StampOf(document));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Preferences.Untouched;
        }
    }

    /// <summary>The file as a JSON object, or <c>null</c> when it is missing, not JSON,
    /// or not an object.</summary>
    private JsonObject? ReadObject()
    {
        try
        {
            return File.Exists(_path) ? Parse(File.ReadAllText(_path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static JsonObject? Parse(string? content)
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

    /// <summary>The switch as the document spells it: <c>true</c> where it is absent,
    /// and <c>null</c> — not the preferences — where it is anything but a boolean.</summary>
    private static bool? ValueOf(JsonObject document)
    {
        if (!document.TryGetPropertyValue(ClearsOvernightKey, out var value) || value is null) return true;

        return value is JsonValue spelled && spelled.TryGetValue<bool>(out var clearsOvernight)
            ? clearsOvernight
            : null;
    }

    /// <summary>The document's stamp, or the file's last-write time for one that has
    /// none or an unreadable one — when it was last written is when it was last
    /// set.</summary>
    private DateTimeOffset StampOf(JsonObject document) =>
        document.TryGetPropertyValue(StampKey, out var value)
        && value is JsonValue stamp
        && stamp.TryGetValue<string>(out var spelled)
        && DateTimeOffset.TryParse(spelled, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : new DateTimeOffset(File.GetLastWriteTimeUtc(_path), TimeSpan.Zero);

    /// <summary>Round-trippable and invariant, so the stamp means the same instant on
    /// another machine.</summary>
    private static string FormatStamp(DateTimeOffset stamp) => stamp.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>The published value; <see cref="UpdatedAt"/> is <c>null</c> only while
    /// there is no file.</summary>
    private sealed record Preferences(bool ClearsOvernight, DateTimeOffset? UpdatedAt)
    {
        public static Preferences Untouched { get; } = new(true, null);
    }
}
