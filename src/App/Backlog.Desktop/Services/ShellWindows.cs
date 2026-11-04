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
/// <item>Closing the main window closes every popped-out one and quits the app: the
/// main window owns the app's life.</item>
/// <item>A popped-out window writes its bounds to <see cref="ShellWindowStore"/> when it
/// moves, resizes or closes, and the next window of that surface opens there, clamped to
/// a monitor that is connected now.</item>
/// <item>Only a window the person pinned reopens at start (<see cref="ReopenRemembered"/>),
/// under the same key, on what it showed at quit and at its own last bounds. Quitting
/// keeps it remembered; the person closing it — its own close button, Show in main
/// window — forgets it.</item>
/// </list>
/// A singleton, because the windows outlive every window's scope.
/// </summary>
public sealed class ShellWindows(ShellNavigationStore navigation, ShellWindowStore windowBounds, ISurfaceCatalog catalog) : IShellWindows
{
    private const string AppTitle = "Backlog.Desktop";

    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, Window> _popped = new(StringComparer.Ordinal);

    /// <summary>What each popped-out window shows now, which a remembered one reopens on.</summary>
    private readonly Dictionary<string, (string SurfaceId, IReadOnlyDictionary<string, string> Query)> _showing = new(StringComparer.Ordinal);

    /// <summary>Where each popped-out window stands, kept for that window alone, as the
    /// per-surface bounds are kept for the surface.</summary>
    private readonly Dictionary<string, ShellWindowBounds> _placed = new(StringComparer.Ordinal);

    private readonly object _gate = new();
    private Window? _main;
    private bool _quitting;

    /// <summary>The main window: <c>/</c>, under the app's own title.</summary>
    public Window CreateMainWindow()
    {
        // One main window per run: this head asks for it once, at start. A second
        // would take over as the window the others are shown in front of.
        var window = new Window(new MainPage()) { Title = DecoratedTitle(AppTitle) };
        lock (_gate)
        {
            _main = window;
        }

        window.Destroying += (_, _) =>
        {
            Flush();
            CloseEveryOtherWindowAndQuit();
        };

        return window;
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
                    _popped.Remove(key);
                    _showing.Remove(key);
                    _placed.Remove(key);
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

        RememberBounds(window, key, surfaceId, reopening);

        // Subscribed after RememberBounds' own, so a remembered window keeps where it
        // stood before a close by the person forgets it.
        window.Destroying += (_, _) =>
        {
            bool quitting;
            lock (_gate)
            {
                _popped.Remove(key);
                _showing.Remove(key);
                _placed.Remove(key);
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
        }

        return window;
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
            return;
        }

        (string SurfaceId, IReadOnlyDictionary<string, string> Query) showing;
        ShellWindowBounds? placed;
        lock (_gate)
        {
            if (!_showing.TryGetValue(windowKey, out showing)) return;
            placed = _placed.GetValueOrDefault(windowKey);
        }

        windowBounds.Remember(new RememberedShellWindow(windowKey, showing.SurfaceId, showing.Query, placed));
    }

    /// <inheritdoc />
    public void ReportShowing(string windowKey, string surfaceId, IReadOnlyDictionary<string, string> query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceId);
        ArgumentNullException.ThrowIfNull(query);

        var showing = (surfaceId, (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(query, StringComparer.Ordinal));
        lock (_gate)
        {
            if (!_popped.ContainsKey(windowKey)) return;
            _showing[windowKey] = showing;
        }

        if (windowBounds.GetRemembered(windowKey) is { } remembered)
            windowBounds.Remember(remembered with { SurfaceId = surfaceId, Query = showing.Item2 });
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
        if (Popped(windowKey) is { } window) OnMainThread(() => window.Title = WindowTitle(title));
    }

    /// <inheritdoc />
    public void ActivateMainWindow()
    {
        Window? main;
        lock (_gate)
        {
            main = _main;
        }

        if (main is not null) OnMainThread(() => Application.Current?.ActivateWindow(main));
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
    /// The main window went: every popped-out window goes with it, and the app quits.
    /// Without the quit the app would live on in the windows beside it, which is not
    /// what closing the main window means (local ADR 0043 §5).
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
        }

        var application = Application.Current;
        if (application is null) return;

        foreach (var window in others) application.CloseWindow(window);

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

#if WINDOWS
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
