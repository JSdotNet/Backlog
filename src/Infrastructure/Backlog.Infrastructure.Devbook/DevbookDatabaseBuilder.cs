using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Backlog.Infrastructure.Devbook.Building;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Devbook;

/// <summary>
/// Builds a repository's devbook database — the app's own writer (local
/// ADR 0015).
///
/// <para>It fills every table the schema in <c>tools/devbook/devbook-schema.sql</c>
/// defines, with the rows <c>tools/devbook/build-database.mjs</c> writes for the
/// same corpus: the reference graph, the resolved reading outline for every scope,
/// each chapter's verbatim text, prose and hashes, the full-text index, and the
/// Archify artifact index. The embedding table is created and left empty, as it is
/// there. The two writers are held to each other, table by table, by
/// <c>DevbookBuilderParityTests</c> on this repository's own corpus.</para>
///
/// <para>What it does not do is validate. <c>problem</c> holds what the build
/// could not use — an unreadable Archify index, an entry the reading order does
/// not list — and not the convention's metadata rules, which stay the devbook
/// checker's to report where somebody can fix them.</para>
///
/// <para>The database is built whole into a temporary file beside the target and
/// moved over it, so a reader never opens a half-built one. Readers hold a
/// connection only for one question (<see cref="DevbookDatabase"/>), so the move
/// normally lands at once; if a reader happens to hold the file it is retried
/// briefly, and failing that the old database stays and the next check tries
/// again.</para>
/// </summary>
public static class DevbookDatabaseBuilder
{
    /// <summary>What the <c>meta</c> table records as having written the file.</summary>
    public const string GeneratedBy = "Backlog.Infrastructure.Devbook";

    /// <summary>
    /// The <c>meta</c> key holding a fingerprint of every input the build read —
    /// each Markdown file, reading order and Archify index, by path, size and
    /// modification time. <see cref="IsCurrent"/> recomputes it from a directory
    /// walk and a <c>stat</c> per file, without opening one.
    /// </summary>
    public const string InputsKey = "inputs";

    /// <summary>
    /// Builds the database for <paramref name="repositoryRoot"/> at
    /// <paramref name="target"/>. Returns <see langword="false"/> — writing
    /// nothing — when the repository has no devbook folder at all.
    /// </summary>
    /// <exception cref="IOException">The finished database could not be moved into place.</exception>
    public static bool Build(string repositoryRoot, string target, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        var root = Path.GetFullPath(repositoryRoot);
        var layout = DevbookBuildLayout.Discover(root);
        if (layout.Folders.Count == 0) return false;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        var temporary = $"{target}.building-{Environment.ProcessId}-{Guid.NewGuid():N}";

        try
        {
            Write(root, layout, temporary, cancellationToken);
            DeleteSidecars(temporary);
            MoveIntoPlace(temporary, target, cancellationToken);
        }
        finally
        {
            TryDelete(temporary);
            DeleteSidecars(temporary);
        }

        return true;
    }

    /// <summary>
    /// Whether the database at <paramref name="databasePath"/> was built from
    /// exactly the inputs <paramref name="repositoryRoot"/> holds now, and in a
    /// schema this app reads. Absent, unreadable, another version, written by a
    /// tool that records no fingerprint, or any input added, removed or touched:
    /// not current.
    /// </summary>
    public static bool IsCurrent(string repositoryRoot, string databasePath)
    {
        using var database = DevbookDatabase.TryOpen(databasePath);
        if (database is null) return false;
        if (!database.Meta.TryGetValue(InputsKey, out var recorded)) return false;

        var root = Path.GetFullPath(repositoryRoot);
        return string.Equals(recorded, InputsFingerprint(root, DevbookBuildLayout.Discover(root)), StringComparison.Ordinal);
    }

    /// <summary>Whether the repository has anything to build from.</summary>
    public static bool HasDevbook(string repositoryRoot) =>
        DevbookBuildLayout.Discover(Path.GetFullPath(repositoryRoot)).Folders.Count > 0;

