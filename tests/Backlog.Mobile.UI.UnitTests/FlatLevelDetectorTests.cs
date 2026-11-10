using Backlog.Mobile.UI.Recordings;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The hint for a recorder that cannot say it was silenced
/// (.devbook/domain/capture/features.md#meeting-recording): a level of exactly 0 held for ten
/// seconds while recording means no sound is reaching the recording, and the first sound clears
/// it.
/// </summary>
public sealed class FlatLevelDetectorTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Silence_held_for_ten_seconds_is_flat()
    {
        var detector = new FlatLevelDetector();

        Assert.False(detector.Observe(0, Start));
        Assert.False(detector.Observe(0, Start.AddSeconds(9.9)));
        Assert.True(detector.Observe(0, Start.AddSeconds(10)));
    }

    [Fact]
    public void Any_sound_starts_the_count_again()
    {
        var detector = new FlatLevelDetector();
        detector.Observe(0, Start);

        Assert.False(detector.Observe(0.01, Start.AddSeconds(6)));
        Assert.False(detector.Observe(0, Start.AddSeconds(12)));
        Assert.True(detector.Observe(0, Start.AddSeconds(22)));
    }

    [Fact]
    public void A_reset_forgets_the_silence_so_far()
    {
        var detector = new FlatLevelDetector();
        detector.Observe(0, Start);

        // A pause, then a resume: the silence before it does not count.
        detector.Reset();

        Assert.False(detector.Observe(0, Start.AddSeconds(10)));
        Assert.True(detector.Observe(0, Start.AddSeconds(20)));
    }
}
