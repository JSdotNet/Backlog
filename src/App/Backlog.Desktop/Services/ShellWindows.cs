using Backlog.Aspire.ServiceDefaults;
using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.FileSystem;
using Backlog.SharedKernel;

namespace Backlog.Desktop.Services;

/// <summary>
/// The desktop head's windows (local ADR 0040 §4, §5): the main window
/// <see cref="App.CreateWindow"/> asks for, and every popped-out window
/// <see cref="Open"/> adds beside it, each a <see cref="MainPage"/> on its own start
/// path with a <c>BlazorWebView</c> — and so a scope — of its own.
/// <list type="bullet">
/// <item>Every window's <see cref="Window.Destroying"/> flushes
/// <see cref="ShellNavigationStore"/>, not only the main one's.</item>
/// <item>Closing the main window closes every popped-out one and quits the app: the
/// main window owns the app's life, and no layout is kept for the next start.</item>
/// </list>
/// A singleton, because the windows outlive every window's scope.
/// </summary>
public sealed class ShellWindows(ShellNavigationStore navigation, ISurfaceCatalog catalog) : IShellWindows
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
    public void Open(string surfaceId, IReadOnlyDictionary<string, string>? query = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceId);

        var key = Guid.NewGuid().ToString("N");
        var title = catalog.TryGet(surfaceId, out var surface) ? surface.Title : AppTitle;
        var path = SurfaceWindowRoute.PathFor(surfaceId, query);

        // Built on the main thread as well as opened there: the page and its
        // BlazorWebView are controls, and a caller from outside the user interface
        // asks from wherever it runs.
        OnMainThread(() =>
        {
            var window = new Window(new MainPage(path, key)) { Title = WindowTitle(title) };

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
            };

            lock (_gate)
            {
                _popped[key] = window;
            }

            Application.Current?.OpenWindow(window);
        });
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
    /// what closing the main window means (local ADR 0040 §5).
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
    private void Flush() => Task.Run(navigation.Flush).Wait(FlushTimeout);

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
