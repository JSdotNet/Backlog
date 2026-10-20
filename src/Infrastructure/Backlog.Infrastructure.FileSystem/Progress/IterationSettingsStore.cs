using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.Progress;

/// <summary>The iteration a person chose, as the file spells it: the length as the
/// setting's stored value — kept verbatim, so a value a newer build added rides along —
/// and the date two-week windows count from, or none.</summary>
public sealed record IterationChoice(string Length, DateOnly? TwoWeekStart);

/// <summary>
/// The person's iteration settings — the length and the two-week start, together — as a
/// small JSON file beside <c>my-day-preferences.json</c> (local ADR 0036, §3), in
/// <see cref="MyDayPreferencesStore"/>'s shape:
/// <code>{ "updatedAt": "2026-10-20T08:00:00.0000000+00:00", "length": "two-weeks", "twoWeekStart": "2026-10-19" }</code>
/// <para>
/// A file that is not there, or cannot be read, is no choice, which
/// <see cref="IterationSettings"/> reads as Week; a person who never chose has nothing
/// written and nothing to send.
/// </para>
/// <para>
/// <b>The file is also the document that travels</b> between a person's desktops, under
/// the kind token <c>iteration-settings</c> (ADR 0036, §1). Every change writes an
/// <c>updatedAt</c>, stamped past the one held, and that is the document's stamp. A copy
/// from another device replaces the file whole at the stamp it arrived with, keeping any
/// key this build does not know; an echo, an older copy and a copy that does not read as
/// the settings leave it alone.
/// </para>
/// <para>
/// <b>The legacy value seeds it once.</b> An earlier build kept the choice per device in
/// <see cref="ShellNavigationStore"/>. With no file yet and a length there, the store
/// writes the file from it at <see cref="FloorStamp"/> (ADR 0036, §5): any copy a desktop
/// stamped since wins over it, and two seeded desktops converge because, at the floor
/// alone, an equal stamp carrying another choice is taken. After that the shell
/// navigation file's two fields are only ever read, never written.
/// </para>
/// <para>
/// Two events, for the reason <see cref="MyDayPreferencesStore"/> keeps two: the sync
/// hears a person's change through the port's <c>Changed</c>, and
/// <see cref="IterationSettings"/> hears a taken copy through <see cref="Replaced"/> — so
/// the Goals view redraws under a pull, and a pull never starts a push.
/// </para>
/// </summary>
public sealed class IterationSettingsStore : IIterationSettingsReplication
{
    /// <summary>The file's name. Persisted, so never to be renamed (ADR 0036).</summary>
    public const string FileName = "iteration-settings.json";

    /// <summary>The stamp a legacy value is seeded at: the Unix epoch, earlier than any
    /// change a person makes on this build. Persisted on every replica that holds a
    /// seeded copy, so never to be changed.</summary>
    public static readonly DateTimeOffset FloorStamp = DateTimeOffset.UnixEpoch;

    private const string StampKey = "updatedAt";
    private const string LengthKey = "length";
    private const string TwoWeekStartKey = "twoWeekStart";
    private const string IsoDate = "yyyy-MM-dd";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    /// <summary>What stamps a change. The system clock outside a test.</summary>
    private readonly TimeProvider _time;

    /// <summary>One writer at a time: a person's change on the renderer's thread and a
    /// pulled copy on the sync loop's must not interleave between the stamp read and the
    /// file write.</summary>
    private readonly Lock _gate = new();

    /// <summary>The published choice and its stamp, as one reference so a reader on any
    /// thread sees both halves of the same version.</summary>
    private Held _current;

    private Action? _localChange;

    /// <summary>The per-user file, seeded from <paramref name="legacy"/> when there is
    /// none yet.</summary>
    public IterationSettingsStore(ShellNavigationStore? legacy)
        : this(DefaultPath, legacy)
    {
    }

    /// <summary>Names the file separately from the per-user location — the only way to
    /// give a test, or the web harness that scopes its settings to its content root, a
    /// store that does not fight over the real per-user file.</summary>
    /// <param name="legacy">Where an earlier build kept the choice, read once to seed a
    /// file that is not there yet; null where there is nothing to seed from.</param>
    /// <param name="time">What stamps a change; the system clock when null.</param>
    public IterationSettingsStore(string path, ShellNavigationStore? legacy = null, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        _time = time ?? TimeProvider.System;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        _current = File.Exists(path) ? Read() : Seed(legacy);
    }

