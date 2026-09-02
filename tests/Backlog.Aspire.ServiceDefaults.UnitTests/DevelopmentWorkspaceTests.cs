
namespace Backlog.Aspire.ServiceDefaults.UnitTests;

/// <summary>
/// The marker exists to tell two concurrently running worktrees apart, so what
/// is asserted here is the shape of a checkout rather than any one repository:
/// a clone, a worktree pointing at its parent's git folder, a detached head,
/// and a folder with no checkout above it at all.
/// </summary>
public sealed class DevelopmentWorkspaceTests : IDisposable
{
    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("backlog-workspace-");

    public void Dispose() => _temp.Delete(recursive: true);

    [Fact]
    public void A_clone_is_named_by_its_folder_and_its_branch()
    {
        var checkout = Clone("Backlog", "refs/heads/main");

        Assert.Equal("Backlog (main)", DevelopmentWorkspace.Describe(checkout));
    }

    [Fact]
    public void A_branch_that_repeats_the_folder_name_is_left_out()
    {
        // Worktrees are normally created from the branch they hold, so saying
        // both would only make the title longer than the tab showing it.
        var checkout = Clone("dev-mode-app-title", "refs/heads/claude/dev-mode-app-title");

        Assert.Equal("dev-mode-app-title", DevelopmentWorkspace.Describe(checkout));
    }

    [Fact]
    public void A_worktree_reads_the_head_its_git_file_points_at()
    {
        var common = Directory.CreateDirectory(Path.Combine(_temp.FullName, "shared", "worktrees", "feature"));
        File.WriteAllText(Path.Combine(common.FullName, "HEAD"), "ref: refs/heads/feature/titles\n");

        var checkout = Directory.CreateDirectory(Path.Combine(_temp.FullName, "feature")).FullName;
        File.WriteAllText(Path.Combine(checkout, ".git"), $"gitdir: {common.FullName}\n");

        Assert.Equal("feature (feature/titles)", DevelopmentWorkspace.Describe(checkout));
    }

    [Fact]
    public void A_detached_head_is_named_by_its_commit()
    {
        var checkout = Clone("Backlog", "2c367f0d9a1b4e5f6071829304a5b6c7d8e9f001");

        Assert.Equal("Backlog (detached 2c367f0)", DevelopmentWorkspace.Describe(checkout));
    }

    [Fact]
    public void The_checkout_is_found_from_a_build_output_folder()
    {
        // What the hosts actually pass is AppContext.BaseDirectory, which sits
        // several folders below the checkout root.
        var checkout = Clone("Backlog", "refs/heads/main");
        var output = Directory.CreateDirectory(Path.Combine(checkout, "src", "App", "bin", "Debug", "net10.0")).FullName;

        Assert.Equal("Backlog (main)", DevelopmentWorkspace.Describe(output));
    }

    [Fact]
    public void A_folder_with_no_checkout_above_it_has_no_marker()
    {
        var installed = Directory.CreateDirectory(Path.Combine(_temp.FullName, "Program Files", "Backlog")).FullName;

        Assert.Null(DevelopmentWorkspace.Describe(installed));
    }

    [Fact]
    public void An_unreadable_head_still_names_the_folder()
    {
        var checkout = Directory.CreateDirectory(Path.Combine(_temp.FullName, "Backlog")).FullName;
        Directory.CreateDirectory(Path.Combine(checkout, ".git"));

        Assert.Equal("Backlog", DevelopmentWorkspace.Describe(checkout));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_start_directory_means_no_marker(string? startDirectory)
    {
        Assert.Null(DevelopmentWorkspace.Describe(startDirectory));
    }

    [Fact]
    public void The_marker_leads_the_title_so_a_truncated_tab_still_shows_it()
    {
        Assert.Equal("[Backlog (main)] Backlog.Desktop", DevelopmentWorkspace.DecorateTitle("Backlog.Desktop", "Backlog (main)"));
    }

    [Fact]
    public void A_title_without_a_marker_is_untouched()
    {
        Assert.Equal("Backlog.Desktop", DevelopmentWorkspace.DecorateTitle("Backlog.Desktop", marker: null));
    }

    [Fact]
    public void The_script_carries_the_marker_as_an_escaped_literal()
    {
        var script = DevelopmentWorkspace.BuildTitleScript("bran\"ch <b>");

        // Razor would HTML-escape an interpolated marker inside a script block,
        // which is why the literal is written by the serializer instead.
        Assert.Contains("""\u0022""", script);
        Assert.Contains("""\u003C""", script);
        Assert.DoesNotContain("<b>", script);
        Assert.Contains("document.title", script);
    }

    [Fact]
    public void There_is_no_script_without_a_marker()
    {
        Assert.Equal(string.Empty, DevelopmentWorkspace.BuildTitleScript(marker: null));
    }

    private string Clone(string folderName, string head)
    {
        var checkout = Directory.CreateDirectory(Path.Combine(_temp.FullName, folderName)).FullName;
        var git = Directory.CreateDirectory(Path.Combine(checkout, ".git")).FullName;
        var content = head.StartsWith("refs/", StringComparison.Ordinal) ? $"ref: {head}\n" : $"{head}\n";
        File.WriteAllText(Path.Combine(git, "HEAD"), content);
        return checkout;
    }
}
