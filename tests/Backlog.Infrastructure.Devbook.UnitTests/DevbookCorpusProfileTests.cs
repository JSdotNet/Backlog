using System.Globalization;

using Backlog.Infrastructure.Devbook.Building;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// The corpus profile Devbook's builder reads a folder under (local ADR 0040 §2):
/// the repository's devbook layout by default, or a profile another context
/// supplies — which names its own folders and adds a tagged line to the inputs
/// fingerprint — and the registry that says which root is read under which.
/// Runs in the SQLite pool-clearing collection because the registry is
/// process-wide and the Brain's tests register their roots in it too.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public class DevbookCorpusProfileTests : IDisposable
{
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "backlog-corpus-profile", Guid.NewGuid().ToString("N"));

    public DevbookCorpusProfileTests() => Directory.CreateDirectory(_scratch);

    [Fact]
    public void A_repository_fingerprint_has_no_tag_line()
    {
        Write(".devbook/arc42/05.md", "# Building blocks\n");

        var lines = InputsText(_scratch, DevbookCorpusProfile.Repository);

        Assert.Equal(DevbookDatabaseSchema.Version.ToString(CultureInfo.InvariantCulture), lines[0]);
        Assert.StartsWith(".devbook/arc42/05.md\t", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_tagged_profile_writes_its_tag_and_folders_as_the_second_line()
    {
        Write("a/one.md", "# One\n");
        Write("b/two.md", "# Two\n");

        var lines = InputsText(_scratch, new TaggedProfile());

        Assert.Equal("x\ta,b", lines[1]);
        Assert.StartsWith("a/one.md\t", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void A_profiles_extra_inputs_follow_the_sorted_walk()
    {
        Write("a/one.md", "# One\n");
        Write("b/two.md", "# Two\n");
        Write("RULES.md", "# Rules\n");

        var lines = InputsText(_scratch, new TaggedProfile());

        Assert.StartsWith("RULES.md\t", lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void The_brains_fingerprint_keeps_the_lines_it_wrote_before_the_hook()
    {
        Write("CONVENTIONS.md", "# Conventions\n\n## Areas\n\n| Area | Folder |\n|---|---|\n| Work | work |\n| Design | design |\n");
        Write("work/standup.md", "# Standup\n");
        Write("design/palette.md", "# Palette\n");

        var lines = InputsText(_scratch, BrainCorpusProfile.Instance);

        Assert.Equal("brain\twork,design", lines[1]);
        Assert.StartsWith("design/palette.md\t", lines[2], StringComparison.Ordinal);
        Assert.StartsWith("work/standup.md\t", lines[3], StringComparison.Ordinal);
        Assert.StartsWith("CONVENTIONS.md\t", lines[4], StringComparison.Ordinal);
        Assert.Equal(5, lines.Length);
    }

    [Fact]
    public void Registering_a_profiles_root_again_replaces_the_first()
    {
        var profile = new TaggedProfile();
        var first = Path.Combine(_scratch, "first");
        var second = Path.Combine(_scratch, "second");

        DevbookDatabaseLocation.RegisterCorpus(profile, first);
        DevbookDatabaseLocation.RegisterCorpus(profile, second);

        Assert.Same(DevbookCorpusProfile.Repository, DevbookDatabaseLocation.ProfileFor(first));
        Assert.Same(profile, DevbookDatabaseLocation.ProfileFor(second + Path.DirectorySeparatorChar));
        Assert.True(DevbookDatabaseLocation.IsRegisteredRoot(second));

        DevbookDatabaseLocation.RegisterCorpus(profile, null);

        Assert.Same(DevbookCorpusProfile.Repository, DevbookDatabaseLocation.ProfileFor(second));
        Assert.False(DevbookDatabaseLocation.IsRegisteredRoot(second));
    }

    [Fact]
    public void An_unregistered_root_reads_as_a_repository()
    {
        Assert.Same(DevbookCorpusProfile.Repository, DevbookDatabaseLocation.ProfileFor(Path.Combine(_scratch, "nowhere")));
        Assert.Same(DevbookCorpusProfile.Repository, DevbookDatabaseLocation.ProfileFor(null));
        Assert.False(DevbookDatabaseLocation.IsRegisteredRoot(null));
    }

    public void Dispose()
    {
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

    private static string[] InputsText(string root, DevbookCorpusProfile profile) =>
        DevbookDatabaseBuilder.InputsText(root, DevbookBuildLayout.Discover(root, profile)).TrimEnd('\n').Split('\n');

    private void Write(string relativePath, string text)
    {
        var path = Path.Combine(_scratch, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>A profile with its own two folders, a tag, and one extra input.</summary>
    private sealed class TaggedProfile : DevbookCorpusProfile
    {
        public override string? FingerprintTag => "x";

        public override bool HasFolderConventions => false;

        public override IReadOnlyList<string>? Folders(string root) => ["a", "b"];

        public override IEnumerable<string> ExtraInputs(string root) => ["RULES.md"];
    }
}
