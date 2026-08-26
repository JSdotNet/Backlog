namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The note list as its own component. What matters here is the half of the pair
/// that lives in this file: the id a mark points at, and the link back up to it.
/// The other half — the <c>sup.md-fnref</c> the inline renderer emits — is in
/// <c>MarkdownFootnoteTests</c>, and the two ids are written in different files,
/// which is exactly why each end is pinned.
/// </summary>
public sealed class MarkdownFootnotesViewTests
{
    private static MdFootnotes Notes =>
        new(
        [
            new MdFootnote("partial", 1, MarkdownPreview.ParseInlines("It covers what people write here.")),
            new MdFootnote("single", 2, MarkdownPreview.ParseInlines("MarkdownView, switching over the records."))
        ]);

    [Fact]
    public void Each_note_is_addressable_and_carries_the_way_back()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownFootnotesView>(parameters => parameters
            .Add(notes => notes.Footnotes, Notes));

        var region = view.Find("aside.md-footnotes");

        // An aside with no name is an aside a reader cannot decide to skip.
        Assert.Equal("Footnotes", region.GetAttribute("aria-label"));

        // Ordered, because the numbers are the point and a reader who lost their
        // place should be able to count.
        Assert.NotNull(view.Find("aside.md-footnotes > ol.md-footnotes__list"));

        var items = view.FindAll("li.md-footnotes__item");

        Assert.Equal(["fn-partial", "fn-single"], items.Select(item => item.Id));

        Assert.Equal(
            ["#fnref-partial", "#fnref-single"],
            view.FindAll("a.md-footnotes__back").Select(link => link.GetAttribute("href")));

        // The number and not the label, because the number is what the reader saw
        // in the prose.
        Assert.Equal(
            ["Back to reference 1", "Back to reference 2"],
            view.FindAll("a.md-footnotes__back").Select(link => link.GetAttribute("aria-label")));
    }

    [Fact]
    public void The_rule_above_the_notes_is_the_same_rule_a_divider_draws()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownFootnotesView>(parameters => parameters
            .Add(notes => notes.Footnotes, Notes));

        // Deliberately the same class: the notes are the end of the document and
        // it is the same end.
        Assert.NotNull(view.Find("aside.md-footnotes > hr.md-divider"));
    }

    [Fact]
    public void Every_part_can_be_pointed_at_a_hosts_own_names()
    {
        using var context = new BunitContext();

        var view = context.Render<MarkdownFootnotesView>(parameters => parameters
            .Add(notes => notes.Footnotes, Notes)
            .Add(notes => notes.BaseCssClass, "pane-notes")
            .Add(notes => notes.ListCssClass, "pane-notes__list")
            .Add(notes => notes.ItemCssClass, "pane-notes__item")
            .Add(notes => notes.BackCssClass, "pane-notes__back")
            .Add(notes => notes.DividerCssClass, "pane-rule")
            .Add(notes => notes.AriaLabel, "Notes"));

        Assert.Empty(view.FindAll(".md-footnotes"));
        Assert.NotNull(view.Find("aside.pane-notes[aria-label='Notes'] > hr.pane-rule"));
        Assert.NotNull(view.Find("ol.pane-notes__list > li.pane-notes__item > a.pane-notes__back"));

        // The ids are not a class hook and must not move with one: they answer the
        // marks in the prose, which this component never sees.
        Assert.Equal("fn-partial", view.FindAll("li").First().Id);
    }
}
