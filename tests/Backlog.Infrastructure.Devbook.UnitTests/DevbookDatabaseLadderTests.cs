using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// ADR 0004's degradation ladder, one test per rung.
///
/// <para>The ladder is the reason the database is safe to read unattended, so it
/// is not a set of error paths bolted on afterwards — it is the design. Each rung
/// is a defined state with a defined behaviour, and only the last of them is
/// visible to a user. A rung with no test is a rung nobody can tell has stopped
/// working, because every one of them fails by quietly doing something correct but
/// slower.</para>
///
/// <list type="number">
/// <item>Row current for this file — serve from the database.</item>
/// <item>Database present, this file drifted — read that one file's Markdown.</item>
/// <item><c>schemaVersion</c> unrecognised — ignore the database entirely.</item>
/// <item>Database absent, locked or unreadable — read Markdown.</item>
/// <item>Retrieval, no embeddings — full text answers, meaning is absent.</item>
/// <item>Retrieval, no database — search is unavailable, and says so.</item>
/// </list>
/// </summary>
public class DevbookDatabaseLadderTests
{
    [Fact]
    public void Rung_one_a_current_row_is_served_from_the_database()
    {
        using var temporary = new TemporaryDatabase();
        DevbookCorpus.Seed(temporary.DatabaseFile);

        using var database = DevbookDatabase.TryOpen(temporary.DatabaseFile);
        Assert.NotNull(database);

        var states = database.FileStates(DevbookCorpus.Scope);
        var state = states[DevbookCorpus.ChapterPath];

        Assert.False(state.HasDrifted(FileFor(temporary)));
        Assert.Equal(DevbookCorpus.ChapterText, Assert.Single(database.Chapters(DevbookCorpus.ChapterPath)).Text);
    }

    [Fact]
    public void Rung_two_a_file_written_since_the_build_has_drifted()
    {
        using var temporary = new TemporaryDatabase();
        DevbookCorpus.Seed(temporary.DatabaseFile);

        using var database = DevbookDatabase.TryOpen(temporary.DatabaseFile);
        Assert.NotNull(database);

        var file = FileFor(temporary);
        File.AppendAllText(file, "\nAnd one more paragraph the database has never seen.\n");

        Assert.True(database.FileStates(DevbookCorpus.Scope)[DevbookCorpus.ChapterPath].HasDrifted(file));
    }

