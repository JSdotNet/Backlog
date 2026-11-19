using System.Text.Json;
using System.Text.Json.Nodes;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Replication;

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
/// the kind token <c>my-day-preferences</c> (ADR 0032, §1), on the terms
/// <see cref="StampedJsonFile"/> keeps: every change stamped past the one held, a copy
/// from another device written whole at its own stamp; an echo, an older copy and a copy
/// that does not read as the preferences leave it alone.
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

    private const string ClearsOvernightKey = "clearsOvernight";

    private readonly StampedJsonFile _file;

    /// <summary>The published value and its stamp, as one reference so a reader on any
    /// thread sees both halves of the same version.</summary>
    private Preferences _current;

    private Action? _changed;
    private Action? _localChange;

    public MyDayPreferencesStore()
        : this(StampedJsonFile.DefaultPath(FileName))
    {
    }

    /// <summary>Names the file separately from the per-user location — the only way to
    /// give a test, or the web harness that scopes its settings to its content root, a
    /// store that does not fight over the real per-user file.</summary>
    /// <param name="time">What stamps a change; the system clock when null.</param>
    public MyDayPreferencesStore(string path, TimeProvider? time = null)
    {
        _file = new StampedJsonFile(path, time);
        _current = Read();
    }

    public string SettingsPath => _file.Path;

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

        lock (_file.Gate)
        {
            if (_current.ClearsOvernight == clearsOvernight) return null;

            var stamp = _file.Next(_current.UpdatedAt);
            _current = new Preferences(clearsOvernight, stamp);

            try
            {
                // Over the file's own object, so a key a newer build added rides along.
                var document = _file.ReadObject() ?? [];
                StampedJsonFile.Stamp(document, stamp);
                document[ClearsOvernightKey] = clearsOvernight;
                _file.Write(document);
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
    /// or it is not a JSON object.</summary>
    public Task<MyDayPreferencesCopyDto?> ReadAsync(CancellationToken cancellationToken = default)
    {
        lock (_file.Gate)
        {
            return Task.FromResult(
                _file.ReadStored() is { } stored ? new MyDayPreferencesCopyDto(stored.Content, stored.UpdatedAt) : null);
        }
    }

    public Task<ReplicaOutcome> ApplyAsync(
        MyDayPreferencesCopyDto inbound,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inbound);

        if (StampedJsonFile.Parse(inbound.Content) is not { } document || ValueOf(document) is not { } clearsOvernight)
        {
            return Task.FromResult(ReplicaOutcome.Unreadable);
        }

        bool moved;

        lock (_file.Gate)
        {
            if (_file.Exists && _file.ReadObject() is { } held
                && LastWriteWins.Weigh(inbound.UpdatedAt, _file.StampOf(held)) is { } settled)
            {
                return Task.FromResult(settled);
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
                // file is offered again whole at the next flip on any device.
            }

            moved = _current.ClearsOvernight != clearsOvernight;
            _current = new Preferences(clearsOvernight, inbound.UpdatedAt);
        }

        // The screen only: a copy from the replica is not news to tell the replica.
        if (moved) _changed?.Invoke();

        return Task.FromResult(ReplicaOutcome.Taken);
    }

    /// <summary>A missing file, an unreadable one and a switch that is not a boolean all
    /// read as clearing overnight: a corrupt preference must never cost the person the
    /// rule they had before it existed.</summary>
    private Preferences Read()
    {
        try
        {
            if (!_file.Exists || _file.ReadObject() is not { } document) return Preferences.Untouched;

            return new Preferences(ValueOf(document) ?? true, _file.StampOf(document));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Preferences.Untouched;
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

    /// <summary>The published value; <see cref="UpdatedAt"/> is <c>null</c> only while
    /// there is no file.</summary>
    private sealed record Preferences(bool ClearsOvernight, DateTimeOffset? UpdatedAt)
    {
        public static Preferences Untouched { get; } = new(true, null);
    }
}
