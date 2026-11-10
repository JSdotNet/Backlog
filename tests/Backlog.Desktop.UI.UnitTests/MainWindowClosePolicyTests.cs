namespace Backlog.Desktop.UI.UnitTests;

/// <summary>Closing the main window hides it while the app keeps running in the tray,
/// and quits otherwise (local ADR 0043 §5).</summary>
public sealed class MainWindowClosePolicyTests
{
    [Theory]
    [InlineData(true, false, true, MainWindowCloseAction.Hide)]
    [InlineData(true, true, true, MainWindowCloseAction.Quit)]
    [InlineData(false, false, true, MainWindowCloseAction.Quit)]
    [InlineData(false, true, true, MainWindowCloseAction.Quit)]
    public void Hides_only_while_kept_in_the_tray_and_no_quit_was_asked(bool keepRunningInTray, bool quitRequested, bool trayIconShown, MainWindowCloseAction expected) =>
        Assert.Equal(expected, MainWindowClosePolicy.For(keepRunningInTray, quitRequested, trayIconShown));

    [Fact]
    public void Without_a_tray_icon_to_bring_it_back_a_close_quits() =>
        Assert.Equal(MainWindowCloseAction.Quit, MainWindowClosePolicy.For(keepRunningInTray: true, quitRequested: false, trayIconShown: false));
}
