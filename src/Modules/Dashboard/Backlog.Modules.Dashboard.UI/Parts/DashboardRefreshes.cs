namespace Backlog.Modules.Dashboard.UI.Parts;

/// <summary>The insights a part reads, so a refresh can reach every part that shows the
/// same figures.</summary>
[Flags]
public enum DashboardSources
{
    None = 0,
    Productivity = 1,
    Tasks = 2,
    Sessions = 4,
    Drift = 8,
    Cost = 16,
    HoursWorked = 32
}

/// <summary>
/// The pane's refresh fan-out (entry 19). A part's refresh drops its insight's cache and
/// reads again; every other part on the pane that reads the same insight then reads
/// again too, from the read the refresh just made.
/// </summary>
/// <remarks>
/// Without it a refresh moved only the part whose control was pressed: refreshing
/// Throughput dropped the shared headline read, and the pull requests tile beside it kept
/// the figure from before. Cascaded by the pane, one per pane, never persisted; a part
/// rendered without a pane (a test, the storybook) gets none and refreshes alone.
/// </remarks>
public sealed class DashboardRefreshes
{
    private readonly List<(DashboardSources Sources, Func<Task> Reload)> _parts = [];

    /// <summary>Joins the fan-out; the returned handle leaves it.</summary>
    public IDisposable Join(DashboardSources sources, Func<Task> reload)
    {
        ArgumentNullException.ThrowIfNull(reload);

        var entry = (sources, reload);
        _parts.Add(entry);

        return new Leave(() => _parts.Remove(entry));
    }

    /// <summary>Asks every other part that reads any of <paramref name="sources"/> to read
    /// again. The part that refreshed reads for itself and is not asked twice.</summary>
    public Task AnnounceAsync(DashboardSources sources, Func<Task> except)
    {
        if (sources == DashboardSources.None) return Task.CompletedTask;

        var reloads = _parts
            .Where(part => (part.Sources & sources) != DashboardSources.None && part.Reload != except)
            .Select(part => part.Reload)
            .ToList()
            // A snapshot before any reload runs: a reload that re-renders can make a part
            // join or leave, and enumerating the live list would then throw.
            .Select(reload => reload())
            .ToList();

        return Task.WhenAll(reloads);
    }

    private sealed class Leave(Action leave) : IDisposable
    {
        private Action? _leave = leave;

        public void Dispose()
        {
            _leave?.Invoke();
            _leave = null;
        }
    }
}
