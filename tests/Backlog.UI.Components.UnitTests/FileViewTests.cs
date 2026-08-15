using System.Globalization;

namespace Backlog.UI.Components.UnitTests;

public sealed class FileViewTests
{
    [Fact]
    public void The_header_carries_the_name_the_path_and_what_is_known_about_the_file()
    {
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "context-loading.instructions.md")
            .Add(v => v.Path, @".github\instructions\context-loading.instructions.md")
            .Add(v => v.Source, "GitHub Copilot")
            .Add(v => v.Kind, "Path-specific instructions")
            .Add(v => v.SizeInBytes, 5837));

        Assert.Equal("context-loading.instructions.md", view.Find(".file-view__name").TextContent);
        Assert.Equal(@".github\instructions\context-loading.instructions.md", view.Find(".file-view__path").TextContent);

        var expectedSize = FileView.FormatSize(5837);
        Assert.Equal($"GitHub Copilot · Path-specific instructions · {expectedSize}", view.Find(".file-view__meta").TextContent);
    }

    [Fact]
    public void What_the_caller_does_not_know_is_left_out_rather_than_filled_in()
    {
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters.Add(v => v.Name, "scratch.md"));

        Assert.Empty(view.FindAll(".file-view__meta"));
        Assert.Empty(view.FindAll(".file-view__path"));
    }

    [Fact]
    public void A_single_known_detail_is_shown_without_a_separator()
    {
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "notes.md")
            .Add(v => v.Kind, "Markdown"));

        Assert.Equal("Markdown", view.Find(".file-view__meta").TextContent);
    }

    [Fact]
    public void The_body_is_the_files_markdown_rendered()
    {
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "notes.md")
            .Add(v => v.Body, "# Title\n\nA paragraph.\n\n- [ ] A task\n"));

        Assert.Equal("Title", view.Find(".file-view__body .md-heading").TextContent);
        Assert.Equal("A paragraph.", view.Find(".file-view__body .md-p").TextContent);
    }

    [Fact]
    public void A_section_heading_does_not_swallow_the_rest_of_the_file()
    {
        // A file is read as a document, not as an entry: `##` is how people write
        // sections, and the entry reading folds everything below the first one
        // into a sub-item that renders as nothing at all.
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "policy.md")
            .Add(v => v.Body, "# Policy\n\n## The gate\n\nWhat the gate is.\n\n## Context\n\nWhat may be loaded.\n"));

        var headings = view.FindAll(".file-view__body .md-heading");

        Assert.Equal(["Policy", "The gate", "Context"], headings.Select(h => h.TextContent));
        Assert.Equal(["1", "2", "2"], headings.Select(h => h.GetAttribute("aria-level")));
        Assert.Equal(2, view.FindAll(".file-view__body .md-p").Count);
    }

    [Fact]
    public void The_body_is_a_named_scroll_region_a_keyboard_can_reach()
    {
        // Without tabindex a Chromium user cannot scroll this at all without a
        // mouse, and an unnamed region is a tab stop that announces nothing.
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters.Add(v => v.Name, "notes.md"));
        var body = view.Find(".file-view__body");

        Assert.Equal("region", body.GetAttribute("role"));
        Assert.Equal("0", body.GetAttribute("tabindex"));
        Assert.Equal("notes.md", body.GetAttribute("aria-label"));
    }

    [Fact]
    public void Nothing_writes_back_so_a_checklist_renders_as_state()
    {
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "checklist.md")
            .Add(v => v.Body, "- [x] Done\n- [ ] Open\n"));

        Assert.Empty(view.FindAll("button"));
        Assert.Equal(2, view.FindAll("[data-testid='entry-checkbox']").Count);
    }

    [Fact]
    public void MaxHeight_is_what_makes_the_body_scroll_and_it_can_be_turned_off()
    {
        using var context = new BunitContext();

        var capped = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "notes.md")
            .Add(v => v.MaxHeight, "12rem"));

        Assert.Equal("max-height: 12rem", capped.Find(".file-view__body").GetAttribute("style"));

        var uncapped = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "notes.md")
            .Add(v => v.MaxHeight, null));

        Assert.Null(uncapped.Find(".file-view__body").GetAttribute("style"));
    }

    [Fact]
    public void Fill_hands_the_height_to_the_host_and_drops_the_cap()
    {
        // The two are not additive: a max-height left on the body would fight
        // the flex box that is meant to be sizing it.
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "notes.md")
            .Add(v => v.Fill, true)
            .Add(v => v.MaxHeight, "12rem"));

        Assert.Contains("file-view--fill", view.Find(".file-view").ClassList);
        Assert.Null(view.Find(".file-view__body").GetAttribute("style"));
    }

    [Fact]
    public void Without_Fill_the_card_does_not_claim_the_hosts_height()
    {
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters.Add(v => v.Name, "notes.md"));

        Assert.DoesNotContain("file-view--fill", view.Find(".file-view").ClassList);
    }

    [Fact]
    public void A_test_id_reaches_the_body_too_so_the_scroll_region_can_be_addressed()
    {
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "notes.md")
            .Add(v => v.TestId, "file-view-notes"));

        Assert.NotNull(view.Find("[data-testid='file-view-notes']"));
        Assert.NotNull(view.Find("[data-testid='file-view-notes-body']"));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(5837, "5,7 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(5242880, "5 MB")]
    [InlineData(1073741824, "1 GB")]
    public void A_size_is_said_the_way_a_reader_would_say_it(long bytes, string expected)
    {
        // Pinned to one culture so the test asserts the rounding, not the decimal
        // separator of whoever is running it — the component itself formats in
        // the current culture on purpose.
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("nl-NL");

        try
        {
            Assert.Equal(expected, FileView.FormatSize(bytes));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
