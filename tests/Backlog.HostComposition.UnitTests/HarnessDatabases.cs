using Backlog.Infrastructure.Sqlite;

namespace Backlog.HostComposition.UnitTests;

/// <summary>
/// Lets go of the SQLite databases a host opened under a folder of the test's own, once
/// the host is gone.
/// <para>
/// The repositories pool their connections, so a host that was disposed still leaves an
/// open handle on every database it wrote. Left alone, Microsoft.Data.Sqlite closes them
/// all in its process-exit handler, one after another, each close checkpointing its WAL:
/// about a hundred harness workspaces on a run of this assembly, long enough on a busy
/// runner that xUnit gave up waiting for the process to end and failed a green run with
/// "Foreground threads were left running" (nightly run 45400985089). The open handles
/// also kept those workspaces from being deleted at all.
/// </para>
/// <para>
/// Only these databases' pools are cleared, never every pool in the process:
/// <c>SqlitePoolClearingTests</c> says why.
/// </para>
/// </summary>
internal static class HarnessDatabases
{
    /// <summary>Releases every database under <paramref name="folder"/>. The paths come
    /// back built on <paramref name="folder"/> exactly as the host was given it, which is
    /// what the pool is keyed on. A database opened unpooled has no pool, and asking for
    /// it clears nothing.</summary>
    public static void Release(string folder)
    {
        if (!Directory.Exists(folder)) return;

        foreach (var database in Directory.EnumerateFiles(folder, "*.db", SearchOption.AllDirectories))
        {
            SqliteDatabaseFile.ReleasePool(database);
        }
    }
}
