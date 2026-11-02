using Backlog.SharedKernel.Devbook;

namespace Backlog.SharedKernel.UnitTests;

/// <summary>
/// The conventional spelling of a devbook path, in the kernel so that a context
/// beside Devbook can compare a path the way Devbook does without referencing it
/// (local ADR 0040): the devbook layout's <c>.devbook/&lt;folder&gt;/</c> becomes
/// the root layout's <c>.&lt;folder&gt;/</c>, and anything else only loses its
/// backslashes and its leading <c>./</c>.
/// </summary>
public class DevbookPathsTests
{
    [Theory]
    [InlineData(".devbook/arc42/05.md", ".arc42/05.md")]
    [InlineData("./.devbook/domain/x.md", ".domain/x.md")]
    [InlineData(".devbook/_meta/x", ".devbook/_meta/x")]
    [InlineData(".devbook\\tech\\shared.md", ".tech/shared.md")]
    [InlineData("work/standup.md", "work/standup.md")]
    public void A_devbook_layout_path_reads_in_the_root_layouts_spelling(string path, string expected) =>
        Assert.Equal(expected, DevbookPaths.ConventionalPath(path));

    [Theory]
    [InlineData("./.github\\copilot-instructions.md", ".github/copilot-instructions.md")]
    [InlineData("/.claude/rules/x.md", ".claude/rules/x.md")]
    [InlineData("  ", "")]
    [InlineData(null, "")]
    public void Normalizing_keeps_the_dot_of_a_dot_folder(string? path, string expected) =>
        Assert.Equal(expected, DevbookPaths.NormalizeKeepingDotFolder(path));

    [Fact]
    public void The_devbook_folders_are_the_five_in_the_generators_order() =>
        Assert.Equal(["arc42", "domain", "tech", "design", "ai"], DevbookPaths.FolderNames);
}
