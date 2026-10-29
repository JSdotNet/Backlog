using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Devbook menu over the Brain (local ADR 0026 §2, ADR 0027 §4): one root per
/// area the Brain's <c>CONVENTIONS.md</c> declares, in its order, or the PARA set,
/// each listing that area folder's notes — and keyed apart from every repository
/// area, so no repository special case reaches a Brain folder that happens to share
/// its name.
/// </summary>
public sealed class DevbookMenuBrainTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "devbook-menu-brain-tests-" + Guid.NewGuid().ToString("N"));
    private string? _setting;

    private string Brain => Path.Combine(_root, "Brain");

    private BrainFolderSource Source() => new(() => _setting);

    /// <summary>The repository side of the menu, which a Brain load never asks.</summary>
    private static DevbookMenu Menu() => new(new BrainFolderSource(() => null));

    [Fact]
    public async Task Declared_areas_are_the_roots_in_their_declared_order()
    {
        Write(BrainConventions.FileName, "## Areas\n\n- Work — `work`\n- Home life — `home`\n");
        Write("work/standup.md", "# Standup\n");
        Write("home/garden.md", "# Garden\n");
        _setting = Brain;

        var tree = await Menu().LoadBrainAsync(Source(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["Work", "Home life"], tree.Roots.Select(root => root.Label));
        Assert.Equal(["brain/work", "brain/home"], tree.Roots.Select(root => root.AreaKey));
        var standup = Assert.Single(tree.Roots[0].Children);
        Assert.Equal(DevbookMenuNodeKind.File, standup.Kind);
        Assert.Equal("work/standup.md", standup.Path);
        Assert.Equal("Standup", standup.Label);
    }

    [Fact]
    public async Task No_declaration_gives_the_para_roots()
    {
        Directory.CreateDirectory(Brain);
        _setting = Brain;

        var tree = await Menu().LoadBrainAsync(Source(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            ["Inbox", "Projects", "Areas", "Resources", "People", "Journal", "Archive"],
            tree.Roots.Select(root => root.Label));
        Assert.Equal(
            ["_inbox", "projects", "areas", "resources", "people", "journal", "archive"],
            tree.Roots.Select(root => root.Path));
    }

    [Fact]
    public async Task A_missing_area_folder_is_an_unavailable_root_that_says_so()
    {
        Write("projects/plan.md", "# Plan\n");
        _setting = Brain;

        var tree = await Menu().LoadBrainAsync(Source(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(tree.Roots.Single(root => root.Path == "projects").Available);
        var journal = tree.Roots.Single(root => root.Path == "journal");
        Assert.False(journal.Available);
        Assert.Empty(journal.Children);
        Assert.False(string.IsNullOrWhiteSpace(journal.Message));
    }

    [Fact]
    public async Task Dot_folders_and_dot_files_are_not_listed()
    {
        Write("projects/plan.md", "# Plan\n");
        Write("projects/.obsidian/workspace.md", "x");
        Write("projects/.draft.md", "x");
        Write("projects/launch/brief.md", "# Brief\n");
        _setting = Brain;

        var tree = await Menu().LoadBrainAsync(Source(), cancellationToken: TestContext.Current.CancellationToken);

        var projects = tree.Roots.Single(root => root.Path == "projects");
        Assert.Equal(["projects/launch", "projects/plan.md"], projects.Children.Select(node => node.Path).Order(StringComparer.Ordinal));
        Assert.Equal("projects/launch/brief.md", Assert.Single(projects.Children.Single(node => node.Path == "projects/launch").Children).Path);
    }

    /// <summary>Show hidden off: the menu is the areas, declared then discovered,
    /// and no area lists a dot-entry.</summary>
    [Fact]
    public async Task Without_show_hidden_the_hidden_folders_and_dot_entries_are_not_listed()
    {
        WriteHiddenBrain();
        _setting = Brain;

        var tree = await Menu().LoadBrainAsync(Source(), showHidden: false, TestContext.Current.CancellationToken);

        Assert.Equal(["Inbox", "Meetings"], tree.Roots.Select(root => root.Label));
        Assert.Equal(["_inbox/idea.md"], tree.Roots[0].Children.Select(node => node.Path));
    }

    /// <summary>Show hidden on (local ADR 0026 §2): the ignored folders and the
    /// dot-folders follow every other root, by folder name, and every area lists
    /// its dot-entries — while a <c>_</c> entry inside an area stays unlisted.</summary>
    [Fact]
    public async Task Show_hidden_lists_the_hidden_folders_last_and_every_dot_entry()
    {
        WriteHiddenBrain();
        _setting = Brain;

        var tree = await Menu().LoadBrainAsync(Source(), showHidden: true, TestContext.Current.CancellationToken);

        Assert.Equal(["Inbox", "Meetings", "Obsidian", "Templates"], tree.Roots.Select(root => root.Label));
        Assert.Equal(["brain/_inbox", "brain/meetings", "brain/.obsidian", "brain/_templates"], tree.Roots.Select(root => root.AreaKey));
        Assert.Equal(["_inbox/.draft.md", "_inbox/idea.md"], tree.Roots[0].Children.Select(node => node.Path).Order(StringComparer.Ordinal));
        Assert.Equal(["Meetings/.trash", "Meetings/standup.md"], tree.Roots[1].Children.Select(node => node.Path).Order(StringComparer.Ordinal));
        Assert.Equal([".obsidian/workspace.md"], tree.Roots[2].Children.Select(node => node.Path));
        Assert.Equal(["_templates/meeting.md"], tree.Roots[3].Children.Select(node => node.Path));
    }

    /// <summary>Declares the inbox and ignores <c>_templates</c>; a discovered
    /// <c>Meetings</c>, a dot-folder, and dot-entries and a <c>_</c> entry inside areas.</summary>
    private void WriteHiddenBrain()
    {
        Write(BrainConventions.FileName, "## Areas\n\n- Inbox — `_inbox`\n\n## Ignore\n\n- `_templates`\n");
        Write("_inbox/idea.md", "# Idea\n");
        Write("_inbox/.draft.md", "# Draft\n");
        Write("_inbox/_scratch.md", "# Scratch\n");
        Write("Meetings/standup.md", "# Standup\n");
        Write("Meetings/.trash/old.md", "# Old\n");
        Write("_templates/meeting.md", "# Meeting\n");
        Write(".obsidian/workspace.md", "x");
    }

    [Fact]
    public async Task A_brain_area_named_like_a_repository_area_takes_none_of_its_special_cases()
    {
        Write(BrainConventions.FileName, "## Areas\n\n- `domain`\n");
        Write("domain/apples.md", "# Apples\n");
        Write("domain/context-map.md", "# Context map\n");
        _setting = Brain;

        var tree = await Menu().LoadBrainAsync(Source(), cancellationToken: TestContext.Current.CancellationToken);

        var domain = Assert.Single(tree.Roots);
        Assert.Equal("brain/domain", domain.AreaKey);
        Assert.Equal(["domain/apples.md", "domain/context-map.md"], domain.Children.Select(node => node.Path));
        Assert.All(domain.Children, node => Assert.Equal("brain/domain", node.AreaKey));
        Assert.DoesNotContain(domain.Children, node => string.Equals(node.Key, "context-map.md", StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_brain_folder_set_or_found_gives_no_roots()
    {
        Assert.Empty((await Menu().LoadBrainAsync(Source(), cancellationToken: TestContext.Current.CancellationToken)).Roots);

        _setting = Brain;
        Assert.Empty((await Menu().LoadBrainAsync(Source(), cancellationToken: TestContext.Current.CancellationToken)).Roots);
    }

    [Fact]
    public async Task The_journal_sorts_newest_first_at_every_level_and_other_areas_by_name()
    {
        Write("journal/2026-09-30.md", "# Sep 30\n");
        Write("journal/2026-10-13.md", "# Oct 13\n");
        Write("journal/2026-10-01.md", "# Oct 1\n");
        Write("journal/2025/12-24.md", "# Christmas Eve\n");
        Write("journal/2025/01-02.md", "# New year\n");
        Write("projects/beta.md", "# Beta\n");
        Write("projects/alpha.md", "# Alpha\n");
        _setting = Brain;

        var tree = await Menu().LoadBrainAsync(Source(), cancellationToken: TestContext.Current.CancellationToken);

        var journal = tree.Roots.Single(root => root.Path == "journal");
        Assert.Equal(
            ["journal/2026-10-13.md", "journal/2026-10-01.md", "journal/2026-09-30.md", "journal/2025"],
            journal.Children.Select(node => node.Path));
        Assert.Equal(
            ["journal/2025/12-24.md", "journal/2025/01-02.md"],
            journal.Children.Single(node => node.Path == "journal/2025").Children.Select(node => node.Path));

        var projects = tree.Roots.Single(root => root.Path == "projects");
        Assert.Equal(["projects/alpha.md", "projects/beta.md"], projects.Children.Select(node => node.Path));
    }

    [Fact]
    public async Task An_area_conventions_marks_as_dated_sorts_newest_first()
    {
        Write(BrainConventions.FileName,
            "## Areas\n\n| Area | Folder | Holds |\n|---|---|---|\n| Log | log | Dated entries, one a day. |\n| Notes | notes | Updated now and then. |\n");
        Write("log/2026-01-01.md", "# A\n");
        Write("log/2026-02-01.md", "# B\n");
        Write("notes/2026-01-01.md", "# A\n");
        Write("notes/2026-02-01.md", "# B\n");
        _setting = Brain;

        var tree = await Menu().LoadBrainAsync(Source(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["log/2026-02-01.md", "log/2026-01-01.md"], tree.Roots[0].Children.Select(node => node.Path));
        Assert.Equal(["notes/2026-01-01.md", "notes/2026-02-01.md"], tree.Roots[1].Children.Select(node => node.Path));
    }

    [Theory]
    [InlineData("journal", "", true)]
    [InlineData("Journal", "", true)]
    [InlineData("log", "Dated entries.", true)]
    [InlineData("log", "Notes, undated.", false)]
    [InlineData("projects", "Updated weekly.", false)]
    public void A_dated_area_is_the_journal_or_one_its_holds_line_calls_dated(string folder, string holds, bool dated) =>
        Assert.Equal(dated, BrainConventions.IsDated(new BrainArea("Area", folder, 1, holds)));

    [Theory]
    [InlineData("brain/work", true)]
    [InlineData("domain", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_brain_area_key_is_told_apart_from_a_repository_one(string? key, bool brain)
    {
        Assert.Equal(brain, DevbookMenu.IsBrainAreaKey(key));
        Assert.Equal("brain/work", DevbookMenu.BrainAreaKey("Work"));
    }

    private void Write(string relativePath, string text)
    {
        var file = Path.Combine(Brain, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
