using Backlog.Aspire.ServiceDefaults;
using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.FileSystem;
using Backlog.SharedKernel;

namespace Backlog.Desktop.Services;

/// <summary>
/// The desktop head's windows (local ADR 0043 §4, §5): the main window
/// <see cref="App.CreateWindow"/> asks for, and every popped-out window
/// <see cref="Open"/> adds beside it, each a <see cref="MainPage"/> on its own start
/// path with a <c>BlazorWebView</c> — and so a scope — of its own.
/// <list type="bullet">
/// <item>Every window's <see cref="Window.Destroying"/> flushes
/// <see cref="ShellNavigationStore"/>, not only the main one's.</item>
/// <item>Closing the main window hides it to the notification area while the person
/// keeps the app running in the tray (<see cref="MainWindowClosePolicy"/>): the
/// stores are flushed, the process, its MCP server and every popped-out window keep
/// running. The tray's Quit, sign-out and shutdown, or the setting turned off, close
/// every popped-out window and quit the app.</item>
/// <item>A popped-out window writes its bounds to <see cref="ShellWindowStore"/> when it
/// moves, resizes or closes, and the next window of that surface opens there, clamped to
/// a monitor that is connected now.</item>
/// <item>Only a window the person remembered reopens at start (<see cref="ReopenRemembered"/>),
/// under the same key, on what it showed at quit and at its own last bounds. Quitting
/// keeps it remembered; the person closing it — its own close button, Show in main
/// window — forgets it.</item>
/// <item>A popped-out window can be kept on top of other windows. That is remembered per
/// surface beside its bounds, so the next window of that surface opens kept on top.</item>
/// <item>A popped-out window's native title bar carries Keep on top and Remember this
/// window beside the system caption buttons (<see cref="SurfaceWindowTitleBar"/>), drawn
/// from <see cref="SurfaceWindowCaption"/> and redrawn whenever either changes, whoever
/// changed it. The main window keeps MAUI's default title bar.</item>
/// </list>
/// A singleton, because the windows outlive every window's scope.
/// </summary>
public sealed class ShellWindows(
    ShellNavigationStore navigation,
    ShellWindowStore windowBounds,
    ISurfaceCatalog catalog,
    WorkspaceSettingsStore settings) : IShellWindows, IDisposable
{
    private const string AppTitle = "Backlog.Desktop";

    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, Window> _popped = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _surfaceOf = new(StringComparer.Ordinal);
    private readonly HashSet<string> _onTop = new(StringComparer.Ordinal);

    /// <summary>Each popped-out window's title bar, which draws its caption toggles.</summary>
    private readonly Dictionary<string, SurfaceWindowTitleBar> _captions = new(StringComparer.Ordinal);

    /// <summary>The popped-out windows that show a surface the catalog offers, rather
    /// than "not available": only those can be remembered.</summary>
    private readonly HashSet<string> _available = new(StringComparer.Ordinal);

    /// <summary>What each popped-out window shows now, which a remembered one reopens on.</summary>
    private readonly Dictionary<string, (string SurfaceId, IReadOnlyDictionary<string, string> Query)> _showing = new(StringComparer.Ordinal);

    /// <summary>Where each popped-out window stands, kept for that window alone, as the
    /// per-surface bounds are kept for the surface.</summary>
    private readonly Dictionary<string, ShellWindowBounds> _placed = new(StringComparer.Ordinal);

    private readonly object _gate = new();
    private Window? _main;

    /// <summary>The app is ending now: set once the main window is destroyed, so the
    /// windows it closes know a quit, not the person, closed them.</summary>
    private bool _quitting;

    /// <summary>Something asked the app to end — the tray's Quit, sign-out, shutdown —
    /// so the next close of the main window quits rather than hides.</summary>
    private volatile bool _quitRequested;

    /// <summary>A start at sign-in skipped reopening the remembered windows; they open
    /// the first time the main window is shown.</summary>
    private bool _reopenWhenShown;

    /// <summary>Whether anything asked to see the main window, so a start to the tray
    /// never hides a window a launch has already asked for.</summary>
    private readonly MainWindowShowLatch _showAsked = new();
#if WINDOWS
    private TrayIcon? _tray;
#endif

    /// <summary>Raised on the main thread each time closing the main window hid it to
    /// the notification area rather than quitting.</summary>
    public event Action? MainWindowHidden;

    /// <summary>The main window: <c>/</c>, under the app's own title.</summary>
    /// <param name="hidden">True for a start at sign-in (local ADR 0043 §5): the window
    /// is created and hidden at once, so the app opens straight to the tray.</param>
    public Window CreateMainWindow(bool hidden = false)
    {
        // One main window per run: this head asks for it once, at start. A second
        // would take over as the window the others are shown in front of.
        var title = DecoratedTitle(AppTitle);
        var window = new Window(new MainPage()) { Title = title };
        lock (_gate)
        {
            _main = window;
        }

#if WINDOWS
        window.Created += (_, _) =>
        {
            if ((window.Handler?.PlatformView as Microsoft.UI.Xaml.Window)?.AppWindow is not { } appWindow) return;

            appWindow.Closing += OnMainWindowClosing;
            if (!_showAsked.KeepHidden(hidden))
            {
                // A start to the tray that a launch asked for before the window existed:
                // brought forward as that launch would have, since this process started
                // at sign-in owns no foreground of its own.
                if (hidden) MainThread.BeginInvokeOnMainThread(ActivateMainWindow);
                return;
            }

            // Hidden now, and once more after the launch has activated the window,
            // whichever comes last — unless a launch asked for the window in between.
            appWindow.Hide();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_showAsked.KeepHidden(hidden)) appWindow.Hide();
            });
        };

        _tray = TrayIcon.TryCreate(title, ActivateMainWindow, RequestQuit, OnSessionEnding);
