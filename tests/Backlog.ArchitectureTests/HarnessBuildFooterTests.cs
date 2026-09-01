namespace Backlog.ArchitectureTests;

/// <summary>
/// The two harnesses that host no application header still have to say which
/// build — and which checkout — you are looking at.
///
/// <para>The desktop shell answers this in its header, where the version chip
/// shows the worktree instead of the version while running from a checkout. The
/// storybook and the mobile harness have no such header: several worktrees serve
/// identical pages at once, so a footer carries the same two facts. These are
/// source assertions because the harnesses are hosts, not components, and
/// nothing renders them in a unit test.</para>
/// </summary>
public class HarnessBuildFooterTests
{
    public static TheoryData<string> Shells => new(
        Path.Combine(Repository.Root.FullName, "src", "Harness", "Backlog.UI.Storybook", "Components", "Layout", "MainLayout.razor"),
        Path.Combine(Repository.Root.FullName, "src", "Harness", "Backlog.Mobile.WebHarness", "Components", "App.razor"));

    [Theory]
    [MemberData(nameof(Shells))]
    public void The_shell_shows_the_version_and_the_worktree(string shell)
    {
        Assert.True(File.Exists(shell), $"Harness shell not found: {shell}");

        var markup = File.ReadAllText(shell);

        Assert.Contains("data-testid=\"build-footer\"", markup, StringComparison.Ordinal);
        Assert.Contains("AppVersion.OfEntryAssembly()", markup, StringComparison.Ordinal);
        Assert.Contains("DevelopmentWorkspace.Current is { } workspace", markup, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"build-footer-workspace\"", markup, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Shells))]
    public void The_worktree_is_shown_beside_the_version_rather_than_instead_of_it(string shell)
    {
        var markup = File.ReadAllText(shell);

        // The header chip swaps one for the other because it has room for one
        // line; a footer has room for both, and the version is the thing these
        // harnesses had no way to show at all before.
        var version = markup.IndexOf("AppVersion.OfEntryAssembly()", StringComparison.Ordinal);
        var workspace = markup.IndexOf("DevelopmentWorkspace.Current is { } workspace", StringComparison.Ordinal);

        Assert.True(version >= 0);
        Assert.True(workspace > version);
    }
}
