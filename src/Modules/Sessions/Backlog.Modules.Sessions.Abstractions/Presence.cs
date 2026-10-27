using System.Collections.Concurrent;
using System.Text.Json;

namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// Whether the person is at this desktop's keyboard, read at the moment somebody asks
/// (<c>.devbook/domain/sessions/domain.md#presence</c>, local ADR 0041).
/// <para>
/// Read on the desktop only. Nothing here is synced, published or served as an MCP tool:
/// the question relay reads it in process when a session asks, and a phone never sees it.
/// </para>
/// </summary>
public interface IPresence
{
    /// <summary>True when there was no keyboard or mouse input for the "away after"
    /// setting, or while the screen is locked; otherwise the person is at the keyboard.</summary>
    bool IsAway { get; }

    /// <summary>When the last keyboard or mouse input on this desktop happened, or null
    /// when the platform could not say.</summary>
    DateTimeOffset? LastInputAt { get; }

    /// <summary>When the person last submitted a prompt in the Claude session
    /// <paramref name="sessionId"/>, from its <c>UserPromptSubmit</c> hook events, or null
    /// when none arrived since this application started.</summary>
    DateTimeOffset? LastPromptAt(string sessionId);
}

/// <summary>
/// The platform's reading of the person's input: what a head answers and a test fakes.
/// Sampled when read, never polled, so a property read is the moment of the reading.
/// </summary>
public interface IInputActivity
{
    /// <summary>When the last keyboard or mouse input happened, or null when the platform
    /// could not read it.</summary>
    DateTimeOffset? LastInputAt { get; }

    /// <summary>Whether the screen is locked right now.</summary>
    bool IsScreenLocked { get; }
}

/// <summary>
/// The three desktop settings that shape the question relay
/// (<c>.devbook/domain/sessions/domain.md#presence</c>): how long without input makes the
/// person away, how long a question asked at the keyboard waits before it is published,
/// and how long a dialog's hook waits for an answer from Backlog.
/// </summary>
public sealed record PresenceSettings(TimeSpan AwayAfter, TimeSpan NotifyAfter, TimeSpan DialogWait)
{
    /// <summary>The defaults ADR 0041 settles: away after 10 minutes, notify after 15
    /// minutes, a dialog waits up to 8 hours.</summary>
    public static PresenceSettings Default { get; } =
        new(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), TimeSpan.FromHours(8));

    /// <summary>The shortest "away after" and "notify after" accepted.</summary>
    public static readonly TimeSpan ShortestDelay = TimeSpan.FromMinutes(1);

    /// <summary>The longest "away after" and "notify after" accepted: a working day.</summary>
    public static readonly TimeSpan LongestDelay = TimeSpan.FromHours(8);

    /// <summary>The shortest dialog wait accepted.</summary>
    public static readonly TimeSpan ShortestDialogWait = TimeSpan.FromHours(1);

    /// <summary>The longest dialog wait accepted: a day, so a hook is never held past it.</summary>
    public static readonly TimeSpan LongestDialogWait = TimeSpan.FromHours(24);

    /// <summary>Why these settings cannot be kept, as one sentence, or null when they can.</summary>
    public string? Problem() =>
        AwayAfter < ShortestDelay || AwayAfter > LongestDelay
            ? $"Away after must be between {ShortestDelay.TotalMinutes:0} and {LongestDelay.TotalMinutes:0} minutes."
            : NotifyAfter < ShortestDelay || NotifyAfter > LongestDelay
                ? $"Notify after must be between {ShortestDelay.TotalMinutes:0} and {LongestDelay.TotalMinutes:0} minutes."
                : DialogWait < ShortestDialogWait || DialogWait > LongestDialogWait
                    ? $"Dialog wait must be between {ShortestDialogWait.TotalHours:0} and {LongestDialogWait.TotalHours:0} hours."
                    : null;
}

/// <summary>
/// The last prompt the person submitted in each Claude session, taken from the
/// <c>UserPromptSubmit</c> hook events the telemetry endpoint already receives.
/// <para>
/// In memory: a restart forgets it, which reads as "no prompt since start" — the same
/// answer a session that never prompted gets. A <c>SessionEnd</c> forgets that session.
/// </para>
/// </summary>
public sealed class SessionPrompts
{
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _last = new(StringComparer.Ordinal);

    public SessionPrompts(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    /// <summary>Takes one hook event as the host sent it. A <c>UserPromptSubmit</c> with a
    /// <c>session_id</c> moves that session's last prompt to now; a <c>SessionEnd</c>
    /// forgets the session; anything else is ignored.</summary>
    public void Record(JsonElement hookEvent)
    {
        if (hookEvent.ValueKind is not JsonValueKind.Object) return;

        if (Text(hookEvent, "session_id") is not { } sessionId) return;

        switch (Text(hookEvent, "hook_event_name"))
        {
            case "UserPromptSubmit":
                _last[sessionId] = _clock.GetUtcNow();
                break;
            case "SessionEnd":
                _last.TryRemove(sessionId, out _);
                break;
        }
    }

    /// <summary>When the person last prompted in <paramref name="sessionId"/>, or null.</summary>
    public DateTimeOffset? LastPromptAt(string sessionId) =>
        !string.IsNullOrWhiteSpace(sessionId) && _last.TryGetValue(sessionId, out var at) ? at : null;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            && value.GetString() is { Length: > 0 } text
            ? text
            : null;
}

/// <summary>
/// The presence rule over a platform's input reading: away while the screen is locked,
/// or once the last input is at least "away after" old. An input the platform could not
/// read counts as at the keyboard, so a session asks where it always did and the
/// "notify after" delay still publishes a question nobody answers.
/// </summary>
public sealed class Presence : IPresence
{
    private readonly IInputActivity _input;
    private readonly SessionPrompts _prompts;
    private readonly Func<TimeSpan> _awayAfter;
    private readonly TimeProvider _clock;

    /// <param name="input">The platform's input reading.</param>
    /// <param name="prompts">The last prompt per session.</param>
    /// <param name="awayAfter">The "away after" setting, read on every question so a
    /// change on the settings page applies to the next one.</param>
    /// <param name="clock">The clock the input's age is measured on.</param>
    public Presence(IInputActivity input, SessionPrompts prompts, Func<TimeSpan> awayAfter, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(awayAfter);

        _input = input;
        _prompts = prompts;
        _awayAfter = awayAfter;
        _clock = clock ?? TimeProvider.System;
    }

    public bool IsAway =>
        _input.IsScreenLocked
        || (_input.LastInputAt is { } last && _clock.GetUtcNow() - last >= _awayAfter());

    public DateTimeOffset? LastInputAt => _input.LastInputAt;

    public DateTimeOffset? LastPromptAt(string sessionId) => _prompts.LastPromptAt(sessionId);
}