    private static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog",
            FileName);

    public string SettingsPath => _path;

    /// <summary>The choice held, or <c>null</c> when none was ever made — or the file
    /// cannot be read.</summary>
    public IterationChoice? Choice => _current.Choice;

    /// <summary>Raised after <see cref="IIterationSettingsReplication.ApplyAsync"/> took a
    /// copy that moved <see cref="Choice"/>. Never for a person's change, which the caller
    /// made and already knows of. It may arrive on any thread.</summary>
    public event Action? Replaced;

    event Action? IIterationSettingsReplication.Changed
    {
        add => _localChange += value;
        remove => _localChange -= value;
    }

    /// <summary>Replaces the choice whole, stamped past the one held, and tells the sync.
    /// Answers the message to show when it changed but could not be saved for next time,
    /// and <c>null</c> otherwise.</summary>
    public string? Set(string length, DateOnly? twoWeekStart)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(length);

        var choice = new IterationChoice(length, twoWeekStart);
        string? error = null;

        lock (_gate)
        {
            if (_current.Choice == choice) return null;

            var stamp = Next(_current.UpdatedAt);
            _current = new Held(choice, stamp);

            try
            {
                // Over the file's own object, so a key a newer build added rides along.
                Write(ReadObject() ?? [], choice, stamp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = "Changed, but the iteration couldn't be saved for next time.";
            }
        }

        _localChange?.Invoke();

        return error;
    }

    /// <summary>The file as stored and its stamp, or <c>null</c> when there is no file
    /// or it is not a JSON object — nothing a device should ask the others to read.
    /// Read from disk rather than from the published value, because the document is the
    /// file's text, keys this build does not read included.</summary>
    public Task<IterationSettingsCopyDto?> ReadAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path)) return Task.FromResult<IterationSettingsCopyDto?>(null);

                var content = File.ReadAllText(_path);
                if (JsonNode.Parse(content) is not JsonObject document) return Task.FromResult<IterationSettingsCopyDto?>(null);

                return Task.FromResult<IterationSettingsCopyDto?>(new IterationSettingsCopyDto(content, StampOf(document)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return Task.FromResult<IterationSettingsCopyDto?>(null);
            }
        }
    }

    public Task<IterationSettingsReplicaOutcome> ApplyAsync(
        IterationSettingsCopyDto inbound,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inbound);

        if (Parse(inbound.Content) is not { } document || ChoiceOf(document) is not { } choice)
        {
            return Task.FromResult(IterationSettingsReplicaOutcome.Unreadable);
        }

        bool moved;

        lock (_gate)
        {
            if (File.Exists(_path) && ReadObject() is { } held)
            {
                var stamp = StampOf(held);
                if (inbound.UpdatedAt < stamp) return Task.FromResult(IterationSettingsReplicaOutcome.Refused);

                // At the floor an equal stamp is another desktop's seeded value, and taking
                // it is how two seeded desktops end on the one the feed holds. One that says
                // what this device holds is its own seed coming back.
                if (inbound.UpdatedAt == stamp && (stamp != FloorStamp || ChoiceOf(held) == choice))
                {
                    return Task.FromResult(IterationSettingsReplicaOutcome.Echo);
                }
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
                // file is offered again whole at the next change on any device.
            }

            moved = _current.Choice != choice;
            _current = new Held(choice, inbound.UpdatedAt);
        }

        // The screen only: a copy from the replica is not news to tell the replica.
        if (moved) Replaced?.Invoke();

        return Task.FromResult(IterationSettingsReplicaOutcome.Taken);
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

    /// <summary>The file's choice, or none for a file that cannot be read: a corrupt
    /// setting reads as Week rather than costing the person the app.</summary>
    private Held Read()
    {
        try
        {
            if (ReadObject() is not { } document) return Held.Nothing;

            return ChoiceOf(document) is { } choice ? new Held(choice, StampOf(document)) : new Held(null, StampOf(document));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Held.Nothing;
        }
    }

    /// <summary>Writes the file from what an earlier build kept per device, at
    /// <see cref="FloorStamp"/>, when that build kept a length; no choice otherwise. The
    /// length is written as the setting's own value, so a member name an older file
    /// carried reads the same on every desktop.</summary>
    private Held Seed(ShellNavigationStore? legacy)
    {
        if (legacy?.GoalsIterationLength is not { } length) return Held.Nothing;

        var choice = new IterationChoice(
            IterationSettings.ToStored(IterationSettings.Parse(length)),
            legacy.GoalsTwoWeekStart);

        try
        {
            Write([], choice, FloorStamp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read as seeded for this session; the next start seeds again.
        }

        return new Held(choice, FloorStamp);
    }

    private void Write(JsonObject document, IterationChoice choice, DateTimeOffset stamp)
    {
        document[StampKey] = FormatStamp(stamp);
        document[LengthKey] = choice.Length;

        if (choice.TwoWeekStart is { } start) document[TwoWeekStartKey] = start.ToString(IsoDate, CultureInfo.InvariantCulture);
        else document.Remove(TwoWeekStartKey);

        File.WriteAllText(_path, document.ToJsonString(JsonOptions));
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

    /// <summary>The choice as the document spells it, or <c>null</c> — not the settings —
    /// where the length is absent or no string, or a start is there and no ISO date. Length
    /// and start travel together (ADR 0036, §1), so a document with half of them unreadable
    /// is not taken half.</summary>
    private static IterationChoice? ChoiceOf(JsonObject document)
    {
        if (!document.TryGetPropertyValue(LengthKey, out var lengthValue)
            || lengthValue is not JsonValue lengthSpelled
            || !lengthSpelled.TryGetValue<string>(out var length)
            || string.IsNullOrWhiteSpace(length))
        {
            return null;
        }

        if (!document.TryGetPropertyValue(TwoWeekStartKey, out var startValue) || startValue is null)
        {
            return new IterationChoice(length, null);
        }

        return startValue is JsonValue startSpelled
            && startSpelled.TryGetValue<string>(out var spelled)
            && DateOnly.TryParseExact(spelled, IsoDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                ? new IterationChoice(length, start)
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

    /// <summary>The published choice; <see cref="UpdatedAt"/> is <c>null</c> only while
    /// there is no file.</summary>
    private sealed record Held(IterationChoice? Choice, DateTimeOffset? UpdatedAt)
    {
        public static Held Nothing { get; } = new(null, null);
    }
}
