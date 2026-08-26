namespace Backlog.UI.Components.UnitTests;

public sealed class FolderViewTests
{
    private static readonly IReadOnlyList<FolderEntry> Tree =
    [
        FolderEntry.File("index.md", "Index", 1_204),
        FolderEntry.Folder("adr", "ADR",
            FolderEntry.File("adr/0001.md", "0001", 3_180),
            FolderEntry.File("adr/0002.md", "0002", 2_048))
    ];

    private static IRenderedComponent<FolderView> Render(
        BunitContext context,
        Action<ComponentParameterCollectionBuilder<FolderView>> parameters)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context.Render(parameters);
    }

    [Fact]
    public void The_header_says_what_the_folder_is_and_counts_what_is_in_it()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Name, ".arc42")
            .Add(v => v.Path, @"D:\Repos\Backlog\.arc42")
            .Add(v => v.Source, "Repository")
            .Add(v => v.Entries, Tree));

        Assert.Equal(".arc42", view.Find(".folder-view__name").TextContent);
        Assert.Equal(@"D:\Repos\Backlog\.arc42", view.Find(".folder-view__path").TextContent);

        // Three files and one folder, counted through the whole tree.
        Assert.Equal("Repository · 3 files, 1 folder", view.Find(".folder-view__meta").TextContent);
    }

    [Fact]
    public void A_kind_the_caller_gave_replaces_the_count_rather_than_joining_it()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Name, "src")
            .Add(v => v.Kind, "Source tree")
            .Add(v => v.Entries, Tree));

        Assert.Equal("Source tree", view.Find(".folder-view__meta").TextContent);
    }

    [Fact]
    public void A_folder_with_nothing_in_it_counts_nothing_and_says_so()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Name, "drafts")
            .Add(v => v.Entries, Array.Empty<FolderEntry>()));

        Assert.Empty(view.FindAll(".folder-view__meta"));
        Assert.Contains("empty", view.Find(".folder-tree__empty, .folder-tree__message, .folder-view__body p").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void By_default_the_top_level_folders_are_open()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Name, ".arc42")
            .Add(v => v.Entries, Tree));

        var labels = view.FindAll(".folder-tree__label").Select(l => l.TextContent).ToList();

        Assert.Contains(labels, l => l.StartsWith("Index", StringComparison.Ordinal));
        Assert.Contains("ADR", labels);
        Assert.Contains(labels, l => l.StartsWith("0001", StringComparison.Ordinal));
    }

    [Fact]
    public void Depth_zero_starts_everything_folded()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Name, ".arc42")
            .Add(v => v.Entries, Tree)
            .Add(v => v.InitiallyExpandedDepth, 0));

        var labels = view.FindAll(".folder-tree__label").Select(l => l.TextContent).ToList();

        Assert.Contains("ADR", labels);
        Assert.DoesNotContain(labels, l => l.StartsWith("0001", StringComparison.Ordinal));
    }

    [Fact]
    public void Clicking_a_folder_opens_it()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Name, ".arc42")
            .Add(v => v.Entries, Tree)
            .Add(v => v.InitiallyExpandedDepth, 0));

        view.FindAll(".folder-tree__item").Single(b => b.TextContent.Contains("ADR", StringComparison.Ordinal)).Click();

        Assert.Contains(
            view.FindAll(".folder-tree__label"),
            l => l.TextContent.StartsWith("0001", StringComparison.Ordinal));
    }

    [Fact]
    public void Clicking_it_again_closes_it()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Name, ".arc42")
            .Add(v => v.Entries, Tree));

        var adr = () => view.FindAll(".folder-tree__item").Single(b => b.TextContent.Contains("ADR", StringComparison.Ordinal));

        // Open to begin with, at the default depth.
        adr().Click();

        Assert.DoesNotContain(
            view.FindAll(".folder-tree__label"),
            l => l.TextContent.StartsWith("0001", StringComparison.Ordinal));
    }

    [Fact]
    public void Selecting_a_row_reports_its_path_and_the_entry_behind_it()
    {
        using var context = new BunitContext();

        string? selectedPath = null;
        FolderEntry? selectedEntry = null;

        var view = Render(context, p => p
            .Add(v => v.Name, ".arc42")
            .Add(v => v.Entries, Tree)
            .Add(v => v.SelectedPathChanged, path => selectedPath = path)
            .Add(v => v.OnEntrySelected, entry => selectedEntry = entry));

        view.FindAll(".folder-tree__item").Single(b => b.TextContent.Contains("Index", StringComparison.Ordinal)).Click();

        Assert.Equal("index.md", selectedPath);
        Assert.Equal("Index", selectedEntry?.Name);
    }

    [Fact]
    public void The_selected_row_says_so_to_a_screen_reader_as_well_as_in_its_class()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Name, ".arc42")
            .Add(v => v.Entries, Tree)
            .Add(v => v.SelectedPath, "index.md"));

        var row = view.FindAll(".folder-tree__item").Single(b => b.TextContent.Contains("Index", StringComparison.Ordinal));

        Assert.Contains("folder-tree__item--active", row.ClassList);
        Assert.Equal("true", row.GetAttribute("aria-selected"));
    }

    [Fact]
    public void A_files_size_rides_on_its_label()
    {
        // The tree indents, so a right-hand column would sit a different
        // distance from every name it belongs to.
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Name, ".arc42")
            .Add(v => v.Entries, Tree));

        var label = view.FindAll(".folder-tree__label").Single(l => l.TextContent.StartsWith("Index", StringComparison.Ordinal));

        Assert.Contains(FileHeader.FormatSize(1_204), label.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Something_that_cannot_be_read_still_has_a_row_and_says_why()
    {
        // An empty tree would say the folder is empty, which is a different and
        // untrue thing.
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Name, "secrets")
            .Add(v => v.Entries, new FolderEntry[]
            {
                new("keys", "Keys", FolderEntryKind.Folder, null, null, false, "Access was denied.")
            }));

        var row = view.Find(".folder-tree__item");

        Assert.True(row.HasAttribute("disabled"));
        Assert.Equal("Access was denied.", row.GetAttribute("title"));
    }

    [Fact]
    public void The_tree_does_not_add_a_tab_stop_of_its_own_around_rows_that_are_already_tab_stops()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Name, ".arc42")
            .Add(v => v.Entries, Tree));

        var body = view.Find(".folder-view__body");

        Assert.False(body.HasAttribute("tabindex"));
        Assert.Equal(".arc42", body.GetAttribute("aria-label"));
    }

    [Fact]
    public void Fill_hands_the_height_to_the_host_and_drops_the_cap()
    {
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Name, ".arc42")
            .Add(v => v.Entries, Tree)
            .Add(v => v.Fill, true)
            .Add(v => v.MaxHeight, "12rem"));

        Assert.Contains("folder-view--fill", view.Find(".folder-view").ClassList);
        Assert.Null(view.Find(".folder-view__body").GetAttribute("style"));
    }

    [Fact]
    public void Expansion_survives_the_host_re_rendering_for_its_own_reasons()
    {
        // Re-seeding on every parameter change would reopen a folder the reader
        // had just closed, every time anything else on the page moved.
        using var context = new BunitContext();

        var view = Render(context, p => p
            .Add(v => v.Name, ".arc42")
            .Add(v => v.Entries, Tree)
            .Add(v => v.InitiallyExpandedDepth, 0));

        view.FindAll(".folder-tree__item").Single(b => b.TextContent.Contains("ADR", StringComparison.Ordinal)).Click();

        view.Render(p => p
            .Add(v => v.Name, ".arc42")
            .Add(v => v.Entries, Tree)
            .Add(v => v.InitiallyExpandedDepth, 0)
            .Add(v => v.Source, "Repository"));

        Assert.Contains(
            view.FindAll(".folder-tree__label"),
            l => l.TextContent.StartsWith("0001", StringComparison.Ordinal));
    }

    [Fact]
    public void Flatten_walks_depth_first_which_is_the_order_it_is_written_on_screen()
    {
        var paths = Tree.SelectMany(entry => entry.Flatten()).Select(entry => entry.Path);

        Assert.Equal(["index.md", "adr", "adr/0001.md", "adr/0002.md"], paths);
    }
}
