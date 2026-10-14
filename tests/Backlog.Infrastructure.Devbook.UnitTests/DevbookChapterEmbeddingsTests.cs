using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// The semantic tier being written (local ADR 0004): the refresher embeds the
/// chapters a database has no vector for, into the database in app storage
/// (local ADR 0015), keyed by <c>content_hash</c> and pinned to one model — and
/// asks nothing when no deployment is there to answer.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class DevbookChapterEmbeddingsTests : IDisposable
{
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "backlog-embeddings", Guid.NewGuid().ToString("N"));

    private string Repository => Path.Combine(_scratch, "repo");

    private string Brain => Path.Combine(_scratch, "Brain");

    private string Cache => Path.Combine(_scratch, "devbook-cache");

    public DevbookChapterEmbeddingsTests()
    {
        Write(Repository, ".devbook/domain/inbox/domain.md", "# Inbox\n\n```meta\nstatus: draft\n```\n\nWhere a capture lands.\n\n## Inbox Item\n\nThe item a capture becomes.\n");
        Write(Repository, ".devbook/arc42/01-introduction.md", "# 01. Introduction\n\nWhy the system exists.\n");
    }

    private string TargetFor(string root) =>
        DevbookDatabaseLocation.PathFor(Path.Combine(Cache, DevbookDatabaseLocation.DatabasesFolderName), root)!;

    [Fact]
    public async Task Every_chapter_is_embedded_into_the_database_under_the_model()
    {
        var embedder = new CountingEmbedder();
        using var refresher = Refresher(embedder);

        await refresher.RefreshAsync(Repository, TestContext.Current.CancellationToken);

        using var database = DevbookDatabase.TryOpen(TargetFor(Repository));
        Assert.NotNull(database);
        Assert.Equal(CountingEmbedder.ModelName, database.EmbeddingModel);
        Assert.True(database.HasEmbeddings);

        var rows = database.Embeddings(CountingEmbedder.ModelName, scope: null);
        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.Equal(CountingEmbedder.Dimensions, row.Vector.Count));
        Assert.Equal(3, embedder.Texts.Count);
        Assert.Contains(embedder.Texts, text => text.StartsWith("Inbox Item\n\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task What_is_read_back_is_the_vector_each_text_was_given()
    {
        var embedder = new CountingEmbedder();
        using var refresher = Refresher(embedder);
        await refresher.RefreshAsync(Repository, TestContext.Current.CancellationToken);

        using var database = DevbookDatabase.TryOpen(TargetFor(Repository));
        var item = Assert.Single(database!.Embeddings(CountingEmbedder.ModelName, scope: null), row => row.Slug == "inbox-item");
        var text = Assert.Single(embedder.Texts, text => text.StartsWith("Inbox Item", StringComparison.Ordinal));

        Assert.Equal(CountingEmbedder.VectorFor(text), item.Vector);
    }

    [Fact]
    public async Task An_unchanged_database_asks_for_nothing_again()
    {
        var embedder = new CountingEmbedder();
        using var refresher = Refresher(embedder);
        await refresher.RefreshAsync(Repository, TestContext.Current.CancellationToken);
        var calls = embedder.Calls;

        await refresher.RefreshAsync(Repository, TestContext.Current.CancellationToken);

        Assert.Equal(calls, embedder.Calls);
    }

    [Fact]
    public async Task A_rebuild_carries_the_vectors_of_unchanged_chapters_and_embeds_only_the_edit()
    {
        var embedder = new CountingEmbedder();
        using var refresher = Refresher(embedder);
        await refresher.RefreshAsync(Repository, TestContext.Current.CancellationToken);
        embedder.Texts.Clear();

        File.AppendAllText(Path.Combine(Repository, ".devbook", "arc42", "01-introduction.md"), "\nAnd who it is for.\n");
        Assert.True(await refresher.RefreshAsync(Repository, TestContext.Current.CancellationToken));

        var text = Assert.Single(embedder.Texts);
        Assert.Contains("who it is for", text, StringComparison.Ordinal);

        using var database = DevbookDatabase.TryOpen(TargetFor(Repository));
        Assert.Equal(3, database!.Embeddings(CountingEmbedder.ModelName, scope: null).Count);
        Assert.Equal(CountingEmbedder.ModelName, database.EmbeddingModel);
        Assert.Equal(3, Count(TargetFor(Repository), "SELECT count(*) FROM chapter_embedding"));
    }

    [Fact]
    public async Task With_no_deployment_configured_nothing_is_called_and_the_words_still_answer()
    {
        var embedder = new CountingEmbedder { Available = false };
        using var refresher = Refresher(embedder);

        Assert.True(await refresher.RefreshAsync(Repository, TestContext.Current.CancellationToken));

        Assert.Equal(0, embedder.Calls);
        using var database = DevbookDatabase.TryOpen(TargetFor(Repository));
        Assert.NotNull(database);
        Assert.False(database.HasEmbeddings);
        Assert.Null(database.EmbeddingModel);
    }

    [Fact]
    public async Task A_refused_batch_ends_the_pass_and_keeps_what_was_written()
    {
        for (var index = 0; index < DevbookChapterEmbeddings.BatchSize; index++)
        {
            Write(Repository, $".devbook/arc42/{index + 10:00}-chapter.md", $"# Chapter {index}\n\nText {index}.\n");
        }

        var embedder = new CountingEmbedder { RefuseAfter = 1 };
        using var refresher = Refresher(embedder);
        await refresher.RefreshAsync(Repository, TestContext.Current.CancellationToken);

        Assert.Equal(2, embedder.Calls);
        Assert.Equal(DevbookChapterEmbeddings.BatchSize, Count(TargetFor(Repository), "SELECT count(*) FROM chapter_embedding"));

        embedder.RefuseAfter = null;
        await refresher.RefreshAsync(Repository, TestContext.Current.CancellationToken);

        Assert.Equal(DevbookChapterEmbeddings.BatchSize + 3, Count(TargetFor(Repository), "SELECT count(*) FROM chapter_embedding"));
    }

    [Fact]
    public async Task Vectors_of_another_model_are_replaced_when_the_model_changes()
    {
        using (var first = Refresher(new CountingEmbedder { Model = "old-model" }))
        {
            await first.RefreshAsync(Repository, TestContext.Current.CancellationToken);
        }

        var embedder = new CountingEmbedder();
        using var refresher = Refresher(embedder);
        await refresher.RefreshAsync(Repository, TestContext.Current.CancellationToken);

        Assert.Equal(3, embedder.Texts.Count);
        using var database = DevbookDatabase.TryOpen(TargetFor(Repository));
        Assert.Empty(database!.Embeddings("old-model", scope: null));
        Assert.Equal(3, database.Embeddings(CountingEmbedder.ModelName, scope: null).Count);
        Assert.Equal(CountingEmbedder.ModelName, database.EmbeddingModel);
    }

    /// <summary>ADR 0026 §4: the Brain is embedded like any devbook, and nothing
    /// derived lands in the Brain folder.</summary>
    [Fact]
    public async Task The_Brain_is_embedded_in_app_storage_and_nothing_is_written_into_its_folder()
    {
        Write(Brain, "README.md", "# My Brain\n\n```meta\nindex: root\n```\n");
        Write(Brain, "CONVENTIONS.md", "# Conventions\n\n## Areas\n\n| Area | Folder |\n|---|---|\n| Work | work |\n");
        Write(Brain, "work/standup.md", "# Standup\n\nWhat moved yesterday.\n");
        var before = Directory.GetFiles(Brain, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();

        DevbookDatabaseLocation.RegisterBrainRoot(Brain);
        try
        {
            using var refresher = Refresher(new CountingEmbedder());
            await refresher.RefreshAsync(Brain, TestContext.Current.CancellationToken);

            Assert.True(Count(TargetFor(Brain), "SELECT count(*) FROM chapter_embedding") > 0);
            Assert.StartsWith(Cache, TargetFor(Brain), StringComparison.Ordinal);
            Assert.Equal(before, Directory.GetFiles(Brain, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());
        }
        finally
        {
            DevbookDatabaseLocation.RegisterBrainRoot(null);
        }
    }

    [Fact]
    public async Task An_embedder_that_throws_costs_the_vectors_and_never_the_build()
    {
        var rebuilt = 0;
        using var refresher = Refresher(new CountingEmbedder { Throw = true });
        refresher.Rebuilt += _ => rebuilt++;

        Assert.True(await refresher.RefreshAsync(Repository, TestContext.Current.CancellationToken));

        Assert.Equal(1, rebuilt);
        using var database = DevbookDatabase.TryOpen(TargetFor(Repository));
        Assert.NotNull(database);
        Assert.False(database.HasEmbeddings);
    }

    [Fact]
    public void A_long_chapter_is_cut_to_the_limit_and_a_title_leads()
    {
        Assert.Equal("Title\n\nBody", DevbookChapterEmbeddings.TextFor("Title", "Body"));
        Assert.Equal("Body", DevbookChapterEmbeddings.TextFor(null, " Body "));
        Assert.Equal(DevbookChapterEmbeddings.MaxCharacters, DevbookChapterEmbeddings.TextFor("T", new string('x', 20_000)).Length);
    }

    private DevbookDatabaseRefresher Refresher(IDevbookChapterEmbedder embedder) =>
        new(() => Cache, quietInterval: TimeSpan.FromMinutes(5), embedder: () => embedder);

    private static long Count(string databasePath, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private static void Write(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_scratch)) Directory.Delete(_scratch, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp folder left behind is harmless.
        }
    }

    /// <summary>A deterministic stand-in: each text's vector is a function of its
    /// characters, so a question that is a chapter's exact text lands on it.</summary>
    private sealed class CountingEmbedder : IDevbookChapterEmbedder
    {
        public const string ModelName = DevbookEmbeddingModel.Default;
        public const int Dimensions = 8;

        public string Model { get; init; } = ModelName;

        public bool Available { get; init; } = true;

        public int? RefuseAfter { get; set; }

        public bool Throw { get; init; }

        public int Calls { get; private set; }

        public List<string> Texts { get; } = [];

        public bool IsAvailable => Available;

        public Task<IReadOnlyList<float[]>?> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Throw) throw new TaskCanceledException("The client's own timeout.");
            if (RefuseAfter is { } limit && Calls > limit) return Task.FromResult<IReadOnlyList<float[]>?>(null);

            Texts.AddRange(texts);
            return Task.FromResult<IReadOnlyList<float[]>?>([.. texts.Select(VectorFor)]);
        }

        public static float[] VectorFor(string text)
        {
            var vector = new float[Dimensions];
            for (var index = 0; index < text.Length; index++) vector[(text[index] * 31 + index) % Dimensions] += 1;
            vector[0] += 0.5f;
            return vector;
        }
    }
}
