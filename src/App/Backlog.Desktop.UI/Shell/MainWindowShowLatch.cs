namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// Whether anything has asked to see the main window yet (local ADR 0043 §5). A start
/// at sign-in opens to the tray, but a launch, the tray or a link can ask for the window
/// before it has even been created; that ask wins over the hide the start would do.
/// Thread-safe: the ask arrives from the activation's thread, the hide runs on the
/// main one.
/// </summary>
public sealed class MainWindowShowLatch
{
    private int _asked;

    /// <summary>True once something asked to see the main window.</summary>
    public bool Asked => Volatile.Read(ref _asked) == 1;

    /// <summary>Something asked to see the main window.</summary>
    public void Ask() => Interlocked.Exchange(ref _asked, 1);

    /// <summary>True while a start to the tray should keep the main window hidden.</summary>
    public bool KeepHidden(bool startedHidden) => startedHidden && !Asked;
}
