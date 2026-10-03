namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// One Devbook page or chapter something points at, drawn as a chip: its title, the
/// chapter's status, a mark when it no longer names anything, and the two acts a
/// holder of the reference has — open it, drop it.
/// <para>
/// The raw reference is never the visible text when there is a title, and always
/// the tooltip, so a reader can still see exactly what was stored.
/// </para>
/// </summary>
public sealed class DevbookReferenceChipTests
{
    private const string Chapter = ".devbook/domain/tasks/domain.md#task";

    [Fact]
    public void The_title_is_drawn_and_the_reference_is_the_tooltip()
    {
        using var context = new BunitContext();

        var chip = context.Render<DevbookReferenceChip>(parameters => parameters
            .Add(c => c.Reference, Chapter)
            .Add(c => c.Title, "Task"));

        var label = chip.Find(".devbook-chip__label");
        Assert.Equal("Task", label.TextContent.Trim());
        Assert.Equal(Chapter, label.GetAttribute("title"));
        Assert.Equal(Chapter, chip.Find(".devbook-chip").GetAttribute("data-reference"));
    }

    [Fact]
    public void With_no_title_the_reference_reads_as_its_own_label()
    {
        using var context = new BunitContext();

        var chip = context.Render<DevbookReferenceChip>(parameters => parameters
            .Add(c => c.Reference, Chapter));

        Assert.Equal("task", chip.Find(".devbook-chip__label").TextContent.Trim());
    }

    [Fact]
    public void A_chapter_status_is_the_folder_aware_status_badge()
    {
        using var context = new BunitContext();

        var chip = context.Render<DevbookReferenceChip>(parameters => parameters
            .Add(c => c.Reference, Chapter)
            .Add(c => c.Title, "Task")
            .Add(c => c.Status, "active"));

        var status = chip.Find(".devbook-chip .badge--status");
        Assert.Equal("active", status.TextContent.Trim());
    }

