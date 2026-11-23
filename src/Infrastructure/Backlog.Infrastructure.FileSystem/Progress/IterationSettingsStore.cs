using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.SharedKernel.Replication;

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
/// the kind token <c>iteration-settings</c> (ADR 0036, §1), on the terms
/// <see cref="StampedJsonFile"/> keeps: every change stamped past the one held, a copy
/// from another device written whole at its own stamp; an echo, an older copy and a copy
/// that does not read as the settings leave it alone.
/// </para>
/// <para>
/// An earlier build seeded the file once at <see cref="FloorStamp"/> from the choice it
/// kept per device (ADR 0036, §5). Replicas still hold such copies, so two of them
/// converge because, at the floor alone, an equal stamp carrying another choice is taken.
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

    /// <summary>The stamp a legacy value was seeded at: the Unix epoch, earlier than any
    /// change a person makes on this build. Persisted on every replica that holds a
    /// seeded copy, so never to be changed.</summary>
    public static readonly DateTimeOffset FloorStamp = DateTimeOffset.UnixEpoch;

    private const string LengthKey = "length";
    private const string TwoWeekStartKey = "twoWeekStart";
    private const string IsoDate = "yyyy-MM-dd";

    private readonly StampedJsonFile _file;

    /// <summary>The published choice and its stamp, as one reference so a reader on any
    /// thread sees both halves of the same version.</summary>
    private volatile Held _current;

    private Action? _localChange;

    /// <summary>The per-user file.</summary>
    public IterationSettingsStore()
        : this(StampedJsonFile.DefaultPath(FileName))
    {
    }

    /// <summary>Names the file separately from the per-user location — the only way to
    /// give a test, or the web harness that scopes its settings to its content root, a
    /// store that does not fight over the real per-user file.</summary>
    /// <param name="time">What stamps a change; the system clock when null.</param>
    public IterationSettingsStore(string path, TimeProvider? time = null)
    {
        _file = new StampedJsonFile(path, time);
        _current = _file.Exists ? Read() : Held.Nothing;
    }

    public string SettingsPath => _file.Path;

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

        lock (_file.Gate)
        {
            if (_current.Choice == choice) return null;

            var stamp = _file.Next(_current.UpdatedAt);
            _current = new Held(choice, stamp);

            try
            {
                // Over the file's own object, so a key a newer build added rides along.
                Write(_file.ReadObject() ?? [], choice, stamp);
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
    /// or it is not a JSON object.</summary>
    public Task<IterationSettingsCopyDto?> ReadAsync(CancellationToken cancellationToken = default)
    {
        lock (_file.Gate)
        {
            return Task.FromResult(
                _file.ReadStored() is { } stored ? new IterationSettingsCopyDto(stored.Content, stored.UpdatedAt) : null);
        }
    }

    public Task<ReplicaOutcome> ApplyAsync(
        IterationSettingsCopyDto inbound,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inbound);

        if (StampedJsonFile.Parse(inbound.Content) is not { } document || ChoiceOf(document) is not { } choice)
        {
            return Task.FromResult(ReplicaOutcome.Unreadable);
        }

        bool moved;

        lock (_file.Gate)
        {
            if (_file.Exists && _file.ReadObject() is { } held)
            {
                var stamp = _file.StampOf(held);
                var settled = LastWriteWins.Weigh(inbound.UpdatedAt, stamp);

                if (settled == ReplicaOutcome.Refused) return Task.FromResult(ReplicaOutcome.Refused);

                // At the floor an equal stamp is another desktop's seeded value, and taking
                // it is how two seeded desktops end on the one the feed holds. One that says
                // what this device holds is its own seed coming back.
                if (settled == ReplicaOutcome.Echo && (stamp != FloorStamp || ChoiceOf(held) == choice))
                {
                    return Task.FromResult(ReplicaOutcome.Echo);
                }
            }

            // The one key this store owns the value of; everything else as it arrived.
            StampedJsonFile.Stamp(document, inbound.UpdatedAt);

            try
            {
                _file.Write(document);
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

        return Task.FromResult(ReplicaOutcome.Taken);
    }

    /// <summary>The file's choice, or none for a file that cannot be read: a corrupt
    /// setting reads as Week rather than costing the person the app.</summary>
    private Held Read()
    {
        try
        {
            if (_file.ReadObject() is not { } document) return Held.Nothing;

            return new Held(ChoiceOf(document), _file.StampOf(document));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Held.Nothing;
        }
    }

    private void Write(JsonObject document, IterationChoice choice, DateTimeOffset stamp)
    {
        StampedJsonFile.Stamp(document, stamp);
        document[LengthKey] = choice.Length;

        if (choice.TwoWeekStart is { } start) document[TwoWeekStartKey] = start.ToString(IsoDate, CultureInfo.InvariantCulture);
        else document.Remove(TwoWeekStartKey);

        _file.Write(document);
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

    /// <summary>The published choice; <see cref="UpdatedAt"/> is <c>null</c> only while
    /// there is no file.</summary>
    private sealed record Held(IterationChoice? Choice, DateTimeOffset? UpdatedAt)
    {
        public static Held Nothing { get; } = new(null, null);
    }
}
