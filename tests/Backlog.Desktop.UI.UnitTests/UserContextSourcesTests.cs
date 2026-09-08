namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Every location here is somewhere on a developer's own machine, so each test
/// hands the reader a home directory of its own. Against the real one these
/// would assert whatever the machine running the suite happens to have, which is
/// to say nothing.
/// </summary>
public sealed class UserContextSourcesTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "backlog-user-context-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public void Reads_the_user_level_instruction_file()
    {
        Write(".claude/CLAUDE.md", "# Mine\n\nAcross every project.\n");

        var source = Source("User instructions");

        Assert.Equal(UserContextState.Present, source.State);
        Assert.Equal(1, source.Files);
        Assert.Equal(30, source.Bytes);
        Assert.Equal(8, source.EstimatedTokens);
        Assert.Equal("Claude Code", source.Host);
    }

    [Fact]
    public void Sums_a_folder_of_user_rules()
    {
        Write(".claude/rules/style.md", "# Style");
        Write(".claude/rules/tests.md", "# Tests");
        Write(".claude/rules/notes.txt", "not a rule");

        var source = Source("User rules");

        Assert.Equal(UserContextState.Present, source.State);
        Assert.Equal(2, source.Files);
        Assert.Equal(14, source.Bytes);
    }

    /// <summary>
    /// Three different answers, because they are three different things to know:
    /// a location nobody made, one somebody emptied, and one that is there.
    /// </summary>
    [Fact]
    public void Tells_absent_and_empty_apart()
    {
        Directory.CreateDirectory(Path.Combine(_home, ".claude", "rules"));

        Assert.Equal(UserContextState.Empty, Source("User rules").State);
        Assert.Equal(UserContextState.Absent, Source("User instructions").State);
    }

    [Fact]
    public void An_empty_file_is_empty_rather_than_loaded()
    {
        Write(".claude/CLAUDE.md", string.Empty);

        var source = Source("User instructions");

        Assert.Equal(UserContextState.Empty, source.State);
        Assert.Equal(0, source.Bytes);
    }

    /// <summary>A location that is not there is still a row. "Your user-level
    /// file is empty" is the finding most readers come for, and omitting it would
    /// read as though the question had never been asked.</summary>
    [Fact]
    public void Every_known_location_is_listed_even_when_nothing_is_there()
    {
        var sources = UserContextSources.Read(_home, _home);

        Assert.Equal(4, sources.Count);
        Assert.All(sources, source => Assert.Equal(UserContextState.Absent, source.State));
        Assert.Contains(sources, source => source.Host == "GitHub Copilot");
        Assert.Contains(sources, source => source.Host == "Claude Code");
    }

    /// <summary>
    /// The index alone, not the folder around it. <c>MEMORY.md</c> is read every
    /// session; the notes beside it are recalled when something makes them
    /// relevant. Measuring the folder counted sixty files and 124 KB against an
    /// index of 11 KB on the machine this was written on, and overstated what
    /// Claude Code carries by more than tenfold.
    /// </summary>
    [Fact]
    public void Measures_the_memory_index_and_not_the_notes_beside_it()
    {
        var project = Path.Combine(_home, "repos", "Backlog");
        Directory.CreateDirectory(project);
        WriteMemory(project, "MEMORY.md", "- a note");
        WriteMemory(project, "a-recalled-note.md", "read only when something makes it relevant");

        var source = Source("Project memory", project);

        Assert.Equal(UserContextState.Present, source.State);
        Assert.Equal(1, source.Files);
        Assert.Equal(8, source.Bytes);
        Assert.EndsWith("MEMORY.md", source.Path, StringComparison.Ordinal);
    }

    /// <summary>
    /// The case that made the ancestor walk necessary: a worktree sits several
    /// folders below the repository it belongs to, while the memory stays filed
    /// under the repository. Checking the clone alone reported "not found" from
    /// inside every worktree.
    /// </summary>
    [Fact]
    public void Finds_the_projects_memory_from_inside_one_of_its_worktrees()
    {
        var project = Path.Combine(_home, "repos", "Backlog");
        var worktree = Path.Combine(project, ".claude", "worktrees", "a-branch");
        Directory.CreateDirectory(worktree);
        WriteMemory(project, "MEMORY.md", "- a note");

        Assert.Equal(UserContextState.Present, Source("Project memory", worktree).State);
    }

    [Fact]
    public void Says_so_when_no_memory_is_filed_for_this_project()
    {
        var project = Path.Combine(_home, "repos", "Unknown");
        Directory.CreateDirectory(project);

        Assert.Equal(UserContextState.Absent, Source("Project memory", project).State);
    }

    [Fact]
    public void Totals_only_what_a_named_host_actually_loads()
    {
        Write(".claude/CLAUDE.md", "# Mine");
        Write(".claude/rules/style.md", "# Style");

        var sources = UserContextSources.Read(_home, _home);

        var claude = UserContextSources.Total(sources, "Claude Code");
        Assert.Equal(2, claude.Files);
        Assert.Equal(13, claude.Bytes);

        // Nothing of Copilot's is on this machine, so it carries nothing.
        Assert.Equal(0, UserContextSources.Total(sources, "GitHub Copilot").Files);
    }

    [Fact]
    public void Reads_nothing_without_a_home_to_read_from() =>
        Assert.Empty(UserContextSources.Read(_home, "   "));

    private UserContextSource Source(string label, string? repositoryRoot = null) =>
        Assert.Single(UserContextSources.Read(repositoryRoot ?? _home, _home), source => source.Label == label);

    private void WriteMemory(string project, string name, string content)
    {
        var slug = string.Concat(project.Select(character => char.IsLetterOrDigit(character) || character == '-' ? character : '-'));
        Write(Path.Combine(".claude", "projects", slug, "memory", name), content);
    }

    private void Write(string relativePath, string content)
    {
        var full = Path.Combine(_home, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
