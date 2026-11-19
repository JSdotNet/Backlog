namespace Backlog.Infrastructure.Brain.UnitTests;

/// <summary>
/// Roadmap's view of the Brain's projects and archive areas (local ADR 0052 §5): which
/// note is a project note it can offer to archive, and the move, which keeps the
/// file's bytes.
/// </summary>
public sealed class BrainRoadmapProjectNotesTests : IDisposable
{
    private readonly string _brain = Path.Combine(Path.GetTempPath(), "brain-project-notes-tests-" + Guid.NewGuid().ToString("N"));

    public BrainRoadmapProjectNotesTests() => Directory.CreateDirectory(_brain);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_brain, recursive: true);
        }
        catch (IOException)
        {
            // Left for the OS to clear.
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private BrainRoadmapProjectNotes Notes()
    {
        var source = new BrainFolderSource(() => _brain);
        return new BrainRoadmapProjectNotes(source, new BrainNoteLinkWriter(source));
    }

    private string Note(string relativePath, string text)
    {
        var path = Path.Combine([_brain, .. relativePath.Split('/')]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    [Theory]
    [InlineData("projects/house.md", true)]
    [InlineData("Projects/client/house.md", true)]
    [InlineData("areas/house.md", false)]
    [InlineData("archive/house.md", false)]
    [InlineData("projects.md", false)]
    [InlineData("", false)]
    public void Only_a_note_in_the_projects_area_can_be_archived(string notePath, bool expected)
    {
        Assert.Equal(expected, Notes().CanArchive(notePath));
    }

    [Fact]
    public void A_Brain_with_no_archive_area_is_never_offered_the_move()
    {
        File.WriteAllText(Path.Combine(_brain, "CONVENTIONS.md"), "## Areas\n\n| Area | Folder |\n|---|---|\n| Projects | projects |\n| Areas | areas |\n");

        Assert.False(Notes().CanArchive("projects/house.md"));
    }

    [Fact]
    public void The_projects_area_is_the_one_the_conventions_name()
    {
        File.WriteAllText(Path.Combine(_brain, "CONVENTIONS.md"), "## Areas\n\n| Area | Folder |\n|---|---|\n| Projects | work |\n| Archive | done |\n");

        var notes = Notes();

        Assert.True(notes.CanArchive("work/house.md"));
        Assert.False(notes.CanArchive("projects/house.md"));
    }

    [Fact]
    public async Task The_move_answers_the_new_path_and_keeps_the_bytes()
    {
        var source = Note("projects/house.md", "# House\r\n\r\n```meta\r\nroadmap: [house]\r\n```\r\n\r\nBody.\n");
        var before = await File.ReadAllBytesAsync(source, Ct);

        var move = await Notes().ArchiveAsync("projects/house.md", Ct);

        Assert.True(move.Moved);
        Assert.Equal("archive/house.md", move.NewPath);
        Assert.False(File.Exists(source));
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(_brain, "archive", "house.md"), Ct));
    }

    [Fact]
    public async Task A_refused_move_answers_why_and_moves_nothing()
    {
        Note("projects/house.md", "# House\n");
        Note("archive/house.md", "# Older house\n");

        var move = await Notes().ArchiveAsync("projects/house.md", Ct);

        Assert.False(move.Moved);
        Assert.Contains("taken", move.Error, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_brain, "projects", "house.md")));
    }
}
