namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Tests that call <c>SqliteConnection.ClearAllPools()</c>, which clears the pool
/// of every SQLite file in the process — every other test's included.
///
/// <para>Microsoft.Data.Sqlite 10.0.11 has a race there (dotnet/efcore#38854): a
/// pooled connection that is being handed to an opening caller briefly reads as
/// leaked, and a <c>Clear()</c> landing in that window reclaims it and disposes
/// its handle. The opener then fails with <c>ObjectDisposedException:
/// 'SQLitePCL.sqlite3'</c> in whichever unrelated test it was — a roadmap band
/// saving its plan, typically — and only under load. Running the tests that
/// clear every pool with nothing else in flight takes the victim away. The fix
/// (dotnet/efcore#39012) is on release/10.0 but in no released package yet;
/// once the pinned version carries it this collection can go.</para>
///
/// <para>A test that only needs its own database's handles gone can call
/// <c>SqliteConnection.ClearPool</c> on a connection with that database's
/// connection string instead, and stay parallel. <c>ArchitectureTests</c>
/// keeps every <c>ClearAllPools</c> caller in a non-parallel collection.</para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlitePoolClearingCollection
{
    public const string Name = "SQLite pool clearing";
}
