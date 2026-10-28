using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// The areas of a Brain (local ADR 0026 §2): the ones its <c>CONVENTIONS.md</c>
/// declares, or the PARA set when it declares none, then every other top folder
/// holding a note, minus the folders its <c>Ignore</c> section lists —
/// <c>requirements.md#the-areas-are-the-declared-ones-then-the-discovered-ones</c>
/// and <c>requirements.md#an-ignored-folder-has-no-tab</c> in the devbook context,
/// from the parsing side.
/// </summary>
public class BrainConventionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-brain-conventions", Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_table_declares_the_areas_in_its_order()
    {
        var areas = BrainConventions.Parse("""
            # Conventions

            ## Areas

            | Area | Folder |
            |------|--------|
            | Work | work |
            | Home life | `home` |
            """);

        Assert.Equal([new BrainArea("Work", "work", 1), new BrainArea("Home life", "home", 2)], areas);
    }

    [Fact]
    public void A_table_s_order_column_wins_over_the_row_order()
    {
        var areas = BrainConventions.Parse("""
            ## Areas

            | Name | Folder | Order |
            |---|---|---|
            | Later | later | 2 |
            | Unnumbered | unnumbered | |
            | First | first | 1 |
            """);

        Assert.Equal(["first", "later", "unnumbered"], areas.Select(area => area.Folder));
        Assert.Equal([1, 2, 3], areas.Select(area => area.Order));
    }

    [Fact]
    public void A_list_declares_the_areas_with_or_without_names()
    {
        var areas = BrainConventions.Parse("""
            # Conventions

            ### Areas

            - Inbox — `_inbox`
            - `projects`
            - people
            * Side work: `side-work`
            """);

        Assert.Equal(
            [
                new BrainArea("Inbox", "_inbox", 1),
                new BrainArea("Projects", "projects", 2),
                new BrainArea("People", "people", 3),
                new BrainArea("Side work", "side-work", 4)
            ],
            areas);
    }

    [Fact]
    public void Only_the_areas_section_is_read()
    {
        var areas = BrainConventions.Parse("""
            # Conventions

            - not an area

            ## Areas

            - `work`

            ```markdown
            - `fenced`
            ```

            ## Naming

            - `naming`
            """);

        Assert.Equal(["work"], areas.Select(area => area.Folder));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData(".hidden")]
    [InlineData("C:")]
    public void A_folder_that_is_not_one_plain_segment_is_skipped(string folder)
    {
        var areas = BrainConventions.Declared($"## Areas\n\n- `{folder}`\n- `kept`\n");

        Assert.Equal(["kept"], areas.Select(area => area.Folder));
    }

    [Fact]
    public void A_byte_order_mark_does_not_hide_the_first_heading()
    {
        var areas = BrainConventions.Declared("﻿## Areas\n\n- `work`\n");

        Assert.Equal(["work"], areas.Select(area => area.Folder));
    }

    [Fact]
    public void A_holds_column_is_read()
    {
        var areas = BrainConventions.Declared("## Areas\n\n| Area | Folder | Holds |\n|:---|:---:|---|\n| Clients | `clients` | One folder per client. |\n");

        Assert.Equal([new BrainArea("Clients", "clients", 1, "One folder per client.")], areas);
    }

    [Fact]
    public void A_folder_declared_twice_counts_once()
    {
        var areas = BrainConventions.Declared("## Areas\n\n- `work`\n- Work again — `Work`\n- `home`\n");

        Assert.Equal([new BrainArea("Work", "work", 1), new BrainArea("Home", "home", 2)], areas);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("# Conventions\n\nNo areas here.\n")]
    [InlineData("## Areas\n\nNone declared yet.\n")]
    public void Nothing_declared_is_the_para_set(string? markdown)
    {
        Assert.Empty(BrainConventions.Declared(markdown));
        Assert.Equal(
            ["_inbox", "projects", "areas", "resources", "people", "journal", "archive"],
            BrainConventions.Parse(markdown).Select(area => area.Folder));
    }

    [Fact]
    public void A_brain_without_conventions_reads_as_para()
    {
        Directory.CreateDirectory(_root);

        Assert.Same(BrainConventions.Para, BrainConventions.Read(_root));
        Assert.Same(BrainConventions.Para, BrainConventions.Read(null));
    }

    [Fact]
    public void A_brain_s_conventions_file_is_read_from_its_root()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, BrainConventions.FileName), "## Areas\n\n- Work — `work`\n- Home — `home`\n");

        Assert.Equal(["work", "home"], BrainConventions.Read(_root).Select(area => area.Folder));
    }

    [Fact]
    public void An_ignore_list_names_its_folders_in_backticks_or_bare()
    {
        var ignored = BrainConventions.Ignored("""
            # Conventions

            - `not-ignored`

            ## Areas

            - `work`

            ## Ignore

            - `_templates`
            - Attachments — `attachments`
            - scratch
            - `Scratch`
            - `../escape`

            ```markdown
            - `fenced`
            ```

            ## Notes

            - `notes`
            """);

        Assert.Equal(["_templates", "attachments", "scratch"], ignored);
    }

    [Fact]
    public void An_ignore_table_reads_its_folder_column()
    {
        var ignored = BrainConventions.Ignored("""
            ### ignore

            | Folder | Why |
            |---|---|
            | `_templates` | Note templates. |
            | Product | An agent's scratch. |
            """);

        Assert.Equal(["_templates", "Product"], ignored);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("## Areas\n\n- `work`\n")]
    public void No_ignore_section_ignores_nothing(string? markdown)
    {
        Assert.Empty(BrainConventions.Ignored(markdown));
    }

    [Fact]
    public void Discovered_areas_follow_the_declared_ones_by_folder_name()
    {
        var declared = BrainConventions.Declared("## Areas\n\n- Work — `work`\n- `missing`\n");

        var areas = BrainConventions.Resolve(
            declared,
            [],
            ["zeta", "work", "Meetings", "alpha", "empty", "_drafts"],
            folder => folder != "empty");

        Assert.Equal(
            [
                new BrainArea("Work", "work", 1),
                new BrainArea("Missing", "missing", 2),
                // Ordinal and case-insensitive, as .NET compares: upper case, so
                // an underscore sorts after the letters.
                new BrainArea("Alpha", "alpha", 3),
                new BrainArea("Meetings", "Meetings", 4),
                new BrainArea("Zeta", "zeta", 5),
                new BrainArea("Drafts", "_drafts", 6)
            ],
            areas);
    }

    /// <summary>The twin of brain.test.mjs's dotless-ı case: <c>ı</c> (U+0131) is
    /// no <c>I</c> to an ordinal ignore-case comparison.</summary>
    [Fact]
    public void Dotless_i_keeps_its_case()
    {
        var areas = BrainConventions.Resolve(BrainConventions.Declared("## Areas\n\n- `work`\n"), ["ILIK"], ["ılık", "kitap"], _ => true);

        Assert.Equal(["work", "kitap", "ılık"], areas.Select(area => area.Folder));
    }

    [Fact]
    public void A_declared_area_matches_its_folder_whatever_the_case()
    {
        var declared = BrainConventions.Declared("## Areas\n\n- `projects`\n");

        var areas = BrainConventions.Resolve(declared, [], ["Projects"], _ => true);

        Assert.Equal(["projects"], areas.Select(area => area.Folder));
    }

    [Fact]
    public void With_nothing_declared_the_para_set_leads_the_discovered_areas()
    {
        var areas = BrainConventions.Resolve([], [], ["projects", "meetings"], _ => true);

        Assert.Equal(
            ["_inbox", "projects", "areas", "resources", "people", "journal", "archive", "meetings"],
            areas.Select(area => area.Folder));
        Assert.Equal(Enumerable.Range(1, 8), areas.Select(area => area.Order));
    }

    [Fact]
    public void Nothing_declared_and_nothing_discovered_is_the_para_set_itself()
    {
        Assert.Same(BrainConventions.Para, BrainConventions.Resolve([], [], ["projects", "empty"], folder => folder == "projects"));
    }

    [Fact]
    public void An_ignored_folder_is_never_an_area_even_when_declared()
    {
        var declared = BrainConventions.Declared("## Areas\n\n- `_inbox`\n- Templates — `_templates`\n");

        var areas = BrainConventions.Resolve(declared, ["_TEMPLATES", "product"], ["_templates", "Product", "meetings"], _ => true);

        Assert.Equal([new BrainArea("Inbox", "_inbox", 1), new BrainArea("Meetings", "meetings", 2)], areas);
    }

    [Fact]
    public void Ignoring_every_declared_area_does_not_bring_para_back()
    {
        var declared = BrainConventions.Declared("## Areas\n\n- `work`\n");

        Assert.Empty(BrainConventions.Resolve(declared, ["work"], ["work"], _ => true));
    }

    [Fact]
    public void Only_a_plain_undeclared_unignored_folder_is_probed()
    {
        var declared = BrainConventions.Declared("## Areas\n\n- `work`\n");
        var probed = new List<string>();

        BrainConventions.Resolve(declared, ["_templates"], ["work", "_templates", ".obsidian", "meetings"], folder =>
        {
            probed.Add(folder);
            return true;
        });

        Assert.Equal(["meetings"], probed);
    }

    [Fact]
    public void A_top_folder_is_an_area_once_a_note_sits_anywhere_below_it()
    {
        File.WriteAllText(Write(BrainConventions.FileName), "## Areas\n\n- `_inbox`\n\n## Ignore\n\n- `_templates`\n");
        File.WriteAllText(Write("Meetings/2026/10/standup.md"), "# Standup\n");
        File.WriteAllText(Write("Demo/readme.txt"), "not a note");
        File.WriteAllText(Write("trash-only/.trash/old.md"), "# Old\n");
        File.WriteAllText(Write("upper/NOTE.MD"), "# Upper\n");
        File.WriteAllText(Write("_templates/meeting.md"), "# Meeting\n");
        File.WriteAllText(Write(".obsidian/workspace.md"), "# Workspace\n");
        Directory.CreateDirectory(Path.Combine(_root, "empty"));
        File.WriteAllText(Write("README.md"), "# Brain\n");

        Assert.Equal(
            [new BrainArea("Inbox", "_inbox", 1), new BrainArea("Meetings", "Meetings", 2)],
            BrainConventions.Read(_root));
    }

    /// <summary>The probe asks the folder's listing, never a note: a note nobody
    /// can open — a cloud placeholder would be downloaded by opening it — still
    /// counts.</summary>
    [Fact]
    public void A_note_counts_by_its_name_without_being_opened()
    {
        var note = Write("locked/note.md");
        File.WriteAllText(note, "# Locked\n");

        using (new FileStream(note, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(["locked"], BrainConventions.Read(_root).Select(area => area.Folder).Except(BrainConventions.Para.Select(area => area.Folder)));
        }
    }

    [Fact]
    public void The_hidden_folders_are_the_ignored_and_the_dot_folders_by_name()
    {
        var hidden = BrainConventions.Hidden(["_templates", "missing"], ["work", ".trash", "_Templates", ".obsidian", "meetings"]);

        Assert.Equal(
            [new BrainArea("Obsidian", ".obsidian", 1), new BrainArea("Trash", ".trash", 2), new BrainArea("Templates", "_Templates", 3)],
            hidden);
    }

    [Fact]
    public void A_brain_s_hidden_folders_are_read_from_disk()
    {
        File.WriteAllText(Write(BrainConventions.FileName), "## Areas\n\n- `_inbox`\n\n## Ignore\n\n- `_templates`\n- `gone`\n");
        File.WriteAllText(Write("_templates/meeting.md"), "# Meeting\n");
        Directory.CreateDirectory(Path.Combine(_root, ".obsidian"));
        Directory.CreateDirectory(Path.Combine(_root, "work"));

        Assert.Equal([".obsidian", "_templates"], BrainConventions.Hidden(_root).Select(area => area.Folder));
        Assert.Empty(BrainConventions.Hidden(null));
        Assert.Empty(BrainConventions.Hidden(Path.Combine(_root, "absent")));
    }

    private string Write(string relativePath)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}
