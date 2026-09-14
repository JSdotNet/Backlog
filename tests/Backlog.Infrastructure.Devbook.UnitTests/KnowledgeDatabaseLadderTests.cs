using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Knowledge.UnitTests;

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
public class KnowledgeDatabaseLadderTests
{
    [Fact]
    public void Rung_one_a_current_row_is_served_from_the_database()
    {
        using var temporary = new TemporaryDatabase();
        KnowledgeCorpus.Seed(temporary.DatabaseFile);

        using var database = KnowledgeDatabase.TryOpen(temporary.DatabaseFile);
        Assert.NotNull(database);

        var states = database.FileStates(KnowledgeCorpus.Scope);
        var state = states[KnowledgeCorpus.ChapterPath];

        Assert.False(state.HasDrifted(FileFor(temporary)));
        Assert.Equal(KnowledgeCorpus.ChapterText, Assert.Single(database.Chapters(KnowledgeCorpus.ChapterPath)).Text);
    }

    [Fact]
    public void Rung_two_a_file_written_since_the_build_has_drifted()
    {
        using var temporary = new TemporaryDatabase();
        KnowledgeCorpus.Seed(temporary.DatabaseFile);

        using var database = KnowledgeDatabase.TryOpen(temporary.DatabaseFile);
        Assert.NotNull(database);

        var file = FileFor(temporary);
        File.AppendAllText(file, "\nAnd one more paragraph the database has never seen.\n");

        Assert.True(database.FileStates(KnowledgeCorpus.Scope)[KnowledgeCorpus.ChapterPath].HasDrifted(file));
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
        KnowledgeCorpus.Seed(temporary.DatabaseFile);

        using var database = KnowledgeDatabase.TryOpen(temporary.DatabaseFile);
        Assert.NotNull(database);

        var file = FileFor(temporary);
        File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file).AddMinutes(17));

        Assert.False(database.FileStates(KnowledgeCorpus.Scope)[KnowledgeCorpus.ChapterPath].HasDrifted(file));
    }

    /// <summary>A file the database lists but that is no longer on disk is not
    /// drifted, it is gone. Reporting it as drift would send a caller off to parse
    /// Markdown that is not there.</summary>
    [Fact]
    public void Rung_two_a_deleted_file_is_gone_rather_than_drifted()
    {
        using var temporary = new TemporaryDatabase();
        KnowledgeCorpus.Seed(temporary.DatabaseFile);

        using var database = KnowledgeDatabase.TryOpen(temporary.DatabaseFile);
        Assert.NotNull(database);

        var file = FileFor(temporary);
        File.Delete(file);

        Assert.False(database.FileStates(KnowledgeCorpus.Scope)[KnowledgeCorpus.ChapterPath].HasDrifted(file));
    }

    [Fact]
    public void Rung_three_an_unrecognised_schema_version_is_ignored_entirely()
    {
        using var temporary = new TemporaryDatabase();
        KnowledgeSchemaSource.Create(temporary.DatabaseFile, KnowledgeDatabaseSchema.Version + 1);

        Assert.Null(KnowledgeDatabase.TryOpen(temporary.DatabaseFile));
    }

    [Fact]
    public void Rung_three_a_database_declaring_no_schema_version_is_ignored_entirely()
    {
        using var temporary = new TemporaryDatabase();
        KnowledgeSchemaSource.Create(temporary.DatabaseFile);

        Assert.Null(KnowledgeDatabase.TryOpen(temporary.DatabaseFile));
    }

    [Fact]
    public void Rung_four_an_absent_database_reads_as_no_database()
    {
        using var temporary = new TemporaryDatabase();

        Assert.Null(KnowledgeDatabase.TryOpen(temporary.DatabaseFile));
        Assert.Null(KnowledgeDatabase.TryOpen(null));
        Assert.Null(KnowledgeDatabase.TryOpenForFolder(temporary.Folder(".domain")));
    }

    /// <summary>
    /// Not a database at all: a half-written file, a text file somebody put there,
    /// a rebuild caught mid-rename. SQLite answers with an error and the reader
    /// answers with the Markdown path, which is what a knowledge panel did before
    /// any index existed.
    /// </summary>
    [Fact]
    public void Rung_four_an_unreadable_database_reads_as_no_database()
    {
        using var temporary = new TemporaryDatabase();
        File.WriteAllText(temporary.DatabaseFile, "this is not a database");

        Assert.Null(KnowledgeDatabase.TryOpen(temporary.DatabaseFile));
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
            KnowledgeSchemaSource.Execute(connection, "CREATE TABLE something_else (id INTEGER PRIMARY KEY)");
            SqliteConnection.ClearPool(connection);
        }

        Assert.Null(KnowledgeDatabase.TryOpen(temporary.DatabaseFile));
    }

    [Fact]
    public void Rung_five_a_database_without_embeddings_still_answers_full_text()
    {
        using var temporary = new TemporaryDatabase();
        KnowledgeSchemaSource.Create(temporary.DatabaseFile, KnowledgeDatabaseSchema.Version);

        using var database = KnowledgeDatabase.TryOpen(temporary.DatabaseFile);
        Assert.NotNull(database);

        Assert.False(database.HasEmbeddings);
        Assert.Null(database.EmbeddingModel);
        Assert.Equal(KnowledgeRetrievalTier.FullText, database.Retrieval);
    }

    [Fact]
    public void Rung_five_a_database_with_embeddings_answers_both_tiers()
    {
        using var temporary = new TemporaryDatabase();
        var seeded = KnowledgeCorpus.Seed(temporary.DatabaseFile);

        using var database = KnowledgeDatabase.TryOpen(temporary.DatabaseFile);
        Assert.NotNull(database);

        Assert.True(database.HasEmbeddings);
        Assert.Equal(seeded.EmbeddingModel, database.EmbeddingModel);
        Assert.Equal(KnowledgeRetrievalTier.FullTextAndSemantic, database.Retrieval);
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
        Assert.Equal(KnowledgeRetrievalTier.Unavailable, KnowledgeRetrieval.TierFor(null));

        var message = KnowledgeRetrieval.UnavailableMessage("Knowledge search");

        Assert.Contains("Knowledge search", message, StringComparison.Ordinal);
        Assert.Contains(KnowledgeRetrieval.BuildCommand, message, StringComparison.Ordinal);
    }

    private static string FileFor(TemporaryDatabase temporary) => temporary.Resolve(KnowledgeCorpus.ChapterPath);
}
