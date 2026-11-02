using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Brain.UnitTests;

/// <summary>
/// The Brain's side of Devbook's corpus registry (local ADR 0040 §2): the folder
/// recorded as the Brain is built under <see cref="BrainCorpusProfile"/>, a moved
/// folder leaves no stale Brain behind, and a Brain database built before the
/// builder took a profile still counts as current — its fingerprint text is the
/// one the old builder wrote, byte for byte.
/// </summary>
[Collection(CorpusRegistryCollection.Name)]
public class BrainDatabaseLocationTests : IDisposable
{
    /// <summary>A fixed modification time, so the old fingerprint can be written
    /// out as the literal it was: 2026-01-02T03:04:05Z in Unix milliseconds.</summary>
    private static readonly DateTime Touched = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private const string TouchedMilliseconds = "1767323045000";

    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "backlog-brain-location", Guid.NewGuid().ToString("N"));

    public BrainDatabaseLocationTests()
    {
        Directory.CreateDirectory(_scratch);
        DevbookDatabaseLocation.Configure(() => Path.Combine(_scratch, "devbook-cache"));
    }

    private string Brain => Path.Combine(_scratch, "OneDrive", "Brain");

    [Fact]
    public void Asking_for_the_brains_database_records_the_folder_as_the_brain()
    {
        var database = BrainDatabaseLocation.ForBrainFolder(Brain);

        Assert.NotNull(database);
        Assert.Equal(DevbookDatabaseLocation.ForRepositoryRoot(Brain), database);
        Assert.Same(BrainCorpusProfile.Instance, DevbookDatabaseLocation.ProfileFor(Brain));
        Assert.True(BrainDatabaseLocation.IsBrainRoot(Brain + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void A_second_brain_folder_replaces_the_first()
    {
        var old = Path.Combine(_scratch, "Old Brain");

        BrainDatabaseLocation.ForBrainFolder(old);
        BrainDatabaseLocation.ForBrainFolder(Brain);

        Assert.Same(DevbookCorpusProfile.Repository, DevbookDatabaseLocation.ProfileFor(old));
        Assert.False(BrainDatabaseLocation.IsBrainRoot(old));
        Assert.True(BrainDatabaseLocation.IsBrainRoot(Brain));
    }

    [Fact]
    public void Registering_no_folder_clears_the_brain()
    {
        BrainDatabaseLocation.RegisterBrainRoot(Brain);
        BrainDatabaseLocation.RegisterBrainRoot(null);

        Assert.False(BrainDatabaseLocation.IsBrainRoot(Brain));
        Assert.Same(DevbookCorpusProfile.Repository, DevbookDatabaseLocation.ProfileFor(Brain));
    }

    [Fact]
    public void A_brain_database_built_before_the_corpus_profile_still_counts_as_current()
    {
        Write("CONVENTIONS.md", "# Conventions\n\n## Areas\n\n- `work`\n- `design`\n");
        Write("work/standup.md", "# Standup\n");
        Write("design/palette.md", "# Palette\n");
        var target = Path.Combine(_scratch, "brain.db");
        Assert.True(DevbookDatabaseBuilder.Build(Brain, target, BrainCorpusProfile.Instance, TestContext.Current.CancellationToken));

        // The text the builder hashed before the hook existed: the schema version,
        // the brain line with the folders it resolved, the sorted walk, then the
        // conventions file — each input by path, size and modification time.
        var before =
            $"{DevbookDatabaseSchema.Version.ToString(CultureInfo.InvariantCulture)}\n"
            + "brain\twork,design\n"
            + $"design/palette.md\t10\t{TouchedMilliseconds}\n"
            + $"work/standup.md\t10\t{TouchedMilliseconds}\n"
            + $"CONVENTIONS.md\t45\t{TouchedMilliseconds}\n";
        RecordInputs(target, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(before))));

        Assert.True(DevbookDatabaseBuilder.IsCurrent(Brain, target, BrainCorpusProfile.Instance));
    }

    public void Dispose()
    {
        BrainDatabaseLocation.RegisterBrainRoot(null);
        DevbookDatabaseLocation.Configure(() => null);

        try
        {
            Directory.Delete(_scratch, recursive: true);
        }
        catch (IOException)
        {
            // A scratch folder left behind costs disk, not correctness.
        }

        GC.SuppressFinalize(this);
    }

    private void Write(string relativePath, string text)
    {
        var path = Path.Combine(Brain, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, Touched);
    }

    /// <summary>Overwrites the fingerprint the database records, as a database
    /// written by the earlier builder would carry it.</summary>
    private static void RecordInputs(string databasePath, string fingerprint)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE meta SET value = $value WHERE key = $key";
        command.Parameters.AddWithValue("$value", fingerprint);
        command.Parameters.AddWithValue("$key", DevbookDatabaseBuilder.InputsKey);
        Assert.Equal(1, command.ExecuteNonQuery());
    }
}
