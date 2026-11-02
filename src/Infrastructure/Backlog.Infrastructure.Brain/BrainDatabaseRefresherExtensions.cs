using Backlog.Infrastructure.Devbook;

namespace Backlog.Infrastructure.Brain;

/// <summary>The refresher's watch over the Brain folder, under the Brain's
/// profile.</summary>
public static class BrainDatabaseRefresherExtensions
{
    /// <summary>Watches the Brain folder at <paramref name="brainRoot"/> under
    /// <see cref="BrainCorpusProfile"/> (<see cref="DevbookDatabaseRefresher.Watch"/>).</summary>
    public static IDisposable WatchBrain(this DevbookDatabaseRefresher refresher, string brainRoot)
    {
        ArgumentNullException.ThrowIfNull(refresher);
        return refresher.Watch(brainRoot, BrainCorpusProfile.Instance);
    }
}
