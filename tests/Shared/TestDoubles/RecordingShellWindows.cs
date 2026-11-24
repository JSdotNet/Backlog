using Backlog.Desktop.UI.Shell;

namespace Backlog.Tests;

/// <summary>
/// The head's windows, recorded: every window asked for, every title, close,
/// activation, pin and remember, and what each window last reported showing. Each
/// open is answered with <paramref name="answer"/> — built, unless a test says
/// otherwise.
/// </summary>
/// <param name="answer">What every <see cref="OpenAsync"/> answers; built when omitted.</param>
internal sealed class RecordingShellWindows(Task<bool>? answer = null) : IShellWindows
{
    private static readonly IReadOnlyDictionary<string, string> NoQuery = new Dictionary<string, string>();

    /// <summary>Every window asked for, in order, with the query it was handed or an
    /// empty one.</summary>
    public List<(string SurfaceId, IReadOnlyDictionary<string, string> Query)> Opened { get; } = [];

    /// <summary>Every call, in order: the surface for an open, <c>main</c> for a
    /// call that brings the main window back.</summary>
    public List<string> Calls { get; } = [];

    public Dictionary<string, string> Titles { get; } = new(StringComparer.Ordinal);

    public List<string> Closed { get; } = [];

    public int Activations { get; private set; }

    public bool CanRemember { get; init; }

    public List<string> Remembered { get; } = [];

    /// <summary>The surface and the query, keys in order, last reported.</summary>
    public (string SurfaceId, string Query)? LastShowing { get; private set; }

    public bool CanKeepOnTop { get; init; }

    public HashSet<string> OnTop { get; } = new(StringComparer.Ordinal);

    public void Open(string surfaceId, IReadOnlyDictionary<string, string>? query = null)
    {
        Calls.Add(surfaceId);
        Opened.Add((surfaceId, query ?? NoQuery));
    }

    public Task<bool> OpenAsync(string surfaceId, IReadOnlyDictionary<string, string>? query = null)
    {
        Open(surfaceId, query);
        return answer ?? Task.FromResult(true);
    }

    public void Close(string windowKey) => Closed.Add(windowKey);

    public void SetTitle(string windowKey, string title) => Titles[windowKey] = title;

    public void ActivateMainWindow()
    {
        Calls.Add("main");
        Activations++;
    }

    public bool IsRemembered(string windowKey) => Remembered.Contains(windowKey);

    public void SetRemembered(string windowKey, bool remembered)
    {
        Remembered.Remove(windowKey);
        if (remembered) Remembered.Add(windowKey);
    }

    public void ReportShowing(string windowKey, string surfaceId, IReadOnlyDictionary<string, string> query) =>
        LastShowing = (surfaceId, string.Join(';', query
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}")));

    public bool IsOnTop(string windowKey) => OnTop.Contains(windowKey);

    public void SetOnTop(string windowKey, bool onTop)
    {
        if (onTop) OnTop.Add(windowKey);
        else OnTop.Remove(windowKey);
    }
}