#endif

        // Reached only when the close was not cancelled: the policy said Quit.
        window.Destroying += (_, _) =>
        {
            Flush();
            CloseEveryOtherWindowAndQuit();
        };

        return window;
    }

    /// <summary>
    /// Quits the app the way the tray's Quit does: marks the quit as asked for, so the
    /// close policy answers Quit, and closes the main window, whose close flushes the
    /// stores, closes every other window and ends the app.
    /// </summary>
    public void RequestQuit()
    {
        _quitRequested = true;

        Window? main;
        lock (_gate)
        {
            main = _main;
        }

        if (main is null) return;

        // Always queued: the tray's menu asks from inside its own window's message,
        // and quitting removes that window.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Application.Current is { } application) application.CloseWindow(main);
        });
    }

    /// <summary>
    /// Windows asks whether it may sign out or shut down (<paramref name="ending"/>
    /// false), then says it will (true). What was on screen is written at the question,
    /// but only the answer makes a close quit: another app may still cancel the
    /// shutdown, and the next ✕ should hide as before.
    /// </summary>
    private void OnSessionEnding(bool ending)
    {
        if (ending) _quitRequested = true;
        Flush();
    }

    /// <summary>
    /// Reopens the remembered windows beside the main window (local ADR 0043 §5) — at
    /// start, or for a start at sign-in, the first time the main window is shown, so a
    /// start to the tray opens no window at all.
    /// </summary>
    public void ReopenRememberedWhenShown(bool hidden)
    {
        lock (_gate)
        {
            // Under the gate, as ActivateMainWindow reads it: an ask that came before
            // reopens now, one that comes after finds the flag set.
            if (_showAsked.KeepHidden(hidden))
            {
                _reopenWhenShown = true;
                return;
            }
        }

        ReopenRemembered();
    }

#if WINDOWS
    /// <summary>
    /// The main window's ✕ or Alt+F4 (local ADR 0043 §5). When the policy says Hide,
    /// the close is cancelled, the stores are flushed as a close would flush them, and
    /// the window is hidden; popped-out windows stay open. When it says Quit, the close
    /// goes ahead and <see cref="Window.Destroying"/> does the rest.
    /// </summary>
    private void OnMainWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        var action = MainWindowClosePolicy.For(settings.KeepRunningInTray, _quitRequested, _tray?.IsShown == true);
        if (action != MainWindowCloseAction.Hide) return;

        args.Cancel = true;
        Flush();
        sender.Hide();
        MainWindowHidden?.Invoke();
    }
