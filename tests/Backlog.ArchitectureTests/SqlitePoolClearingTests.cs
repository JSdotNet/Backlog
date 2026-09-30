using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// A test that clears every SQLite pool in the process runs with nothing else in
/// flight.
///
/// <para><c>SqliteConnection.ClearAllPools()</c> clears the pools of every test
/// running beside the caller, and in Microsoft.Data.Sqlite 10.0.11 a clear can
/// dispose the handle of a connection another test is opening at that moment
/// (dotnet/efcore#38854). The failure lands in the other test, only under load,
/// as <c>ObjectDisposedException: 'SQLitePCL.sqlite3'</c> — nothing points back
/// at the caller. So the caller is held to a collection that disables
/// parallelization, or clears only its own database's pool with
/// <c>SqliteConnection.ClearPool</c>.</para>
/// </summary>
public partial class SqlitePoolClearingTests
{
    [Fact]
    public void Every_test_that_clears_all_pools_runs_in_a_non_parallel_collection()
    {
        var tests = new DirectoryInfo(Path.Combine(Repository.Root.FullName, "tests"));

        var offenders = Sources(tests)
            .Where(file => file.Name != nameof(SqlitePoolClearingTests) + ".cs")
            .Where(file => CallsClearAllPools(File.ReadAllText(file.FullName)))
            .Where(file => !InNonParallelCollection(file))
            .Select(file => Path.GetRelativePath(Repository.Root.FullName, file.FullName))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"SqliteConnection.ClearAllPools() is called outside a non-parallel collection in:{Environment.NewLine}"
            + string.Join(Environment.NewLine, offenders)
            + $"{Environment.NewLine}Put the class in its project's SqlitePoolClearingCollection, "
            + "or clear only its own database's pool with SqliteConnection.ClearPool.");
    }

    private static IEnumerable<FileInfo> Sources(DirectoryInfo tests) =>
        tests.EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Where(file => !file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    /// <summary>A call, not a mention: comments name the method to explain why
    /// it is not called.</summary>
    private static bool CallsClearAllPools(string source) =>
        source.Split('\n').Any(line =>
            !line.TrimStart().StartsWith("//", StringComparison.Ordinal)
            && line.Contains("ClearAllPools()", StringComparison.Ordinal));

    private static bool InNonParallelCollection(FileInfo file)
    {
        var source = File.ReadAllText(file.FullName);

        return NonParallelCollections(ProjectFolder(file))
            .Any(collection => source.Contains($"[Collection({collection}.Name)]", StringComparison.Ordinal));
    }

    /// <summary>The classes in <paramref name="project"/> declared as a collection
    /// definition with parallelization disabled.</summary>
    private static IEnumerable<string> NonParallelCollections(DirectoryInfo project) =>
        Sources(project)
            .SelectMany(file => NonParallelDefinition().Matches(File.ReadAllText(file.FullName)))
            .Select(match => match.Groups["name"].Value);

    private static DirectoryInfo ProjectFolder(FileInfo file)
    {
        for (var folder = file.Directory; folder is not null; folder = folder.Parent)
        {
            if (folder.EnumerateFiles("*.csproj").Any()) return folder;
        }

        throw new InvalidOperationException($"{file.FullName} is in no project.");
    }

    [GeneratedRegex(@"\[CollectionDefinition\([^\]]*DisableParallelization\s*=\s*true\)\]\s*(?:public|internal)\s+(?:sealed\s+)?class\s+(?<name>\w+)")]
    private static partial Regex NonParallelDefinition();
}
