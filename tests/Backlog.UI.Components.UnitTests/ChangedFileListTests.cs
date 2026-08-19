namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The changed-file list. Most of what is asserted here is that it is the same
/// widget as ChangeScopePicker — same roles, same roving tabindex, same tick
/// slot — because two lists side by side that pick one thing each must not be
/// two different widgets.
/// </summary>
public sealed class ChangedFileListTests
{
    private static readonly IReadOnlyList<ChangedFile> Files =
    [
        new("src/Core/Backlog.UI.Components/Markdown/MarkdownView.razor",
            "MarkdownView.razor",
            "src/Core/Backlog.UI.Components/Markdown",
            ChangeKind.Changed, 3, 1, 2),
        new("src/Core/Backlog.UI.Components/Compare/ChangeModel.cs",
            "ChangeModel.cs",
            "src/Core/Backlog.UI.Components/Compare",
            ChangeKind.Added, 6, 0, 0),
        new("docs/old-notes.md", "old-notes.md", "docs", ChangeKind.Removed, 0, 4, 0)
    ];

    [Fact]
    public void Each_row_says_in_words_what_its_glyph_says_in_shape()
    {
        using var context = new BunitContext();

        var list = Render(context, null);

        // Three channels for one fact: the glyph is shape, the word is text,
        // and the row tint is colour. The glyph is hidden from assistive tech
        // so the word is not read twice, and neither is the only carrier.
        Assert.Equal(["±", "+", "−"], list.FindAll(".changed-file__kind").Select(kind => kind.TextContent));
        Assert.All(
            list.FindAll(".changed-file__kind"),
            kind => Assert.Equal("true", kind.GetAttribute("aria-hidden")));

        var spoken = list.FindAll(".changed-file .sr-only").Select(part => part.TextContent).ToList();

        Assert.Contains("Modified", spoken);
        Assert.Contains("Added", spoken);
        Assert.Contains("Removed", spoken);
    }

    [Fact]
    public void The_counts_run_is_hidden_and_a_sentence_stands_in_for_it()
    {
        using var context = new BunitContext();

        var row = Render(context, null).Find("[data-testid='files-modified-MarkdownView.razor']");

        // "plus three minus one plus-or-minus two" is not a sentence.
        Assert.Equal("+3 −1 ±2", row.QuerySelector(".changed-file__counts")!.TextContent);
        Assert.Equal("true", row.QuerySelector(".changed-file__counts")!.GetAttribute("aria-hidden"));
        Assert.Contains(
            "3 sections added, 1 removed, 2 changed",
            row.QuerySelectorAll(".sr-only").Select(part => part.TextContent));
    }

    [Fact]
    public void The_directory_is_in_the_dom_whole_and_on_a_line_of_its_own()
    {
        using var context = new BunitContext();

        var row = Render(context, null).Find("[data-testid='files-modified-MarkdownView.razor']");

        // Not truncated, unlike FileView's path: in a narrow list the tail of
        // the directory is the segment that tells .../Markdown from .../Layout.
        Assert.Equal("MarkdownView.razor", row.QuerySelector(".changed-file__name")!.TextContent);
        Assert.Equal(
            "src/Core/Backlog.UI.Components/Markdown",
            row.QuerySelector(".changed-file__directory")!.TextContent);
    }

    [Fact]
    public void Exactly_one_row_reports_itself_selected_and_exactly_one_is_tabbable()
    {
        using var context = new BunitContext();

        var list = Render(context, "docs/old-notes.md");

        Assert.Single(list.FindAll("[role=option][aria-selected=true]"));
        Assert.Single(list.FindAll("[role=option][tabindex='0']"));
        Assert.Equal(3, list.FindAll(".changed-file__mark").Count);
    }

    [Fact]
    public void Arrow_keys_move_the_selection_the_same_way_the_picker_does()
    {
        using var context = new BunitContext();

        string? selected = null;
        var list = Render(context, Files[0].Path, value => selected = value);

        list.Find("[data-testid='files-modified-MarkdownView.razor']")
            .KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        Assert.Equal(Files[1].Path, selected);
    }

    [Fact]
    public void An_empty_scope_is_an_answer_and_not_an_empty_listbox()
    {
        using var context = new BunitContext();

        var list = context.Render<ChangedFileList>(parameters => parameters
            .Add(p => p.Files, [])
            .Add(p => p.EmptyTitle, "Nothing uncommitted")
            .Add(p => p.TestId, "files"));

        Assert.Empty(list.FindAll("[role=listbox]"));
        Assert.Equal("Nothing uncommitted", list.Find(".empty-state__title").TextContent);

        // No call to action: nothing to look at is a complete, often welcome
        // answer, not a task somebody has failed to start.
        Assert.Empty(list.FindAll(".empty-state__action"));
    }

    private static IRenderedComponent<ChangedFileList> Render(
        BunitContext context,
        string? selectedPath,
        Action<string?>? onSelected = null) =>
        context.Render<ChangedFileList>(parameters => parameters
            .Add(p => p.Files, Files)
            .Add(p => p.SelectedPath, selectedPath)
            .Add(p => p.SelectedPathChanged, value => onSelected?.Invoke(value))
            .Add(p => p.TestId, "files"));
}