#endif

    /// <summary>Removes the tray icon. The app is ending.</summary>
    public void Dispose()
    {
#if WINDOWS
        var tray = _tray;
        _tray = null;
        tray?.Dispose();
#endif
    }

    /// <inheritdoc />
    public void Open(string surfaceId, IReadOnlyDictionary<string, string>? query = null) => _ = OpenAsync(surfaceId, query);

    /// <inheritdoc />
    /// <remarks>True once the window is built and the application has opened it;
    /// false with no <see cref="Application.Current"/> to open it in, or when building
    /// it threw — the window is then forgotten, so nothing later finds it.</remarks>
    public Task<bool> OpenAsync(string surfaceId, IReadOnlyDictionary<string, string>? query = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceId);

        return Open(surfaceId, query, Guid.NewGuid().ToString("N"), reopening: null);
    }

    /// <summary>
    /// Reopens every remembered window whose surface the catalog still offers, beside
    /// the main window, and forgets the ones whose surface it no longer offers (local
    /// ADR 0043 §5). Each opens on its own: one that fails to build leaves the others,
    /// and the main window, as they were. Called once, after the main window is created.
    /// </summary>
    public void ReopenRemembered()
    {
        var plan = ShellWindowReopenPlan.For(windowBounds.Remembered, catalog);
        foreach (var key in plan.Drop) windowBounds.Forget(key);

        foreach (var window in plan.Open)
        {
            // A window already open under the key is the one remembered: asked twice,
            // nothing opens again.
            if (Popped(window.Key) is not null) continue;

            try
            {
                _ = Open(window.SurfaceId, window.Query, window.Key, window);
            }
            catch (Exception)
            {
                // An entry that cannot even be turned into a window's address would
                // fail the same way at every start: forget it, and open the rest.
                windowBounds.Forget(window.Key);
            }
        }
    }

    /// <summary>Opens a window under <paramref name="key"/>: a fresh one, or the
    /// remembered window <paramref name="reopening"/> at its own bounds.</summary>
    private Task<bool> Open(string surfaceId, IReadOnlyDictionary<string, string>? query, string key, RememberedShellWindow? reopening)
    {
        var title = catalog.TryGet(surfaceId, out var surface) ? surface.Title : AppTitle;
        var path = SurfaceWindowRoute.PathFor(surfaceId, query);

        // Asynchronous continuations: the caller resumes off the main thread, never
        // inside the window's construction.
        var built = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Built on the main thread as well as opened there: the page and its
        // BlazorWebView are controls, and a caller from outside the user interface
        // asks from wherever it runs.
        OnMainThread(() =>
        {
            if (Application.Current is not { } application)
            {
                built.TrySetResult(false);
                return;
            }

            try
            {
                application.OpenWindow(Build(surfaceId, query, key, path, title, reopening));
                built.TrySetResult(true);
            }
            catch (Exception)
            {
                lock (_gate)
                {
                    Forget(key);
                }

                built.TrySetResult(false);
            }
        });

        return built.Task;
    }

    /// <summary>A popped-out window on <paramref name="path"/>, known under
    /// <paramref name="key"/> until it closes. Called on the main thread.</summary>
    private Window Build(
        string surfaceId,
        IReadOnlyDictionary<string, string>? query,
        string key,
        string path,
        string title,
        RememberedShellWindow? reopening)
    {
        var window = new Window(new MainPage(path, key)) { Title = WindowTitle(title) };
        var caption = new SurfaceWindowTitleBar(WindowTitle(title), kind => SurfaceWindowCaption.Press(this, key, kind));
#if WINDOWS
        // Set before the window opens, so it never shows MAUI's default title bar
        // first. Window.Title stays what the taskbar and Alt+Tab read.
        window.TitleBar = caption.View;
        window.Created += (_, _) => DressCaptionButtons(window);
#endif

        RememberBounds(window, key, surfaceId, reopening);

        // Subscribed after RememberBounds' own, so a remembered window keeps where it
        // stood before a close by the person forgets it.
        window.Destroying += (_, _) =>
        {
            bool quitting;
            lock (_gate)
            {
                Forget(key);
                quitting = _quitting;
            }

            // Quitting is not a choice about this window, so a remembered one stays
            // remembered; the person closing it is (local ADR 0043 §5).
            if (!quitting) windowBounds.Forget(key);

            // The main window's close flushed once for every window it closes.
            if (!quitting) Flush();
            else FlushBounds();
        };

        lock (_gate)
        {
            _popped[key] = window;
            _showing[key] = (surfaceId, new Dictionary<string, string>(query ?? new Dictionary<string, string>(), StringComparer.Ordinal));
            _surfaceOf[key] = surfaceId;
            _captions[key] = caption;
            if (catalog.TryGet(surfaceId, out _)) _available.Add(key);
            if (windowBounds.IsOnTop(surfaceId)) _onTop.Add(key);
        }

        ShowCaption(key);

        return window;
    }

    /// <summary>Draws the window <paramref name="key"/> names' caption toggles as they
    /// stand now. A window that has gone draws nothing.</summary>
    private void ShowCaption(string key) => OnMainThread(() =>
    {
        // Read where it is drawn: two changes from different threads then draw in
        // the order the main thread runs them, each reading the state as it is then,
        // never an older snapshot posted after a newer one.
        SurfaceWindowTitleBar? caption;
        bool available;
        lock (_gate)
        {
            caption = _captions.GetValueOrDefault(key);
            available = _available.Contains(key);
        }

        caption?.Show(SurfaceWindowCaption.For(this, key, available));
    });

    /// <summary>Drops every trace of the window <paramref name="key"/> names. Called
    /// under the gate.</summary>
    private void Forget(string key)
    {
        _popped.Remove(key);
        _showing.Remove(key);
        _placed.Remove(key);
        _surfaceOf.Remove(key);
        _onTop.Remove(key);
        _captions.Remove(key);
        _available.Remove(key);
    }

    /// <inheritdoc />
    public bool CanRemember => true;

    /// <inheritdoc />
    public bool IsRemembered(string windowKey) =>
        !string.IsNullOrWhiteSpace(windowKey) && windowBounds.GetRemembered(windowKey) is not null;

    /// <inheritdoc />
    /// <remarks>Remembered on what the window shows now and where it stands now; a
    /// key whose window has gone is only ever forgotten.</remarks>
    public void SetRemembered(string windowKey, bool remembered)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowKey);

        if (!remembered)
        {
            windowBounds.Forget(windowKey);
        }
        else if (Showing(windowKey) is { } showing)
        {
            windowBounds.Remember(new RememberedShellWindow(windowKey, showing.SurfaceId, showing.Query, showing.Placed));
        }

        // Whoever asked, the bookmark says what the store now holds.
        ShowCaption(windowKey);
    }

    /// <summary>What the window <paramref name="windowKey"/> names shows and where it
    /// stands, or null for a window that has gone.</summary>
    private (string SurfaceId, IReadOnlyDictionary<string, string> Query, ShellWindowBounds? Placed)? Showing(string windowKey)
    {
        lock (_gate)
        {
            return _showing.TryGetValue(windowKey, out var showing)
                ? (showing.SurfaceId, showing.Query, _placed.GetValueOrDefault(windowKey))
                : null;
        }
    }

    /// <inheritdoc />
    public void ReportShowing(string windowKey, string surfaceId, IReadOnlyDictionary<string, string> query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceId);
        ArgumentNullException.ThrowIfNull(query);

        var showing = (surfaceId, (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(query, StringComparer.Ordinal));
        bool becameAvailable;
        lock (_gate)
        {
            if (!_popped.ContainsKey(windowKey)) return;
            _showing[windowKey] = showing;

            // Only a surface the window shows is reported, so the window can be
            // remembered from here on.
            becameAvailable = _available.Add(windowKey);
        }

        if (windowBounds.GetRemembered(windowKey) is { } remembered)
            windowBounds.Remember(remembered with { SurfaceId = surfaceId, Query = showing.Item2 });

        if (becameAvailable) ShowCaption(windowKey);
    }

    /// <inheritdoc />
    public void Close(string windowKey)
    {
        // Always queued, never run inline: the press that asks is handled by the
        // renderer of the very BlazorWebView this closes, which must finish handling
        // it before its window is torn down.
        if (Popped(windowKey) is { } window) MainThread.BeginInvokeOnMainThread(() => Application.Current?.CloseWindow(window));
    }

    /// <inheritdoc />
    public void SetTitle(string windowKey, string title)
    {
        SurfaceWindowTitleBar? caption;
        lock (_gate)
        {
            caption = _captions.GetValueOrDefault(windowKey);
        }

        // Both: the caption draws its own title, and the taskbar and Alt+Tab read the window's.
        if (Popped(windowKey) is { } window)
        {
            OnMainThread(() =>
            {
                window.Title = WindowTitle(title);
                caption?.SetTitle(WindowTitle(title));
            });
        }
    }

    /// <inheritdoc />
    public void ActivateMainWindow()
    {
        Window? main;
        bool reopen;
        lock (_gate)
        {
            // Asked even before the main window exists: a start to the tray then
            // opens it shown instead of hiding it.
            _showAsked.Ask();
            main = _main;
            reopen = _reopenWhenShown && main is not null;
            if (reopen) _reopenWhenShown = false;
        }

        // Shown first: a main window hidden to the tray comes back for every caller
        // that asks for it - the tray, a plain second launch, a link, a toast, Show
        // in main window (local ADR 0043 §5).
        if (main is not null) OnMainThread(() =>
        {
#if WINDOWS
            var platformWindow = main.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            if (platformWindow?.AppWindow is { } appWindow)
            {
                appWindow.Show();
                if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presenter)
                    presenter.Restore();
            }
#endif
            Application.Current?.ActivateWindow(main);
#if WINDOWS
            // Activating alone leaves a window shown behind the foreground one, or only
            // flashing in the taskbar; the launch that asked granted this process the
            // foreground (AllowSetForegroundWindow), and the tray's own click owns it.
            if (platformWindow is not null) _ = SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(platformWindow));
#endif

            if (!reopen) return;

            try
            {
                ReopenRemembered();
            }
            catch (Exception)
            {
                // As at start: the main window is up, and the next start tries again.
            }
        });
    }

    /// <inheritdoc />
    /// <remarks>Only on Windows, the one platform this head ships on whose windows
    /// have the presenter that does it.</remarks>
    public bool CanKeepOnTop =>
