using Backlog.UI.Components.Devbook;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The folder is the first segment of a reference path, and every folder the
/// devbook convention names has to come back as itself: a folder this map does
/// not know renders with no vocabulary at all, so a status in it can never be
/// told from a typo.
/// </summary>
public sealed class DevbookFoldersTests
{
    [Theory]
    [InlineData(".arc42/05-building-blocks.md", DevbookFolder.Arc42)]
    [InlineData(".domain/tasks/domain.md#task", DevbookFolder.Domain)]
    [InlineData(".backlog/epics/sync.md", DevbookFolder.Backlog)]
    [InlineData(".tech/tooling.md#claude-code", DevbookFolder.Tech)]
    [InlineData(".design/color-scheme.md", DevbookFolder.Design)]
    [InlineData(".ai/02-specify.md#devbook-skills", DevbookFolder.Ai)]
    [InlineData(".ai/adoption-map.md", DevbookFolder.Ai)]
    public void Every_devbook_folder_is_read_off_the_first_segment(string path, DevbookFolder expected)
    {
        Assert.Equal(expected, DevbookFolders.FromPath(path));
    }

    [Theory]
    [InlineData("./.ai/concepts.md")]
    [InlineData("/.ai/concepts.md")]
    [InlineData(".AI/concepts.md")]
    [InlineData(@".ai\concepts.md")]
    [InlineData("  .ai/concepts.md  ")]
    public void A_hand_authored_prefix_or_casing_is_the_same_folder(string path)
    {
        Assert.Equal(DevbookFolder.Ai, DevbookFolders.FromPath(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".github/copilot-instructions.md")]
    [InlineData("README.md")]
    [InlineData("ai/concepts.md")]
    public void Anything_outside_the_devbook_folders_is_unknown(string? path)
    {
        Assert.Equal(DevbookFolder.Unknown, DevbookFolders.FromPath(path));
    }
}
