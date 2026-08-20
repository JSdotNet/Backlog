namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// How a share becomes a draft.
///
/// <para>The interesting case is YouTube, which sends the video title as the
/// subject and the link as the text. Two separate values are no use to a
/// quick-capture field, so <see cref="SharedContent"/> decides what one line of
/// text a share is worth — and that decision is worth pinning down here rather
/// than only through the screen.</para>
/// </summary>
public sealed class SharedContentCompositionTests
{
    [Fact]
    public void A_subject_and_a_link_read_as_the_title_followed_by_the_link()
    {
        var shared = SharedContent.From("https://youtu.be/abc123", "How to fold a fitted sheet");

        Assert.Equal("How to fold a fitted sheet https://youtu.be/abc123", shared.Draft);
    }

    [Fact]
    public void A_share_with_no_subject_is_just_its_text()
    {
        var shared = SharedContent.From("https://example.test/article", subject: null);

        Assert.Equal("https://example.test/article", shared.Draft);
    }

    [Fact]
    public void A_share_with_no_text_is_just_its_subject()
    {
        var shared = SharedContent.From(text: null, subject: "Read the quarterly report");

        Assert.Equal("Read the quarterly report", shared.Draft);
    }

    [Fact]
    public void A_share_that_carries_nothing_is_empty_rather_than_a_blank_draft()
    {
        var shared = SharedContent.From("   ", "\t");

        Assert.Equal(string.Empty, shared.Draft);
        Assert.True(shared.IsEmpty);
    }

    [Fact]
    public void Surrounding_whitespace_from_the_sending_app_is_not_carried_into_the_draft()
    {
        var shared = SharedContent.From("  https://youtu.be/abc123\n", "  A title  ");

        Assert.Equal("A title https://youtu.be/abc123", shared.Draft);
    }

    [Fact]
    public void An_app_that_sends_the_same_words_as_subject_and_text_does_not_get_them_twice()
    {
        var shared = SharedContent.From("Buy milk", "Buy milk");

        Assert.Equal("Buy milk", shared.Draft);
    }
}
