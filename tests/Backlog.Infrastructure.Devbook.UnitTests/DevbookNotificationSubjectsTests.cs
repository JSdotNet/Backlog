using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// A remark's chapter link read back: the repository it names and the chapter, so whoever
/// holds only the notification can tell which repository a remark is about.
/// </summary>
public sealed class DevbookNotificationSubjectsTests
{
    [Fact]
    public void A_chapter_link_reads_back_as_the_repository_and_chapter_it_was_built_from()
    {
        var link = DevbookNotificationSubjects.ChapterLink("backlog", ".devbook/arc42/05 building blocks.md");

        Assert.Equal(("backlog", ".devbook/arc42/05 building blocks.md"), DevbookNotificationSubjects.ParseChapterLink(link));
    }

    [Fact]
    public void A_link_built_with_no_repository_reads_back_as_none()
    {
        var link = DevbookNotificationSubjects.ChapterLink(null, "notes/today.md");

        Assert.Equal(((string?)null, "notes/today.md"), DevbookNotificationSubjects.ParseChapterLink(link));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/devbook/backlog/a.md")]
    [InlineData("backlog://devbook/backlog")]
    [InlineData("backlog://tasks/backlog/a.md")]
    public void Anything_that_is_not_a_chapter_link_reads_as_nothing(string? link)
    {
        Assert.Null(DevbookNotificationSubjects.ParseChapterLink(link));
    }
}
