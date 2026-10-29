using Backlog.Aspire.ServiceDefaults;
using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.FileSystem;
using Backlog.SharedKernel;

namespace Backlog.Desktop.Services;

/// <summary>
/// The desktop head's windows (local ADR 0041 §4, §5): the main window
/// <see cref="App.CreateWindow"/> asks for, and every popped-out window
/// <see cref="Open"/> adds beside it, each a <see cref="MainPage"/> on its own start
/// path with a <c>BlazorWebView</c> — and so a scope — of its own.
/// <list type="bullet">
/// <item>Every window's <see cref="Window.Destroying"/> flushes
/// <see cref="ShellNavigationStore"/>, not only the main one's.</item>
/// <item>Closing the main window closes every popped-out one and quits the app: the
/// main window owns the app's life, and no layout is kept for the next start.</item>
/// <item>A popped-out window writes its bounds to <see cref="ShellWindowStore"/> when it
/// moves, resizes or closes, and the next window of that surface opens there, clamped to
/// a monitor that is connected now. Nothing reopens at start.</item>
/// </list>
/// A singleton, because the windows outlive every window's scope.
/// </summary>
public sealed class ShellWindows(ShellNavigationStore navigation, ShellWindowStore windowBounds, ISurfaceCatalog catalog) : IShellWindows
{
    private const string AppTitle = "Backlog.Desktop";

    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, Window> _popped = new(StringComparer.Ordinal);
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

        var key = Guid.NewGuid().ToString("N");
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
                application.OpenWindow(Build(surfaceId, key, path, title));
                built.TrySetResult(true);
            }
            catch (Exception)
            {
                lock (_gate)
                {
                    _popped.Remove(key);
                }

                built.TrySetResult(false);
            }
        });

        return built.Task;
    }

    /// <summary>A popped-out window on <paramref name="path"/>, remembered under
    /// <paramref name="key"/> until it closes. Called on the main thread.</summary>
    private Window Build(string surfaceId, string key, string path, string title)
    {
        var window = new Window(new MainPage(path, key)) { Title = WindowTitle(title) };

        RememberBounds(window, surfaceId);

        window.Destroying += (_, _) =>
        {
            bool quitting;
            lock (_gate)
            {
                _popped.Remove(key);
                quitting = _quitting;
            }

            // The main window's close flushed once for every window it closes.
            if (!quitting) Flush();
            else FlushBounds();
        };

        lock (_gate)
        {
            _popped[key] = window;
        }

        return window;
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
    /// what closing the main window means (local ADR 0041 §5).
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
    /// <paramref name="surfaceId"/> stood, clamped to a monitor connected now, and keeps
    /// its bounds as it moves, resizes and closes. Placed before the window is first
    /// shown, so it never flashes up where the system would have put it.
    /// </summary>
    private void RememberBounds(Window window, string surfaceId)
    {
#if WINDOWS
        window.Created += (_, _) =>
        {
            if ((window.Handler?.PlatformView as Microsoft.UI.Xaml.Window)?.AppWindow is not { } appWindow) return;

            Place(appWindow, windowBounds.Get(surfaceId)?.ClampTo(WorkAreas()));

            // Kept once as placed, before anything maximises it: a window maximised
            // before it ever moved still has a rectangle to restore to.
            Keep(appWindow, surfaceId, restoring: true);

            appWindow.Changed += (sender, args) =>
            {
                if (args.DidPositionChange || args.DidSizeChange || args.DidPresenterChange) Keep(sender, surfaceId);
            };
        };

        window.Destroying += (_, _) =>
        {
            if ((window.Handler?.PlatformView as Microsoft.UI.Xaml.Window)?.AppWindow is { } appWindow) Keep(appWindow, surfaceId);
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
    private void Keep(Microsoft.UI.Windowing.AppWindow appWindow, string surfaceId, bool restoring = false)
    {
        var state = (appWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter)?.State;
        if (state is Microsoft.UI.Windowing.OverlappedPresenterState.Minimized) return;

        var position = appWindow.Position;
        var size = appWindow.Size;
        var current = new ShellWindowBounds(position.X, position.Y, size.Width, size.Height, Maximized: false);

        if (restoring)
        {
            if (state is not Microsoft.UI.Windowing.OverlappedPresenterState.Maximized)
            {
                windowBounds.Set(surfaceId, current with { Maximized = windowBounds.Get(surfaceId)?.Maximized ?? false });
            }

            return;
        }

        var bounds = state is Microsoft.UI.Windowing.OverlappedPresenterState.Maximized
            ? (windowBounds.Get(surfaceId) ?? current) with { Maximized = true }
            : current;

        windowBounds.Set(surfaceId, bounds);
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