    private static void Write(string root, DevbookBuildLayout layout, string temporary, CancellationToken cancellationToken)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = temporary,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());

        connection.Open();
        Execute(connection, "PRAGMA journal_mode = WAL");
        Execute(connection, DevbookDatabaseSchema.Ddl);

        using var transaction = connection.BeginTransaction();
        var problems = new List<DevbookBuildProblem>();

        var graph = DevbookGraphBuilder.Build(root, layout);
        InsertGraph(connection, transaction, graph);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var scope in layout.Scopes)
        {
            var entries = DevbookOutlineBuilder.Resolve(root, scope, layout, problems);
            InsertOutline(connection, transaction, scope, entries, layout);
        }

        cancellationToken.ThrowIfCancellationRequested();
        InsertChapters(connection, transaction, root, layout, cancellationToken);
        InsertArchify(connection, transaction, root, layout, problems);

        using (var insert = Command(connection, transaction, "INSERT INTO problem (scope, severity, path, message) VALUES ($scope, $severity, $path, $message)"))
        {
            foreach (var problem in problems)
            {
                Bind(insert, ("$scope", problem.Scope), ("$severity", problem.Severity), ("$path", problem.Path), ("$message", problem.Message));
                insert.ExecuteNonQuery();
            }
        }

        using (var meta = Command(connection, transaction, "INSERT INTO meta (key, value) VALUES ($key, $value)"))
        {
            foreach (var (key, value) in (IEnumerable<(string, string)>)
                [
                    (DevbookDatabaseSchema.SchemaVersionKey, DevbookDatabaseSchema.Version.ToString(CultureInfo.InvariantCulture)),
                    (DevbookDatabaseSchema.GeneratedByKey, GeneratedBy),
                    (DevbookDatabaseSchema.GeneratedAtKey, DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)),
                    (DevbookDatabaseSchema.ScopesKey, string.Join(',', layout.Scopes)),
                    (InputsKey, InputsFingerprint(root, layout))
                ])
            {
                Bind(meta, ("$key", key), ("$value", value));
                meta.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    private static void InsertGraph(SqliteConnection connection, SqliteTransaction transaction, DevbookGraph graph)
    {
        using var node = Command(connection, transaction, """
            INSERT INTO node (id, type, label, folder, path, slug, level, line, status, out_of_scope, effort, kind, version, issue)
            VALUES ($id, $type, $label, $folder, $path, $slug, $level, $line, $status, $outOfScope, $effort, $kind, $version, $issue)
            """);
        using var attribute = Command(connection, transaction, "INSERT INTO node_attribute (node_id, name, value) VALUES ($node, $name, $value)");
        using var edge = Command(connection, transaction, "INSERT INTO edge (id, type, source, target) VALUES ($id, $type, $source, $target)");

        foreach (var data in graph.Nodes)
        {
            Bind(node,
                ("$id", data.Id), ("$type", data.Type), ("$label", data.Label), ("$folder", data.Folder),
                ("$path", data.Path), ("$slug", data.Slug), ("$level", data.Level), ("$line", data.Line),
                ("$status", data.Status), ("$outOfScope", data.Folder is null ? 1 : 0), ("$effort", data.Effort),
                ("$kind", data.Kind), ("$version", data.Version), ("$issue", data.Issue));
            node.ExecuteNonQuery();

            foreach (var (name, values) in data.Attributes)
            {
                foreach (var value in values)
                {
                    Bind(attribute, ("$node", data.Id), ("$name", name), ("$value", value));
                    attribute.ExecuteNonQuery();
                }
            }
        }

        foreach (var data in graph.Edges)
        {
            Bind(edge, ("$id", data.Id), ("$type", data.Type), ("$source", data.Source), ("$target", data.Target));
            edge.ExecuteNonQuery();
        }
    }

    private static void InsertOutline(SqliteConnection connection, SqliteTransaction transaction, string scope, IReadOnlyList<DevbookOutlineEntry> entries, DevbookBuildLayout layout)
    {
        using var insert = Command(connection, transaction, """
            INSERT INTO outline_entry (scope, parent_id, ordinal, type, name, path, title, status, kind, is_root)
            VALUES ($scope, $parent, $ordinal, $type, $name, $path, $title, $status, $kind, $root);
            SELECT last_insert_rowid();
            """);

        void Walk(IReadOnlyList<DevbookOutlineEntry> nodes, long? parentId)
        {
            for (var ordinal = 0; ordinal < nodes.Count; ordinal++)
            {
                var entry = nodes[ordinal];
                var kind = entry.Kind ?? layout.FolderKindForPath(entry.Path.EndsWith(".md", StringComparison.Ordinal) ? entry.Path : $"{entry.Path}/x.md");

                Bind(insert,
                    ("$scope", scope), ("$parent", parentId), ("$ordinal", ordinal), ("$type", entry.Type),
                    ("$name", entry.Name), ("$path", entry.Path), ("$title", entry.Title), ("$status", entry.Status),
                    ("$kind", kind), ("$root", entry.IsRoot ? 1 : 0));

                var id = (long)insert.ExecuteScalar()!;
                if (entry.Children.Count > 0) Walk(entry.Children, id);
            }
        }

        Walk(entries, null);
    }

    private static void InsertChapters(SqliteConnection connection, SqliteTransaction transaction, string root, DevbookBuildLayout layout, CancellationToken cancellationToken)
    {
        using var insert = Command(connection, transaction, """
            INSERT INTO chapter (path, folder, slug, level, title, status, line, text, search_text, content_hash, source_hash, size, mtime)
            VALUES ($path, $folder, $slug, $level, $title, $status, $line, $text, $searchText, $contentHash, $sourceHash, $size, $mtime)
            """);

        foreach (var folder in layout.Folders)
        {
            foreach (var relativePath in DevbookBuildFiles.Markdown(root, folder, skipGenerated: true))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var absolute = DevbookBuildFiles.Absolute(root, relativePath);
                var markdown = DevbookBuildFiles.ReadText(root, relativePath);
                var file = new FileInfo(absolute);
                var chapters = DevbookMarkdown.Parse(markdown).Chapters;
                var slices = DevbookChapterText.Slices(markdown, chapters);
                var sourceHash = DevbookMarkdown.Sha256(markdown);
                var folderKind = layout.FolderKindForPath(relativePath);

                for (var index = 0; index < chapters.Count; index++)
                {
                    var chapter = chapters[index];
                    Bind(insert,
                        ("$path", relativePath), ("$folder", folderKind), ("$slug", chapter.Slug), ("$level", chapter.Level),
                        ("$title", chapter.Text), ("$status", chapter.Meta?.GetValueOrDefault("status") as string), ("$line", chapter.Line),
                        ("$text", slices[index].Text), ("$searchText", slices[index].SearchText),
                        ("$contentHash", DevbookMarkdown.Sha256(slices[index].Text)), ("$sourceHash", sourceHash),
                        ("$size", file.Length), ("$mtime", DevbookFileState.UnixMilliseconds(file.LastWriteTimeUtc)));
                    insert.ExecuteNonQuery();
                }
            }
        }

        // External-content FTS5, filled once after the table it mirrors: nothing
        // ever updates a row here.
        Execute(connection, "INSERT INTO chapter_fts (rowid, title, search_text) SELECT id, title, search_text FROM chapter", transaction);
    }

    private static void InsertArchify(SqliteConnection connection, SqliteTransaction transaction, string root, DevbookBuildLayout layout, List<DevbookBuildProblem> problems)
    {
        using var insert = Command(connection, transaction, """
            INSERT INTO archify_artifact (chapter_path, fence_hash, ordinal, type, quality, kind, spec_path, artifact_path, checks_passed, check_count)
            VALUES ($chapter, $fence, $ordinal, $type, $quality, $kind, $spec, $artifact, $passed, $count)
            """);

        foreach (var folder in layout.Folders)
        {
            foreach (var indexPath in DevbookBuildFiles.ArchifyIndexes(root, folder))
            {
                var artifactDirectory = indexPath[..indexPath.LastIndexOf('/')];
                var chapterDirectory = artifactDirectory[..artifactDirectory.LastIndexOf('/')];

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(DevbookBuildFiles.ReadText(root, indexPath));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
                {
                    problems.Add(new DevbookBuildProblem(folder, "warning", indexPath,
                        $"{indexPath} is not readable JSON; its artifacts are absent from the database and their chapters fall back to mermaid."));
                    continue;
                }

                using (document)
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Object
                        || !document.RootElement.TryGetProperty("entries", out var entries)
                        || entries.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    foreach (var property in entries.EnumerateObject())
                    {
                        var entry = property.Value;
                        if (entry.ValueKind != JsonValueKind.Object || String(entry, "chapter") is not { } chapter)
                        {
                            problems.Add(new DevbookBuildProblem(folder, "warning", indexPath,
                                $"{indexPath} entry \"{property.Name}\" names no chapter; re-run tools/diagrams/archify-artifacts.mjs."));
                            continue;
                        }

                        Bind(insert,
                            ("$chapter", $"{chapterDirectory}/{chapter}"), ("$fence", property.Name),
                            ("$ordinal", Number(entry, "ordinal") ?? 0L), ("$type", String(entry, "type")),
                            ("$quality", String(entry, "quality")), ("$kind", String(entry, "kind")),
                            ("$spec", String(entry, "spec") is { Length: > 0 } spec ? $"{artifactDirectory}/{spec}" : null),
                            ("$artifact", String(entry, "artifact") is { Length: > 0 } artifact ? $"{artifactDirectory}/{artifact}" : null),
                            ("$passed", Number(entry, "checksPassed")), ("$count", Number(entry, "checkCount")));
                        insert.ExecuteNonQuery();
                    }
                }
            }
        }

        static string? String(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        // An integral number binds as an integer, as node:sqlite binds one, so the
        // column holds 3 and not 3.0.
        static object? Number(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number) return null;
            return value.TryGetInt64(out var integer) ? integer : value.GetDouble();
        }
    }

    /// <summary>
    /// A fingerprint of every input a build reads: each Markdown file, each
    /// <c>_reading-order.json</c> and each Archify index, by path, size and
    /// modification time — one directory walk and a <c>stat</c> per file.
    /// </summary>
    internal static string InputsFingerprint(string root, DevbookBuildLayout layout)
    {
        var inputs = new List<string>();
        foreach (var folder in layout.Folders)
        {
            inputs.AddRange(DevbookBuildFiles.Markdown(root, folder, skipGenerated: false));
            inputs.AddRange(DevbookBuildFiles.ArchifyIndexes(root, folder));
            inputs.Add($"{folder}/_reading-order.json");
        }

        inputs.Add(layout.RepositoryReadingOrderPath);
        inputs.Sort(StringComparer.Ordinal);

        var text = new StringBuilder();
        text.Append(DevbookDatabaseSchema.Version).Append('\n');
        foreach (var relativePath in inputs.Distinct(StringComparer.Ordinal))
        {
            var file = new FileInfo(DevbookBuildFiles.Absolute(root, relativePath));
            text.Append(relativePath).Append('\t');
            if (file.Exists)
            {
                text.Append(file.Length.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(DevbookFileState.UnixMilliseconds(file.LastWriteTimeUtc).ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                text.Append('-');
            }

            text.Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static void MoveIntoPlace(string temporary, string target, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temporary, target, overwrite: true);
                DeleteSidecars(target);
                return;
            }
            catch (Exception exception) when ((exception is IOException or UnauthorizedAccessException) && attempt < 10)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.Sleep(TimeSpan.FromMilliseconds(100 * attempt));
            }
        }
    }

    private static void DeleteSidecars(string path)
    {
        TryDelete($"{path}-wal");
        TryDelete($"{path}-shm");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left for the next build; a stray temporary file is never read.
        }
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, params (string Name, object? Value)[] values)
    {
        command.Parameters.Clear();
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
