namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// Tests that call <c>SqliteConnection.ClearAllPools()</c>, which clears every
/// SQLite pool in the process. In Microsoft.Data.Sqlite 10.0.11 a clear can
/// dispose the handle of a connection another test is opening at that moment
/// (dotnet/efcore#38854, fixed by #39012 in no released package yet), so these
/// run with nothing else in flight. The Desktop UI suite's collection of the
/// same name says more; <c>ArchitectureTests</c> keeps every caller in one.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlitePoolClearingCollection
{
    public const string Name = "SQLite pool clearing";
}
