using Backlog.Infrastructure.Sqlite;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Lets go of the backlog databases a test opened under its own temporary
/// folder, before the folder is deleted.
///
/// <para>The repositories pool their connections, so a test that disposed
/// everything it composed still leaves an open handle on every database it
/// wrote: the folder then refuses to delete, and the handle lives until the
/// process exits. There Microsoft.Data.Sqlite closes every pooled connection
/// in turn, each close checkpointing its WAL — several hundred of them on a
/// full run, long enough on a slow CI disk that the runner gave up waiting for
/// its foreground threads and failed a green run.</para>
///
/// <para>Only these databases' pools are cleared, never every pool in the
/// process: see <see cref="SqlitePoolClearingCollection"/> for why that one
/// would have to run alone.</para>
/// </summary>
internal static class TestDatabases
{
    /// <summary>Releases every backlog database under <paramref name="folder"/>.
    /// The paths come back built on <paramref name="folder"/> exactly as the
    /// test passed it to the stores, which is what the pool is keyed on.</summary>
    public static void Release(string folder)
    {
        if (!Directory.Exists(folder)) return;

        foreach (var database in Directory.EnumerateFiles(
                     folder, SqliteTaskRepository.DatabaseFileName, SearchOption.AllDirectories))
        {
            SqliteDatabaseFile.ReleasePool(database);
        }
    }
}
