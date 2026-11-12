namespace Backlog.Desktop.UI.UnitTests;

/// <summary>A start to the tray keeps the main window hidden only until something asks
/// to see it (local ADR 0043 §5): a launch, the tray, a link that arrived while the app
/// was still starting.</summary>
public sealed class MainWindowShowLatchTests
{
    [Fact]
    public void A_start_to_the_tray_stays_hidden_while_nothing_asked() =>
        Assert.True(new MainWindowShowLatch().KeepHidden(startedHidden: true));

    [Fact]
    public void A_plain_start_is_never_kept_hidden() =>
        Assert.False(new MainWindowShowLatch().KeepHidden(startedHidden: false));

    [Fact]
    public void A_show_asked_before_the_window_hid_keeps_it_shown()
    {
        var latch = new MainWindowShowLatch();

        latch.Ask();

        Assert.True(latch.Asked);
        Assert.False(latch.KeepHidden(startedHidden: true));
    }
}
