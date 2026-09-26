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

        var differences = CompareDatabases(fromNode, fromApp);

        Assert.True(
            differences.Count == 0,
            $"The C# builder and {GeneratorName} disagree ({differences.Count} difference(s), first 20 shown):\n"
            + string.Join('\n', differences.Take(20)));

        // And the comparison compared something.
        using var app = Open(fromApp);
        Assert.True(Rows(app, "chapter", "id").Count > 500, "this repository's corpus came out nearly empty");
    }

    /// <summary>
    /// The same comparison on a small corpus built to hold what this repository's
    /// own does not: every reading-order rule of the convention — a missing root,
    /// <c>index: root</c> and <c>index: exclude</c>, a <c>number</c> field, split
    /// files and an invariants subpage, a stray <c>_reading-order.json</c> — and
    /// annotation fences open, resolved, above the first heading, under a repeated
    /// slug and hidden inside another fence. The Node writer builds it with this
    /// repository's installed generator.
    /// </summary>
    [Fact]
    public void The_app_matches_the_node_writer_on_the_convention_s_and_the_annotations_edge_cases()
    {
        var fixture = Path.Combine(_scratch, "fixture");
        foreach (var (path, text) in EdgeCases)
        {
            var file = Path.Combine(fixture, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }

        var fromNode = Path.Combine(_scratch, "fixture-node.db");
        var fromApp = Path.Combine(_scratch, "fixture-app.db");

        RunNodeWriter(fixture, fromNode, generator: Path.Combine(RepositoryRoot.Root.FullName, ".devbook", "_tools", "devbook-meta"));
        Assert.True(DevbookDatabaseBuilder.Build(fixture, fromApp, TestContext.Current.CancellationToken));

        var differences = CompareDatabases(fromNode, fromApp);

        Assert.True(
            differences.Count == 0,
            $"The C# builder and {GeneratorName} disagree on the fixture ({differences.Count} difference(s), first 20 shown):\n"
            + string.Join('\n', differences.Take(20)));

        // And the fixture exercised what it is for.
        using var app = Open(fromApp);
        Assert.Contains(Rows(app, "chapter", "id"), row => row.Contains("open_annotations=INTEGER:2", StringComparison.Ordinal));
        Assert.Contains(Rows(app, "outline_entry", "id"), row => row.Contains("name=TEXT:domain.invariants.md", StringComparison.Ordinal));
    }

    private static readonly (string Path, string Text)[] EdgeCases =
    [
        (".devbook/_reading-order.json", """{ "version": 1, "directories": { ".": { "order": [".devbook/ai"] } } }"""),
        (".devbook/arc42/_reading-order.json", """{ "version": 1, "directories": { ".devbook/arc42": { "root": "10-quality.md", "order": [] } } }"""),
        (".devbook/arc42/10-quality.md", "# 10. Quality\n"),
        (".devbook/arc42/02-constraints.md", "# 02. Constraints\n"),
        (".devbook/arc42/01-introduction.md", "# 01. Introduction\n"),
        (".devbook/arc42/adr/README.md", "# Architecture Decision Records\n"),
        (".devbook/arc42/adr/0002-second.md", "# ADR 0002: Second\n"),
        (".devbook/arc42/adr/0001-first.md", "# ADR 0001: First\n"),
        (".devbook/arc42/adr/renumbered.md", "# Renumbered\n\n```meta\nnumber: 3\n```\n"),
        (".devbook/arc42/tdr/README.md", "# Technical Debt Records\n\n```meta\nindex: root\n```\n"),
        (".devbook/arc42/tdr/0001-debt.md", "# TDR 0001: Debt\n"),
        (".devbook/arc42/tdr/0002-hidden.md", "# TDR 0002: Hidden\n\n```meta\nindex: exclude\n```\n"),
        (".devbook/domain/context-map.md", "# Context Map\n"),
        (".devbook/domain/zeta/context.md", "# Zeta\n"),
        (".devbook/domain/zeta/model.md", "# Zeta\n\n```meta\ntype: aggregate\nstatus: draft\n```\n"),
        (".devbook/domain/zeta/domain.md", """
            Above the first heading.

            ```annotation
            author: ada
            date: 2026-09-01
            body: On the file.
            ```

            # Zeta

            ## Capture

            ```meta
            status: draft
            ```

            ```annotation
            author: ada
            date: 2026-09-01
            body: |
              status: resolved
              is body text, not the status.
            ```

            ```annotation
            status: resolved
            author: bo
            date: 2026-09-02
            body: Settled.
            ```

            ## Capture

            ```annotation
            kind: question
            author: cy
            date: 2026-09-03
            body: Same slug.
            ```

            ## Fenced

            ```mermaid
            flowchart TB
            # Not a heading
            ```

            ```annotation
            status:
            author: di
            date: 2026-09-04
            body: Empty status.
            replies:
              - author: ada
                date: 2026-09-05
                body: Agreed.
            ```

            ````markdown
            ```annotation
            author: ex
            body: Inside another fence.
            ```
            ````
            """),
        (".devbook/domain/zeta/domain.invariants.md", "# Zeta\n"),
        (".devbook/domain/zeta/domain.order.md", "# Zeta\n"),
        (".devbook/domain/zeta/notes.md", "# Zeta\n"),
        (".devbook/domain/alpha/features.checkout.md", "# Alpha\n"),
        (".devbook/domain/alpha/actors.md", "# Alpha\n"),
        (".devbook/tech/tooling.md", "# Tooling\n"),
        (".devbook/tech/cloud.md", "# Cloud\n"),
        (".devbook/tech/technology-graph.md", "# Technology Graph\n"),
        (".devbook/tech/shared.md", "# Shared\n\n```meta\nstatus: adopted\n```\n"),
        (".devbook/design/README.md", "# Design\n"),
        (".devbook/design/content-editing.md", "# Content Editing\n"),
        (".devbook/design/color-scheme.md", "# Color Scheme\n"),
        (".devbook/design/design-principles.md", "# Design Principles\n"),
        (".devbook/ai/concepts.md", "# Concepts\n"),
        (".devbook/ai/02-code.md", "# Code\n"),
        (".devbook/ai/adoption-map.md", "# Adoption Map\n"),
        (".devbook/ai/01-plan.md", "# Plan\n")
    ];

    private static List<string> CompareDatabases(string fromNode, string fromApp)
    {
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
        return differences;
    }

    private const string GeneratorName = "tools/devbook/build-database.mjs";

    private static void RunNodeWriter(string repository, string target, string? generator = null)
    {
        var writer = Path.Combine(RepositoryRoot.Root.FullName, "tools", "devbook", "build-database.mjs");
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false
        };
        start.ArgumentList.Add(writer);
        start.ArgumentList.Add("--root");
        start.ArgumentList.Add(repository);
        start.ArgumentList.Add("--out");
        start.ArgumentList.Add(target);

        if (generator is not null)
        {
            start.ArgumentList.Add("--generator");
            start.ArgumentList.Add(generator);
        }

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