    [Fact]
    public void No_status_draws_no_badge_and_no_mark()
    {
        using var context = new BunitContext();

        var chip = context.Render<DevbookReferenceChip>(parameters => parameters
            .Add(c => c.Reference, ".devbook/domain/tasks/domain.md")
            .Add(c => c.Title, "Tasks"));

        Assert.Empty(chip.FindAll(".badge"));
        Assert.Empty(chip.FindAll(".devbook-chip__broken"));
        Assert.DoesNotContain("devbook-chip--broken", chip.Find(".devbook-chip").ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void A_broken_reference_says_why_in_words_and_not_in_colour_alone()
    {
        using var context = new BunitContext();

        var chip = context.Render<DevbookReferenceChip>(parameters => parameters
            .Add(c => c.Reference, ".devbook/domain/tasks/domain.md#renamed")
            .Add(c => c.Title, "renamed")
            .Add(c => c.Broken, true)
            .Add(c => c.Problem, "No heading renamed on that page any more."));

        Assert.Contains("devbook-chip--broken", chip.Find(".devbook-chip").ClassName, StringComparison.Ordinal);

        var mark = chip.Find(".devbook-chip__broken");
        Assert.Equal("img", mark.GetAttribute("role"));
        Assert.Equal("No heading renamed on that page any more.", mark.GetAttribute("aria-label"));
        Assert.Equal("No heading renamed on that page any more.", mark.GetAttribute("title"));

        // The tooltip on the label carries the problem too, after the reference,
        // so a pointer user who hovers the words learns it without finding the mark.
        Assert.Equal(
            ".devbook/domain/tasks/domain.md#renamed — No heading renamed on that page any more.",
            chip.Find(".devbook-chip__label").GetAttribute("title"));
    }

    [Fact]
    public void Pressing_the_label_opens_the_reference()
    {
        using var context = new BunitContext();
        string? opened = null;

        var chip = context.Render<DevbookReferenceChip>(parameters => parameters
            .Add(c => c.Reference, Chapter)
            .Add(c => c.Title, "Task")
            .Add(c => c.OnOpen, (string reference) => opened = reference));

        var label = chip.Find("button.devbook-chip__label");
        Assert.Equal("Open Task (domain/tasks/domain.md#task) in the Devbook", label.GetAttribute("aria-label"));

        label.Click();

        Assert.Equal(Chapter, opened);
    }

    [Fact]
    public void With_nothing_to_open_it_the_label_is_text_and_takes_no_tab_stop()
    {
        using var context = new BunitContext();

        var chip = context.Render<DevbookReferenceChip>(parameters => parameters
            .Add(c => c.Reference, Chapter)
            .Add(c => c.Title, "Task"));

        Assert.Empty(chip.FindAll("button.devbook-chip__label"));
        Assert.Equal("span", chip.Find(".devbook-chip__label").TagName, ignoreCase: true);
    }

    [Fact]
    public void The_remove_control_names_what_it_removes_and_hands_back_the_reference()
    {
        using var context = new BunitContext();
        string? removed = null;

        var chip = context.Render<DevbookReferenceChip>(parameters => parameters
            .Add(c => c.Reference, Chapter)
            .Add(c => c.Title, "Task")
            .Add(c => c.OnRemove, (string reference) => removed = reference));

        var remove = chip.Find(".devbook-chip__remove");
        Assert.Equal("Remove Task (domain/tasks/domain.md#task)", remove.GetAttribute("aria-label"));

        remove.Click();

        Assert.Equal(Chapter, removed);
    }

    [Fact]
    public void Without_a_remove_handler_there_is_no_remove_control()
    {
        using var context = new BunitContext();

        var chip = context.Render<DevbookReferenceChip>(parameters => parameters
            .Add(c => c.Reference, Chapter)
            .Add(c => c.Title, "Task"));

        Assert.Empty(chip.FindAll(".devbook-chip__remove"));
    }

    [Fact]
    public void Disabled_leaves_both_acts_drawn_and_inert()
    {
        using var context = new BunitContext();
        var acts = 0;

        var chip = context.Render<DevbookReferenceChip>(parameters => parameters
            .Add(c => c.Reference, Chapter)
            .Add(c => c.Title, "Task")
            .Add(c => c.Disabled, true)
            .Add(c => c.OnOpen, (string _) => acts++)
            .Add(c => c.OnRemove, (string _) => acts++));

        Assert.True(chip.Find("button.devbook-chip__label").HasAttribute("disabled"));
        Assert.True(chip.Find(".devbook-chip__remove").HasAttribute("disabled"));
        Assert.Equal(0, acts);
    }

    /// <summary>Several pages share a title — more than one is called "Tasks" — so a
    /// name made of the title alone gave a row of chips identical names. The page
    /// the reference lies in tells them apart, for both acts.</summary>
    [Fact]
    public void Two_chips_with_one_title_have_names_that_tell_them_apart()
    {
        using var context = new BunitContext();

        IRenderedComponent<DevbookReferenceChip> Chip(string reference) =>
            context.Render<DevbookReferenceChip>(parameters => parameters
                .Add(c => c.Reference, reference)
                .Add(c => c.Title, "Tasks")
                .Add(c => c.OnOpen, (string _) => { })
                .Add(c => c.OnRemove, (string _) => { }));

        var domain = Chip(".devbook/domain/tasks/domain.md");
        var features = Chip(".devbook/domain/tasks/features.md");

        Assert.Equal("Open Tasks (domain/tasks/domain.md) in the Devbook", domain.Find("button.devbook-chip__label").GetAttribute("aria-label"));
        Assert.Equal("Open Tasks (domain/tasks/features.md) in the Devbook", features.Find("button.devbook-chip__label").GetAttribute("aria-label"));
        Assert.Equal("Remove Tasks (domain/tasks/features.md)", features.Find(".devbook-chip__remove").GetAttribute("aria-label"));

        // The visible text stays the title; the location is for the name only.
        Assert.Equal("Tasks", features.Find(".devbook-chip__label").TextContent.Trim());
    }

    /// <summary>A reference outside the devbook folder keeps its path whole: there is
    /// no <c>.devbook/</c> to take off it.</summary>
    [Fact]
    public void A_legacy_location_is_named_as_it_is_stored()
    {
        using var context = new BunitContext();

        var chip = context.Render<DevbookReferenceChip>(parameters => parameters
            .Add(c => c.Reference, ".domain/tasks/domain.md#task")
            .Add(c => c.Title, "Task")
            .Add(c => c.OnOpen, (string _) => { }));

        Assert.Equal("Open Task (.domain/tasks/domain.md#task) in the Devbook", chip.Find("button.devbook-chip__label").GetAttribute("aria-label"));
    }

    /// <summary>The mark beside a broken chip has the reason as its name, but the
    /// control a keyboard user lands on is the label — so the label's own name has
    /// to carry the problem too, or tabbing through the chips never meets it.</summary>
    [Fact]
    public void A_broken_chip_s_open_control_names_the_problem()
    {
        using var context = new BunitContext();

        var chip = context.Render<DevbookReferenceChip>(parameters => parameters
            .Add(c => c.Reference, ".devbook/domain/tasks/domain.md#renamed")
            .Add(c => c.Title, "renamed")
            .Add(c => c.Broken, true)
            .Add(c => c.Problem, "No heading with this anchor on the page any more.")
            .Add(c => c.OnOpen, (string _) => { }));

        Assert.Equal(
            "Open renamed (domain/tasks/domain.md#renamed) in the Devbook — No heading with this anchor on the page any more.",
            chip.Find("button.devbook-chip__label").GetAttribute("aria-label"));
    }

    /// <summary>A page and a heading on it that has gone share a title and a page,
    /// so the anchor is what tells their names apart — and the broken one's remove
    /// control says what is wrong with what it removes, as its open control does.</summary>
    [Fact]
    public void A_page_and_a_broken_heading_on_it_have_different_names()
    {
        using var context = new BunitContext();

        var page = context.Render<DevbookReferenceChip>(parameters => parameters
            .Add(c => c.Reference, ".devbook/domain/tasks/features.md")
            .Add(c => c.Title, "Tasks")
            .Add(c => c.OnRemove, (string _) => { }));

        var heading = context.Render<DevbookReferenceChip>(parameters => parameters
            .Add(c => c.Reference, ".devbook/domain/tasks/features.md#x")
            .Add(c => c.Title, "Tasks")
            .Add(c => c.Broken, true)
            .Add(c => c.Problem, "No heading with this anchor on the page any more.")
            .Add(c => c.OnRemove, (string _) => { }));

        Assert.Equal("Remove Tasks (domain/tasks/features.md)", page.Find(".devbook-chip__remove").GetAttribute("aria-label"));
        Assert.Equal(
            "Remove Tasks (domain/tasks/features.md#x) — No heading with this anchor on the page any more.",
            heading.Find(".devbook-chip__remove").GetAttribute("aria-label"));
    }
}
