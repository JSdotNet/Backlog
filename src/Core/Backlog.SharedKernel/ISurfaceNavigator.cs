namespace Backlog.SharedKernel;

/// <summary>
/// How a surface jumps to another — "Open task", "Open in Devbook" — in the window
/// it was clicked in (local ADR 0042 §3). One per window: the hosts register it with
/// the window-state lifetime, so the window whose shell listens is the one that
/// switches. It replaces the callbacks the shell used to pass each pane.
/// </summary>
public interface ISurfaceNavigator
{
    /// <summary>Raised for every <see cref="Show"/>; the window's shell answers it.</summary>
    event Action<SurfaceRequest>? ShowRequested;

    /// <summary>Asks this window to show the surface <paramref name="surfaceId"/>,
    /// with the query keys that surface accepts.</summary>
    void Show(string surfaceId, IReadOnlyDictionary<string, string>? query = null);
}

/// <summary>A surface asked for through <see cref="ISurfaceNavigator.Show"/>.</summary>
/// <param name="SurfaceId">The surface's id.</param>
/// <param name="Query">The query it was asked with; empty when none.</param>
public sealed record SurfaceRequest(string SurfaceId, IReadOnlyDictionary<string, string> Query)
{
    /// <summary>The value of <paramref name="key"/>, or null when the request
    /// carries none.</summary>
    public string? Value(string key) => Query.TryGetValue(key, out var value) ? value : null;
}

/// <summary>The navigator a window's shell listens to.</summary>
public sealed class SurfaceNavigator : ISurfaceNavigator
{
    private static readonly IReadOnlyDictionary<string, string> NoQuery =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public event Action<SurfaceRequest>? ShowRequested;

    public void Show(string surfaceId, IReadOnlyDictionary<string, string>? query = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceId);
        ShowRequested?.Invoke(new SurfaceRequest(surfaceId, query ?? NoQuery));
    }
}
