namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The containment rule behind <see cref="AgentSession.ResolvedRepository"/>,
/// pinned without a settings store: which registered clone a working folder is
/// under, and — the half that matters — which it is not.
/// </summary>
public sealed class RegisteredClonesTests
{
    private static readonly RegisteredClone Backlog = new("JSdotNet/Backlog", @"D:\Repos\Backlog");

    private static readonly RegisteredClone Archify = new("JSdotNet/Archify", @"D:\Repos\Archify");

    [Fact]
    public void The_clone_root_itself_is_inside_the_clone() =>
        // A session opened at the root is the common case, not an edge.
        Assert.Equal("JSdotNet/Backlog", RegisteredClones.RepositoryContaining(@"D:\Repos\Backlog", [Backlog, Archify]));

    [Fact]
    public void A_worktree_under_the_clone_is_inside_the_clone() =>
        // The whole point: Claude Code's worktrees sit under .claude/worktrees of
        // the clone they were cut from.
        Assert.Equal(
            "JSdotNet/Backlog",
            RegisteredClones.RepositoryContaining(@"D:\Repos\Backlog\.claude\worktrees\keen-bose-667825", [Backlog, Archify]));

    [Fact]
    public void A_sibling_folder_sharing_the_clone_as_a_prefix_is_not_inside_it() =>
        // D:\Repos\Backlog2 starts with D:\Repos\Backlog and is a different folder.
        // Containment is by segment, not by prefix.
        Assert.Null(RegisteredClones.RepositoryContaining(@"D:\Repos\Backlog2\src", [Backlog]));

    [Fact]
    public void A_folder_outside_every_clone_resolves_to_nothing() =>
        Assert.Null(RegisteredClones.RepositoryContaining(@"C:\Users\someone\scratch", [Backlog, Archify]));

    [Fact]
    public void The_deepest_containing_clone_wins()
    {
        // A clone registered inside another clone is the more specific claim about
        // a folder under it; the outer one must not swallow it.
        var nested = new RegisteredClone("JSdotNet/Backlog.Docs", @"D:\Repos\Backlog\docs-site");

        Assert.Equal(
            "JSdotNet/Backlog.Docs",
            RegisteredClones.RepositoryContaining(@"D:\Repos\Backlog\docs-site\content", [Backlog, nested]));

        // Order of registration is not the tie-breaker; depth is.
        Assert.Equal(
            "JSdotNet/Backlog.Docs",
            RegisteredClones.RepositoryContaining(@"D:\Repos\Backlog\docs-site\content", [nested, Backlog]));

        // And a folder beside the nested clone still belongs to the outer one.
        Assert.Equal(
            "JSdotNet/Backlog",
            RegisteredClones.RepositoryContaining(@"D:\Repos\Backlog\src", [Backlog, nested]));
    }

    [Theory]
    [InlineData(@"D:\Repos\Backlog\", @"D:\Repos\Backlog\src")]
    [InlineData(@"D:/Repos/Backlog", @"D:\Repos\Backlog\src")]
    [InlineData(@"D:\Repos\Backlog", @"D:/Repos/Backlog/src/")]
    public void Separators_and_trailing_slashes_do_not_decide_containment(string clone, string folder) =>
        // The agents spell the folder the way their runtime does and the
        // Repositories screen stores it the way a person typed it.
        Assert.Equal("JSdotNet/Backlog", RegisteredClones.RepositoryContaining(folder, [new RegisteredClone("JSdotNet/Backlog", clone)]));

    [Fact]
    public void Case_follows_the_file_system()
    {
        var resolved = RegisteredClones.RepositoryContaining(@"d:\repos\backlog\src", [Backlog]);

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("JSdotNet/Backlog", resolved);
        }
        else
        {
            Assert.Null(resolved);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_folder_is_placed_nowhere(string? folder) =>
        // A replicated session has no folder; it must not land in whichever clone
        // happens to normalise to the empty string.
        Assert.Null(RegisteredClones.RepositoryContaining(folder, [Backlog]));

    [Fact]
    public void A_registration_without_a_clone_directory_is_never_a_match() =>
        // The clone directory is machine-local, and a workspace shared with someone
        // who has cloned nothing must not place their sessions anywhere.
        Assert.Null(RegisteredClones.RepositoryContaining(
            @"D:\Repos\Backlog",
            [new RegisteredClone("JSdotNet/Backlog", ""), new RegisteredClone("JSdotNet/Other", "   ")]));
}