    /// <summary>
    /// The half of rung two that makes it worth having. A branch switch or a
    /// restore rewrites a file's modification time without changing a word of it,
    /// and treating that as drift would send the panel off to re-parse a file whose
    /// row is exactly right. The hash is what buys the difference, and it is only
    /// read once the cheap facts disagree.
    /// </summary>
    [Fact]
    public void Rung_two_a_touched_file_with_unchanged_content_has_not_drifted()
    {
        using var temporary = new TemporaryDatabase();
        DevbookCorpus.Seed(temporary.DatabaseFile);

        using var database = DevbookDatabase.TryOpen(temporary.DatabaseFile);
        Assert.NotNull(database);

        var file = FileFor(temporary);
        File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file).AddMinutes(17));

        Assert.False(database.FileStates(DevbookCorpus.Scope)[DevbookCorpus.ChapterPath].HasDrifted(file));
    }

    /// <summary>A file the database lists but that is no longer on disk is not
    /// drifted, it is gone. Reporting it as drift would send a caller off to parse
    /// Markdown that is not there.</summary>
    [Fact]
    public void Rung_two_a_deleted_file_is_gone_rather_than_drifted()
    {
        using var temporary = new TemporaryDatabase();
        DevbookCorpus.Seed(temporary.DatabaseFile);

        using var database = DevbookDatabase.TryOpen(temporary.DatabaseFile);
        Assert.NotNull(database);

        var file = FileFor(temporary);
        File.Delete(file);

        Assert.False(database.FileStates(DevbookCorpus.Scope)[DevbookCorpus.ChapterPath].HasDrifted(file));
    }

    [Fact]
    public void Rung_three_an_unrecognised_schema_version_is_ignored_entirely()
    {
        using var temporary = new TemporaryDatabase();
        DevbookSchemaSource.Create(temporary.DatabaseFile, DevbookDatabaseSchema.Version + 1);

        Assert.Null(DevbookDatabase.TryOpen(temporary.DatabaseFile));
    }

    [Fact]
    public void Rung_three_a_database_declaring_no_schema_version_is_ignored_entirely()
    {
        using var temporary = new TemporaryDatabase();
        DevbookSchemaSource.Create(temporary.DatabaseFile);

        Assert.Null(DevbookDatabase.TryOpen(temporary.DatabaseFile));
    }

    [Fact]
    public void Rung_four_an_absent_database_reads_as_no_database()
    {
        using var temporary = new TemporaryDatabase();

        Assert.Null(DevbookDatabase.TryOpen(temporary.DatabaseFile));
        Assert.Null(DevbookDatabase.TryOpen(null));
        Assert.Null(DevbookDatabase.TryOpenForFolder(temporary.Folder(".domain")));
    }

    /// <summary>
    /// Not a database at all: a half-written file, a text file somebody put there,
    /// a rebuild caught mid-rename. SQLite answers with an error and the reader
    /// answers with the Markdown path, which is what a Devbook panel did before
    /// any index existed.
    /// </summary>
    [Fact]
    public void Rung_four_an_unreadable_database_reads_as_no_database()
    {
        using var temporary = new TemporaryDatabase();
        File.WriteAllText(temporary.DatabaseFile, "this is not a database");

        Assert.Null(DevbookDatabase.TryOpen(temporary.DatabaseFile));
    }

    /// <summary>
    /// A database that opens but whose tables are not there — the shape a reader
    /// meets if the writer is interrupted, or if somebody points it at a different
    /// SQLite file. It answers the same way, because the version it cannot read is
    /// the version it does not recognise.
    /// </summary>
    [Fact]
    public void Rung_four_a_database_without_the_schema_reads_as_no_database()
    {
        using var temporary = new TemporaryDatabase();

        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = temporary.DatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString()))
        {
            connection.Open();
            DevbookSchemaSource.Execute(connection, "CREATE TABLE something_else (id INTEGER PRIMARY KEY)");
            SqliteConnection.ClearPool(connection);
        }

        Assert.Null(DevbookDatabase.TryOpen(temporary.DatabaseFile));
    }

    [Fact]
    public void Rung_five_a_database_without_embeddings_still_answers_full_text()
    {
        using var temporary = new TemporaryDatabase();
        DevbookSchemaSource.Create(temporary.DatabaseFile, DevbookDatabaseSchema.Version);

        using var database = DevbookDatabase.TryOpen(temporary.DatabaseFile);
        Assert.NotNull(database);

        Assert.False(database.HasEmbeddings);
        Assert.Null(database.EmbeddingModel);
        Assert.Equal(DevbookRetrievalTier.FullText, database.Retrieval);
    }

    [Fact]
    public void Rung_five_a_database_with_embeddings_answers_both_tiers()
    {
        using var temporary = new TemporaryDatabase();
        var seeded = DevbookCorpus.Seed(temporary.DatabaseFile);

        using var database = DevbookDatabase.TryOpen(temporary.DatabaseFile);
        Assert.NotNull(database);

        Assert.True(database.HasEmbeddings);
        Assert.Equal(seeded.EmbeddingModel, database.EmbeddingModel);
        Assert.Equal(DevbookRetrievalTier.FullTextAndSemantic, database.Retrieval);
    }

    /// <summary>
    /// The one rung a user sees. Browsing degrades to Markdown because it touches
    /// the files on screen; search cannot, because scanning the corpus per query is
    /// a hang rather than a fallback. So the answer is words, and the words name
    /// the command that fixes it.
    /// </summary>
    [Fact]
    public void Rung_six_no_database_means_search_is_unavailable_and_says_so()
    {
        Assert.Equal(DevbookRetrievalTier.Unavailable, DevbookRetrieval.TierFor(null));

        var message = DevbookRetrieval.UnavailableMessage("Devbook search");

        Assert.Contains("Devbook search", message, StringComparison.Ordinal);
        Assert.Contains(DevbookRetrieval.BuildCommand, message, StringComparison.Ordinal);
    }

    // --- The file name from before the rename ---------------------------------

    /// <summary>
    /// The database was <c>_meta/knowledge.db</c> while the context was called
    /// Knowledge. An index built before the rename is still a current index, so a
    /// root holding only the old file resolves to it and reads from it until the
    /// generator writes the new name.
    /// </summary>
    [Fact]
    public void A_root_with_only_the_old_database_name_resolves_and_reads_it()
    {
        using var temporary = new TemporaryDatabase(DevbookDatabaseLocation.LegacyFileName);
        DevbookCorpus.Seed(temporary.DatabaseFile);

        Assert.Equal(temporary.DatabaseFile, DevbookDatabaseLocation.ForRepositoryRoot(temporary.RootDirectory));

        using var database = DevbookDatabase.TryOpenForFolder(temporary.Folder(".domain"));
        Assert.NotNull(database);
        Assert.Equal(DevbookCorpus.ChapterText, Assert.Single(database.Chapters(DevbookCorpus.ChapterPath)).Text);
    }

    [Fact]
    public void A_root_with_both_database_names_resolves_the_current_one()
    {
        using var temporary = new TemporaryDatabase();
        File.WriteAllText(temporary.DatabaseFile, string.Empty);
        File.WriteAllText(Path.Combine(temporary.RootDirectory, "_meta", DevbookDatabaseLocation.LegacyFileName), string.Empty);

        Assert.Equal(temporary.DatabaseFile, DevbookDatabaseLocation.ForRepositoryRoot(temporary.RootDirectory));
    }

    /// <summary>"Absent" resolves to the current name, so a caller's existence check
    /// and the generator's target agree on where the file is going to be.</summary>
    [Fact]
    public void A_root_with_neither_database_name_resolves_to_the_current_one()
    {
        using var temporary = new TemporaryDatabase();

        var resolved = DevbookDatabaseLocation.ForRepositoryRoot(temporary.RootDirectory);

        Assert.Equal(temporary.DatabaseFile, resolved);
        Assert.EndsWith(DevbookDatabaseLocation.FileName, resolved, StringComparison.Ordinal);
    }

    private static string FileFor(TemporaryDatabase temporary) => temporary.Resolve(DevbookCorpus.ChapterPath);
}
