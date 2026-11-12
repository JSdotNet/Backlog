using Backlog.UI.Components.Notes;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The band a meeting recording shows on its note: a dot, the time recorded, the input level,
/// and Pause (Resume while paused) and Stop — each state told by a word as well as a shape,
/// Stop busy while the file is attached, and a failure said in words.
/// </summary>
public sealed class RecordingBarTests
{
    [Fact]
    public void Recording_shows_the_dot_the_timer_the_level_and_pause_and_stop()
    {
        using var context = new BunitContext();
        var paused = 0;

        var bar = context.Render<RecordingBar>(parameters => parameters
            .Add(p => p.State, RecordingBarState.Recording)
            .Add(p => p.Elapsed, TimeSpan.FromSeconds(83))
            .Add(p => p.Level, 0.42)
            .Add(p => p.OnPause, () => paused++)
            .Add(p => p.TestId, "bar"));

        Assert.Equal("recording", bar.Find("[data-testid='bar']").GetAttribute("data-state"));
        Assert.Equal("active", bar.Find("[data-testid='bar-dot']").GetAttribute("data-tone"));
        Assert.Equal("Recording", bar.Find("[data-testid='bar-label']").TextContent);
        Assert.Equal("status", bar.Find("[data-testid='bar-label']").GetAttribute("role"));

        var timer = bar.Find("[data-testid='bar-timer']");
        Assert.Equal("1:23", timer.TextContent);
        Assert.Equal("timer", timer.GetAttribute("role"));
        Assert.Null(timer.GetAttribute("aria-live"));
        Assert.Equal("PT0H1M23S", timer.GetAttribute("datetime"));

        var meter = bar.Find("[data-testid='bar-meter']");
        Assert.Equal("meter", meter.GetAttribute("role"));
        Assert.Equal("Input level", meter.GetAttribute("aria-label"));
        Assert.Equal("42", meter.GetAttribute("aria-valuenow"));
        Assert.Equal("42 percent", meter.GetAttribute("aria-valuetext"));

        Assert.Empty(bar.FindAll("[data-testid='bar-resume']"));
        Assert.Equal("Stop", bar.Find("[data-testid='bar-stop']").TextContent.Trim());
        bar.Find("[data-testid='bar-pause']").Click();
        Assert.Equal(1, paused);
    }

    [Fact]
    public void Paused_offers_resume_and_draws_a_hollow_dot_and_an_empty_meter()
    {
        using var context = new BunitContext();
        var resumed = 0;

        var bar = context.Render<RecordingBar>(parameters => parameters
            .Add(p => p.State, RecordingBarState.Paused)
            .Add(p => p.Elapsed, TimeSpan.FromSeconds(754))
            .Add(p => p.Level, 0.8)
            .Add(p => p.OnResume, () => resumed++)
            .Add(p => p.TestId, "bar"));

        Assert.Contains("recording-bar--paused", bar.Find("[data-testid='bar']").ClassList);
        Assert.Equal("pending", bar.Find("[data-testid='bar-dot']").GetAttribute("data-tone"));
        Assert.Equal("Paused", bar.Find("[data-testid='bar-label']").TextContent);
        Assert.Equal("12:34", bar.Find("[data-testid='bar-timer']").TextContent);
        Assert.Equal("0", bar.Find("[data-testid='bar-meter']").GetAttribute("aria-valuenow"));
        Assert.Empty(bar.FindAll("[data-testid='bar-pause']"));

        bar.Find("[data-testid='bar-resume']").Click();
        Assert.Equal(1, resumed);
    }

    [Fact]
    public void Stopping_makes_stop_busy_and_drops_a_second_press()
    {
        using var context = new BunitContext();
        var stopped = 0;

        var bar = context.Render<RecordingBar>(parameters => parameters
            .Add(p => p.State, RecordingBarState.Stopping)
            .Add(p => p.OnStop, () => stopped++)
            .Add(p => p.TestId, "bar"));

        var stop = bar.Find("[data-testid='bar-stop']");
        Assert.Equal("true", stop.GetAttribute("aria-busy"));
        Assert.False(stop.HasAttribute("disabled"));
        Assert.True(bar.Find("[data-testid='bar-pause']").HasAttribute("disabled"));
        Assert.Equal("Saving the recording", bar.Find("[data-testid='bar-label']").TextContent);

        stop.Click();
        Assert.Equal(0, stopped);
    }

    [Fact]
    public void Failed_says_why_in_words_with_no_meter_and_no_controls()
    {
        using var context = new BunitContext();

        var bar = context.Render<RecordingBar>(parameters => parameters
            .Add(p => p.State, RecordingBarState.Failed)
            .Add(p => p.Error, "The disk may be full.")
            .Add(p => p.TestId, "bar"));

        var error = bar.Find("[data-testid='bar-error']");
        Assert.Equal("The disk may be full.", error.TextContent);
        Assert.Equal("alert", error.GetAttribute("role"));
        Assert.Equal("blocked", bar.Find("[data-testid='bar-dot']").GetAttribute("data-tone"));
        Assert.Equal("Recording stopped", bar.Find("[data-testid='bar-label']").TextContent);
        Assert.Empty(bar.FindAll("[data-testid='bar-meter']"));
        Assert.Empty(bar.FindAll("button"));
    }

    [Fact]
    public void A_failed_bar_with_a_dismiss_handler_offers_dismiss()
    {
        using var context = new BunitContext();
        var dismissed = 0;

        var bar = context.Render<RecordingBar>(parameters => parameters
            .Add(p => p.State, RecordingBarState.Failed)
            .Add(p => p.OnDismiss, () => dismissed++)
            .Add(p => p.TestId, "bar"));

        Assert.Equal("The recording stopped.", bar.Find("[data-testid='bar-error']").TextContent);
        bar.Find("[data-testid='bar-dismiss']").Click();
        Assert.Equal(1, dismissed);
    }

    [Theory]
    [InlineData(RecordingBarState.Recording)]
    [InlineData(RecordingBarState.Paused)]
    public void A_notice_is_said_in_words_on_a_live_bar(RecordingBarState state)
    {
        using var context = new BunitContext();

        var bar = context.Render<RecordingBar>(parameters => parameters
            .Add(p => p.State, state)
            .Add(p => p.Notice, "Paused: a phone call has the microphone.")
            .Add(p => p.TestId, "bar"));

        Assert.Equal("Paused: a phone call has the microphone.", bar.Find("[data-testid='bar-notice']").TextContent);
    }

    [Fact]
    public void A_failed_bar_draws_no_notice()
    {
        using var context = new BunitContext();

        var bar = context.Render<RecordingBar>(parameters => parameters
            .Add(p => p.State, RecordingBarState.Failed)
            .Add(p => p.Notice, "Another app has the microphone. Nothing is being recorded.")
            .Add(p => p.TestId, "bar"));

        Assert.Empty(bar.FindAll("[data-testid='bar-notice']"));
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(59.9, "0:59")]
    [InlineData(754, "12:34")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3723, "1:02:03")]
    [InlineData(-5, "0:00")]
    public void The_timer_reads_minutes_and_seconds_then_hours(double seconds, string expected) =>
        Assert.Equal(expected, RecordingBar.FormatElapsed(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void A_level_outside_the_scale_is_clamped()
    {
        using var context = new BunitContext();

        var bar = context.Render<RecordingBar>(parameters => parameters
            .Add(p => p.State, RecordingBarState.Recording)
            .Add(p => p.Level, 3.0)
            .Add(p => p.TestId, "bar"));

        Assert.Equal("100", bar.Find("[data-testid='bar-meter']").GetAttribute("aria-valuenow"));
    }
}