#if WINDOWS
        true;
#else
        false;
#endif

    /// <inheritdoc />
    public bool IsOnTop(string windowKey)
    {
        lock (_gate)
        {
            return _onTop.Contains(windowKey);
        }
    }

    /// <inheritdoc />
    public void SetOnTop(string windowKey, bool onTop)
    {
        if (!CanKeepOnTop) return;

        Window? window;
        string? surfaceId;
        lock (_gate)
        {
            window = _popped.GetValueOrDefault(windowKey);
            surfaceId = _surfaceOf.GetValueOrDefault(windowKey);
            if (window is null || surfaceId is null) return;

            if (onTop) _onTop.Add(windowKey);
            else _onTop.Remove(windowKey);
        }

        windowBounds.SetOnTop(surfaceId, onTop);
        OnMainThread(() => ApplyOnTop(window, onTop));
        ShowCaption(windowKey);
    }

    /// <summary>
    /// The window <paramref name="windowKey"/> names: a popped-out window, or the main
    /// window for no key — the main window's own scope carries none — and for a key whose
    /// window has gone. What a head service that needs a window to stand over, such as a
    /// picker, asks for the window a request came from.
    /// </summary>
    public Window? WindowFor(string? windowKey)
    {
        lock (_gate)
        {
            return windowKey is not null && _popped.TryGetValue(windowKey, out var window) ? window : _main;
        }
    }

    /// <summary>The main window, once <see cref="CreateMainWindow"/> made it.</summary>
    public Window? MainWindow => WindowFor(null);

    private Window? Popped(string windowKey)
    {
        lock (_gate)
        {
            return _popped.GetValueOrDefault(windowKey);
        }
    }

    /// <summary>
    /// The main window went: every popped-out window goes with it, the tray icon is
    /// removed, and the app quits. Without the quit the app would live on in the
    /// windows beside it, which is not what quitting means (local ADR 0043 §5).
    /// </summary>
    private void CloseEveryOtherWindowAndQuit()
    {
        Window[] others;
        lock (_gate)
        {
            if (_quitting) return;

            _quitting = true;
            others = [.. _popped.Values];
            _popped.Clear();
            _surfaceOf.Clear();
            _onTop.Clear();
            _captions.Clear();
            _available.Clear();
        }

        var application = Application.Current;
        if (application is null) return;

        foreach (var window in others) application.CloseWindow(window);

        Dispose();
        application.Quit();
    }

    /// <summary>
    /// The shell writes what was on screen off the click path, so the last choice
    /// made before a window closed may still be on its way to disk. The window waits
    /// for it rather than leaving it to the process exit, but only briefly: a write
    /// stalled on a synced folder must not hold the closing window.
    /// </summary>
    private void Flush() => Task.Run(() =>
    {
        navigation.Flush();
        windowBounds.Flush();
    }).Wait(FlushTimeout);

    /// <summary>The window bounds alone: the main window's close already flushed the
    /// navigation for every window it closes.</summary>
    private void FlushBounds() => Task.Run(windowBounds.Flush).Wait(FlushTimeout);

    /// <summary>
    /// Opens <paramref name="window"/> where the last window of
    /// <paramref name="surfaceId"/> stood — or, reopening a remembered window, where that
    /// window stood — clamped to a monitor connected now, and keeps its bounds as it
    /// moves, resizes and closes. Placed before the window is first shown, so it never
    /// flashes up where the system would have put it.
    /// </summary>
    private void RememberBounds(Window window, string key, string surfaceId, RememberedShellWindow? reopening)
    {
#if WINDOWS
        window.Created += (_, _) =>
        {
            if ((window.Handler?.PlatformView as Microsoft.UI.Xaml.Window)?.AppWindow is not { } appWindow) return;

            // A remembered window kept no bounds opens where the system puts a new
            // one, not where another window of its surface last stood.
            var placed = (reopening is not null ? reopening.Bounds : windowBounds.Get(surfaceId))?.ClampTo(WorkAreas());
            Place(appWindow, placed);
            if (placed is not null)
            {
                lock (_gate)
                {
                    _placed[key] = placed;
                }
            }

            if (IsOnTop(key)) ApplyOnTop(window, onTop: true);

            // Kept once as placed, before anything maximises it: a window maximised
            // before it ever moved still has a rectangle to restore to.
            Keep(appWindow, key, surfaceId, restoring: true);

            appWindow.Changed += (sender, args) =>
            {
                if (args.DidPositionChange || args.DidSizeChange || args.DidPresenterChange) Keep(sender, key, surfaceId);
            };
        };

        window.Destroying += (_, _) =>
        {
            if ((window.Handler?.PlatformView as Microsoft.UI.Xaml.Window)?.AppWindow is { } appWindow) Keep(appWindow, key, surfaceId);
        };
#endif
    }

    /// <summary>Keeps <paramref name="window"/> above every window that is not kept on
    /// top itself, or lets it go. Called on the main thread.</summary>
    private static void ApplyOnTop(Window window, bool onTop)
    {
#if WINDOWS
        if ((window.Handler?.PlatformView as Microsoft.UI.Xaml.Window)?.AppWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = onTop;
#endif
    }

#if WINDOWS
    /// <summary>
    /// The system's minimise, maximise and close, which it keeps drawing over the
    /// custom title bar, in the same dark tokens as the bar
    /// (<c>.devbook/design/color-scheme.md</c>) whatever the system theme: glyphs in
    /// <c>color-text-primary</c>, <c>color-text-secondary</c> while the window is
    /// inactive, over <c>color-background-alt</c> under the pointer and
    /// <c>color-background-raised</c> pressed. The system keeps close's own red hover.
    /// </summary>
    private static void DressCaptionButtons(Window window)
    {
        if ((window.Handler?.PlatformView as Microsoft.UI.Xaml.Window)?.AppWindow?.TitleBar is not { } bar) return;

        var transparent = Microsoft.UI.Colors.Transparent;
        var primary = CaptionColors.ToWinUI(CaptionColors.TextPrimary);
        bar.ButtonBackgroundColor = transparent;
        bar.ButtonInactiveBackgroundColor = transparent;
        bar.ButtonForegroundColor = primary;
        bar.ButtonInactiveForegroundColor = CaptionColors.ToWinUI(CaptionColors.TextSecondary);
        bar.ButtonHoverBackgroundColor = CaptionColors.ToWinUI(CaptionColors.BackgroundAlt);
        bar.ButtonHoverForegroundColor = primary;
        bar.ButtonPressedBackgroundColor = CaptionColors.ToWinUI(CaptionColors.BackgroundRaised);
        bar.ButtonPressedForegroundColor = primary;
    }

    private static void Place(Microsoft.UI.Windowing.AppWindow appWindow, ShellWindowBounds? bounds)
    {
        if (bounds is null) return;

        // Moved first, then sized: landing on a monitor of another scale rescales the
        // window, and the size set after the move is the one that stays.
        appWindow.Move(new Windows.Graphics.PointInt32(bounds.X, bounds.Y));
        appWindow.Resize(new Windows.Graphics.SizeInt32(bounds.Width, bounds.Height));
        if (bounds.Maximized && appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.Maximize();
    }

    /// <summary>
    /// Keeps where the window stands now. A minimised window keeps nothing: it has no
    /// place on screen to come back to. A maximised one keeps the rectangle already kept
    /// and marks it maximised, since the system reports the maximised rectangle, not the
    /// one it restores to.
    /// </summary>
    /// <param name="restoring">True for the keep straight after placing, which records
    /// the rectangle the window restores to and leaves the maximised state as kept.</param>
    private void Keep(Microsoft.UI.Windowing.AppWindow appWindow, string key, string surfaceId, bool restoring = false)
    {
        var state = (appWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter)?.State;
        if (state is Microsoft.UI.Windowing.OverlappedPresenterState.Minimized) return;

        var position = appWindow.Position;
        var size = appWindow.Size;
        var current = new ShellWindowBounds(position.X, position.Y, size.Width, size.Height, Maximized: false);
        var maximized = state is Microsoft.UI.Windowing.OverlappedPresenterState.Maximized;

        if (restoring)
        {
            if (!maximized)
            {
                windowBounds.Set(surfaceId, current with { Maximized = windowBounds.Get(surfaceId)?.Maximized ?? false });
                KeepForWindow(key, previous => current with { Maximized = previous?.Maximized ?? false });
            }

            return;
        }

        var bounds = maximized
            ? (windowBounds.Get(surfaceId) ?? current) with { Maximized = true }
            : current;

        windowBounds.Set(surfaceId, bounds);
        KeepForWindow(key, previous => maximized ? (previous ?? current) with { Maximized = true } : current);
    }

    /// <summary>
    /// Keeps the bounds <paramref name="next"/> makes of this window's own last ones,
    /// by the same rules the surface's follow, and writes them to the window's
    /// remembered entry when it has one: a remembered window reopens where it stood,
    /// not where another window of its surface did.
    /// </summary>
    private void KeepForWindow(string key, Func<ShellWindowBounds?, ShellWindowBounds> next)
    {
        ShellWindowBounds kept;
        lock (_gate)
        {
            // A window that has already gone keeps nothing for itself. Its shown
            // surface outlives the main window's close, which lets go of every
            // window before they are destroyed, so a quit still keeps where it stood.
            if (!_showing.ContainsKey(key)) return;

            kept = next(_placed.GetValueOrDefault(key));
            _placed[key] = kept;
        }

        if (windowBounds.GetRemembered(key) is { } remembered) windowBounds.Remember(remembered with { Bounds = kept });
    }

    /// <summary>The work area of every monitor connected now, the primary one first.
    /// Read by index: enumerating the list <c>DisplayArea.FindAll</c> answers fails
    /// in some Windows App SDK releases.</summary>
    private static List<ScreenArea> WorkAreas()
    {
        var areas = new List<ScreenArea>();
        var primary = Microsoft.UI.Windowing.DisplayArea.Primary;
        if (primary is not null) areas.Add(ToScreenArea(primary));

        var all = Microsoft.UI.Windowing.DisplayArea.FindAll();
        for (var i = 0; i < all.Count; i++)
        {
            var area = ToScreenArea(all[i]);
            if (!areas.Contains(area)) areas.Add(area);
        }

        return areas;
    }

    private static ScreenArea ToScreenArea(Microsoft.UI.Windowing.DisplayArea display) =>
        new(display.WorkArea.X, display.WorkArea.Y, display.WorkArea.Width, display.WorkArea.Height);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
#endif

    /// <summary>A popped-out window's title: its surface, then the app.</summary>
    private static string WindowTitle(string surfaceTitle) =>
        DecoratedTitle(string.Equals(surfaceTitle, AppTitle, StringComparison.Ordinal) ? AppTitle : $"{surfaceTitle} - {AppTitle}");

    // Debug builds are the ones started out of a worktree, often several at
    // once, so they say which worktree they came from. An installed app has no
    // checkout above it and nothing to disambiguate, and keeps the plain name.
    private static string DecoratedTitle(string title) =>
#if DEBUG
        DevelopmentWorkspace.DecorateTitle(title);
#else
        title;
#endif

    private static void OnMainThread(Action action)
    {
        if (MainThread.IsMainThread) action();
        else MainThread.BeginInvokeOnMainThread(action);
    }
}
