using System.Text.Json;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>
/// The presence rule (<c>.devbook/domain/sessions/domain.md#presence</c>): away after the
/// "away after" setting without input, or while the screen is locked; and the last prompt
/// per Claude session, fed by the <c>UserPromptSubmit</c> hook events.
/// </summary>
public sealed class PresenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeInput _input = new();
    private readonly SessionPrompts _prompts;
    private TimeSpan _awayAfter = PresenceSettings.Default.AwayAfter;

    public PresenceTests() => _prompts = new SessionPrompts(_clock);

    private Presence NewPresence() => new(_input, _prompts, () => _awayAfter, _clock);

    private sealed class FakeInput : IInputActivity
    {
        public DateTimeOffset? LastInputAt { get; set; }

        public bool IsScreenLocked { get; set; }
    }

    private static JsonElement HookEvent(string name, string? sessionId) =>
        JsonSerializer.SerializeToElement(new Dictionary<string, string?>
        {
            ["hook_event_name"] = name,
            ["session_id"] = sessionId,
            ["cwd"] = "D:/Repos/Backlog"
        });

    // --- The away rule --------------------------------------------------------------------

    [Fact]
    public void Input_just_now_is_at_the_keyboard()
    {
        _input.LastInputAt = Now;

        var presence = NewPresence();

        Assert.False(presence.IsAway);
        Assert.Equal(Now, presence.LastInputAt);
    }

    [Fact]
    public void Input_one_second_short_of_away_after_is_still_at_the_keyboard()
    {
        _input.LastInputAt = Now - _awayAfter + TimeSpan.FromSeconds(1);

        Assert.False(NewPresence().IsAway);
    }

    [Fact]
    public void Input_as_old_as_away_after_is_away()
    {
        _input.LastInputAt = Now - _awayAfter;

        Assert.True(NewPresence().IsAway);
    }

    [Fact]
    public void Time_passing_without_input_makes_the_person_away_on_the_next_read()
    {
        _input.LastInputAt = Now;
        var presence = NewPresence();

        Assert.False(presence.IsAway);

        _clock.Advance(TimeSpan.FromMinutes(10));

        Assert.True(presence.IsAway);
    }

    [Fact]
    public void A_locked_screen_is_away_even_with_input_just_now()
    {
        _input.LastInputAt = Now;
        _input.IsScreenLocked = true;

        Assert.True(NewPresence().IsAway);
    }

    [Fact]
    public void Unlocking_with_fresh_input_is_at_the_keyboard_again()
    {
        _input.IsScreenLocked = true;
        var presence = NewPresence();
        Assert.True(presence.IsAway);

        _input.IsScreenLocked = false;
        _input.LastInputAt = Now;

        Assert.False(presence.IsAway);
    }

    [Fact]
    public void An_input_the_platform_could_not_read_counts_as_at_the_keyboard()
    {
        _input.LastInputAt = null;

        var presence = NewPresence();

        Assert.False(presence.IsAway);
        Assert.Null(presence.LastInputAt);
    }

    [Fact]
    public void The_away_after_setting_is_read_on_every_question()
    {
        _input.LastInputAt = Now - TimeSpan.FromMinutes(5);
        var presence = NewPresence();

        Assert.False(presence.IsAway);

        _awayAfter = TimeSpan.FromMinutes(3);

        Assert.True(presence.IsAway);
    }

    // --- The last prompt per session ------------------------------------------------------

    [Fact]
    public void A_session_that_never_prompted_has_no_last_prompt()
    {
        Assert.Null(NewPresence().LastPromptAt("session-a"));
    }

    [Fact]
    public void A_user_prompt_submit_is_that_sessions_last_prompt()
    {
        _prompts.Record(HookEvent("UserPromptSubmit", "session-a"));

        var presence = NewPresence();

        Assert.Equal(Now, presence.LastPromptAt("session-a"));
        Assert.Null(presence.LastPromptAt("session-b"));
    }

    [Fact]
    public void A_later_prompt_moves_the_last_prompt()
    {
        _prompts.Record(HookEvent("UserPromptSubmit", "session-a"));
        _clock.Advance(TimeSpan.FromMinutes(7));
        _prompts.Record(HookEvent("UserPromptSubmit", "session-a"));

        Assert.Equal(Now.AddMinutes(7), NewPresence().LastPromptAt("session-a"));
    }

    [Fact]
    public void Other_hook_events_are_not_prompts()
    {
        _prompts.Record(HookEvent("PreToolUse", "session-a"));
        _prompts.Record(HookEvent("PostToolUse", "session-a"));
        _prompts.Record(HookEvent("Stop", "session-a"));

        Assert.Null(_prompts.LastPromptAt("session-a"));
    }

    [Fact]
    public void Session_end_forgets_the_sessions_last_prompt()
    {
        _prompts.Record(HookEvent("UserPromptSubmit", "session-a"));
        _prompts.Record(HookEvent("SessionEnd", "session-a"));

        Assert.Null(_prompts.LastPromptAt("session-a"));
    }

    [Fact]
    public void A_prompt_without_a_session_id_or_a_body_that_is_not_an_object_is_ignored()
    {
        _prompts.Record(HookEvent("UserPromptSubmit", null));
        _prompts.Record(JsonSerializer.SerializeToElement(new[] { 1, 2 }));

        Assert.Null(_prompts.LastPromptAt(""));
    }

    // --- The settings ---------------------------------------------------------------------

    [Fact]
    public void The_defaults_are_ten_minutes_fifteen_minutes_and_eight_hours()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), PresenceSettings.Default.AwayAfter);
        Assert.Equal(TimeSpan.FromMinutes(15), PresenceSettings.Default.NotifyAfter);
        Assert.Equal(TimeSpan.FromHours(8), PresenceSettings.Default.DialogWait);
        Assert.Null(PresenceSettings.Default.Problem());
    }

    [Theory]
    [InlineData(0, 15, 8, "Away after")]
    [InlineData(10, 0, 8, "Notify after")]
    [InlineData(10, 15, 0, "Dialog wait")]
    [InlineData(10, 15, 25, "Dialog wait")]
    [InlineData(481, 15, 8, "Away after")]
    public void Settings_out_of_range_are_refused_with_the_setting_named(int awayMinutes, int notifyMinutes, int waitHours, string named)
    {
        var settings = new PresenceSettings(
            TimeSpan.FromMinutes(awayMinutes), TimeSpan.FromMinutes(notifyMinutes), TimeSpan.FromHours(waitHours));

        Assert.StartsWith(named, settings.Problem());
    }
}
