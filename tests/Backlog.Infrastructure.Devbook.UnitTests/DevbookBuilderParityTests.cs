using System.Diagnostics;
using System.Globalization;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// The app's builder against the Node writer, table by table, on this
/// repository's own <c>.devbook/</c>.
///
/// <para>Local ADR 0015 made the desktop app the writer of the database it reads
/// and kept <c>tools/devbook/build-database.mjs</c> as the reference: it imports
/// the devbook plugin's own generator, so when a plugin release changes how a
/// chapter parses, that script's output changes and this test fails. That is the
/// only thing keeping the two parsers in step, so it compares every table either
/// writer fills except <c>meta</c> (timestamps, and a fingerprint only the app
/// records) and <c>problem</c> (the Node writer carries the generator's lint, the
/// app deliberately does not) — every column, every row, and the value types, so
/// an integer written as a real is a difference too.</para>
///
/// <para>It needs Node 22.5 or later on the path, for <c>node:sqlite</c>; the .NET
/// job in CI installs it. Without Node this fails rather than skipping, because a
/// skipped comparison is a green build that compared nothing.</para>
/// </summary>
public class DevbookBuilderParityTests : IDisposable
{
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "backlog-devbook-parity", Guid.NewGuid().ToString("N"));

    public DevbookBuilderParityTests() => Directory.CreateDirectory(_scratch);

    [Fact]
    public void The_app_builds_the_same_database_as_the_node_writer()
    {
        var repository = RepositoryRoot.Root.FullName;
        var fromNode = Path.Combine(_scratch, "node.db");
        var fromApp = Path.Combine(_scratch, "app.db");

        RunNodeWriter(repository, fromNode);
        Assert.True(DevbookDatabaseBuilder.Build(repository, fromApp, TestContext.Current.CancellationToken));

        using var node = Open(fromNode);
        using var app = Open(fromApp);

        var differences = new List<string>();
        foreach (var (table, order) in (IEnumerable<(string, string)>)
            [
                ("node", "id"),
                ("node_attribute", "node_id, name, value"),
                ("edge", "id, type, source, target"),
                ("outline_entry", "id"),
                ("chapter", "id"),
                ("archify_artifact", "chapter_path, fence_hash, ordinal"),
                ("chapter_embedding", "content_hash")
            ])
        {
            differences.AddRange(Compare(table, Rows(node, table, order), Rows(app, table, order)));
        }

        differences.AddRange(Compare("chapter_fts MATCH devbook", Fts(node, "devbook"), Fts(app, "devbook")));

        Assert.True(
            differences.Count == 0,
            $"The C# builder and {GeneratorName} disagree ({differences.Count} difference(s), first 20 shown):\n"
            + string.Join('\n', differences.Take(20)));

        // And the comparison compared something.
        Assert.True(Rows(app, "chapter", "id").Count > 500, "this repository's corpus came out nearly empty");
    }

    private const string GeneratorName = "tools/devbook/build-database.mjs";

    private static void RunNodeWriter(string repository, string target)
    {
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(Path.Combine(repository, "tools", "devbook", "build-database.mjs"));
        start.ArgumentList.Add("--root");
        start.ArgumentList.Add(repository);
        start.ArgumentList.Add("--out");
        start.ArgumentList.Add(target);

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("node did not start");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            Assert.Fail($"This comparison needs Node 22.5 or later on the PATH to run {GeneratorName}: {exception.Message}");
            return;
        }

        using (process)
        {
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();

            Assert.True(process.WaitForExit(TimeSpan.FromMinutes(2)), $"{GeneratorName} did not finish in two minutes.");
            Assert.True(process.ExitCode == 0, $"{GeneratorName} failed ({process.ExitCode}):\n{error.Result}\n{output.Result}");
        }
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static List<string> Rows(SqliteConnection connection, string table, string order)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {table} ORDER BY {order}";

        var rows = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var cells = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                cells[i] = reader.IsDBNull(i)
                    ? $"{reader.GetName(i)}=NULL"
                    : $"{reader.GetName(i)}={reader.GetDataTypeName(i)}:{Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture)}";
            }

            rows.Add(string.Join(" | ", cells));
        }

        return rows;
    }

    private static List<string> Fts(SqliteConnection connection, string term)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT rowid FROM chapter_fts WHERE chapter_fts MATCH $term ORDER BY rowid";
        command.Parameters.AddWithValue("$term", term);

        var rows = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) rows.Add(reader.GetInt64(0).ToString(CultureInfo.InvariantCulture));
        return rows;
    }

    private static IEnumerable<string> Compare(string table, List<string> expected, List<string> actual)
    {
        if (expected.Count != actual.Count)
        {
            yield return $"{table}: {expected.Count} row(s) from node, {actual.Count} from the app";
        }

        var missing = expected.Except(actual, StringComparer.Ordinal).Take(5);
        var extra = actual.Except(expected, StringComparer.Ordinal).Take(5);

        foreach (var row in missing) yield return $"{table}: only node wrote   {row}";
        foreach (var row in extra) yield return $"{table}: only the app wrote {row}";

        if (expected.Count == actual.Count && !expected.Except(actual, StringComparer.Ordinal).Any())
        {
            for (var i = 0; i < expected.Count; i++)
            {
                if (!string.Equals(expected[i], actual[i], StringComparison.Ordinal))
                {
                    yield return $"{table}: same rows, different order from row {i}";
                    break;
                }
            }
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_scratch, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp folder that outlives the run is not a test failure.
        }
    }
}
