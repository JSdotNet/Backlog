using Backlog.Infrastructure.FileSystem;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// Inspecting a Brain folder and scaffolding its convention (local ADR 0026,
/// <c>.devbook/domain/devbook/requirements.md#brain-folder-setup</c>): the sync
/// product said as information, the two convention files looked for, and
/// <b>Create conventions</b> writing what is missing and never overwriting.
/// </summary>
public sealed class BrainFolderSetupTests : IDisposable
{
    private static readonly string[] ParaFolders = ["_inbox", "projects", "areas", "resources", "people", "journal", "archive"];

    private readonly string _brain = Path.Combine(
        Path.GetTempPath(),
        "brain-folder-setup-tests-" + Guid.NewGuid().ToString("N"),
        "Brain");

    public BrainFolderSetupTests() => Directory.CreateDirectory(_brain);

    private static BrainFolderSetup Setup(Func<string, SyncedFolderMatch?>? detect = null) => new(detect ?? (_ => null));

    [Fact]
    public void An_empty_folder_has_neither_convention_file()
    {
        var status = Setup().Inspect(_brain);

        Assert.True(status.Exists);
        Assert.False(status.HasReadme);
        Assert.False(status.HasConventions);
        Assert.True(status.NeedsConventions);
        Assert.Null(status.SyncedBy);
    }

    [Fact]
    public void A_folder_under_onedrive_is_reported_as_synced()
    {
        var status = Setup(_ => new SyncedFolderMatch("OneDrive", @"C:\Users\sam\OneDrive")).Inspect(_brain);

        Assert.Equal("OneDrive", status.SyncedBy?.ProviderName);
    }

    [Fact]
    public void A_sync_probe_that_throws_says_nothing()
    {
        var status = Setup(_ => throw new InvalidOperationException("no answer")).Inspect(_brain);

        Assert.Null(status.SyncedBy);
    }

    /// <summary>The requirement's first scenario, and ADR 0026's Verification 4.</summary>
    [Fact]
    public void Create_conventions_scaffolds_an_empty_folder()
    {
        var result = Setup().CreateConventions(_brain);

        Assert.Null(result.Error);
        Assert.Empty(result.Kept);

        var readme = File.ReadAllText(Path.Combine(_brain, "README.md"));
        Assert.Contains("index: root", readme, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_brain, "CONVENTIONS.md")));

        foreach (var folder in ParaFolders)
        {
            Assert.True(Directory.Exists(Path.Combine(_brain, folder)), folder);
        }

        Assert.Equal(["README.md", "CONVENTIONS.md", .. ParaFolders.Select(folder => folder + "/")], result.Written);

        var status = Setup().Inspect(_brain);
        Assert.False(status.NeedsConventions);
    }

    /// <summary>The requirement's second scenario: the person's own README is
    /// left exactly as it was.</summary>
    [Fact]
    public void Create_conventions_never_overwrites_a_readme_already_there()
    {
        const string mine = "# My brain\n\nWritten by me.\n";
        File.WriteAllText(Path.Combine(_brain, "README.md"), mine);

        var result = Setup().CreateConventions(_brain);

        Assert.Null(result.Error);
        Assert.Equal(mine, File.ReadAllText(Path.Combine(_brain, "README.md")));
        Assert.Contains("README.md", result.Kept);
        Assert.Contains("CONVENTIONS.md", result.Written);
        Assert.All(ParaFolders, folder => Assert.True(Directory.Exists(Path.Combine(_brain, folder))));
    }

    /// <summary>The area folders follow the folder's own CONVENTIONS.md, not the
    /// shipped one, so a person's convention decides them.</summary>
    [Fact]
    public void Create_conventions_makes_the_folders_an_existing_conventions_declares()
    {
        const string conventions = """
            # Conventions

            ## Areas

            | Area | Folder | Holds |
            |---|---|---|
            | Work | `work` | Work notes. |
            | Home | `home` | Home notes. |
            """;
        File.WriteAllText(Path.Combine(_brain, "CONVENTIONS.md"), conventions);

        var result = Setup().CreateConventions(_brain);

        Assert.Null(result.Error);
        Assert.Equal(conventions, File.ReadAllText(Path.Combine(_brain, "CONVENTIONS.md")));
        Assert.True(Directory.Exists(Path.Combine(_brain, "work")));
        Assert.True(Directory.Exists(Path.Combine(_brain, "home")));
        Assert.False(Directory.Exists(Path.Combine(_brain, "projects")));
        Assert.Equal(["README.md", "work/", "home/"], result.Written);
    }

    [Fact]
    public void Running_it_twice_writes_nothing_the_second_time()
    {
        _ = Setup().CreateConventions(_brain);
        var readme = Path.Combine(_brain, "README.md");
        File.AppendAllText(readme, "\nMy own line.\n");

        var second = Setup().CreateConventions(_brain);

        Assert.Empty(second.Written);
        Assert.Contains("My own line.", File.ReadAllText(readme), StringComparison.Ordinal);
    }

    /// <summary>ADR 0026's Verification 6, for this half: setting a Brain up
    /// writes Markdown and folders, and nothing derived.</summary>
    [Fact]
    public void Create_conventions_writes_no_database_into_the_folder()
    {
        _ = Setup().CreateConventions(_brain);

        var files = Directory.EnumerateFiles(_brain, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .ToList();

        Assert.Equal(["CONVENTIONS.md", "README.md"], files.Order(StringComparer.Ordinal));
    }

    public void Dispose()
    {
        try
        {
            var root = Path.GetDirectoryName(_brain)!;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
