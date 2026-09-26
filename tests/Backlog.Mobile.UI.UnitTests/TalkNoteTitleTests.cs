using Backlog.Mobile.UI.TalkNotes;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The title a talk note arrives under when nobody typed one — the first body
/// line, then "Photo · date", then the first file's name.
/// </summary>
public sealed class TalkNoteTitleTests
{
    private static readonly DateOnly Today = new(2026, 9, 26);

    private static readonly (string, string)[] NoFiles = [];

    [Fact]
    public void A_typed_title_is_the_title()
    {
        Assert.Equal("Keynote", TalkNoteTitle.For("  Keynote ", "# Something else", NoFiles, Today));
    }

    [Fact]
    public void Without_one_the_first_body_line_is_the_title_without_its_markdown()
    {
        var body = "\n\n## Offline-first sync is a UX decision\n- outbox before network";

        Assert.Equal("Offline-first sync is a UX decision", TalkNoteTitle.For("", body, NoFiles, Today));
    }

    [Fact]
    public void A_long_first_line_is_cut_with_an_ellipsis()
    {
        var title = TalkNoteTitle.For(null, new string('a', 500), NoFiles, Today);

        Assert.Equal(TalkNoteTitle.MaximumFromBody, title.Length);
        Assert.EndsWith("…", title, StringComparison.Ordinal);
    }

    [Fact]
    public void A_note_with_only_a_picture_is_a_photo_of_today()
    {
        var title = TalkNoteTitle.For(" ", " ", [("handout.pdf", "application/pdf"), ("IMG_0042.jpg", "image/jpeg")], Today);

        Assert.Equal("Photo · 26 Sep 2026", title);
    }

    [Fact]
    public void A_note_with_only_files_is_named_for_the_first()
    {
        var title = TalkNoteTitle.For(null, null, [("handout.pdf", "application/pdf"), ("slides.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation")], Today);

        Assert.Equal("handout.pdf", title);
    }

    [Fact]
    public void An_empty_note_has_no_title()
    {
        Assert.Equal(string.Empty, TalkNoteTitle.For("", "  \n ", NoFiles, Today));
    }
}
