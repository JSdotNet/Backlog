namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The three things a reader does to a file, all of them in the header: copy it,
/// edit it, see what changed in it.
///
/// <para>Every one of them is opt-in, and the first test is the reason: a pane
/// that asks for none of them has to render the header it always rendered. The
/// rest is the mode machinery — read, edit and compare are one at a time, and
/// which one is showing decides what the body is and what the scroll region is
/// called.</para>
/// </summary>
public sealed class FileViewActionsTests
{
    private const string Body = """
        # Domain

        What the domain is.

        ## The gate

        What the gate is.
        """;

    private const string Older = """
        # Domain

        What the domain was.

        ## The gate

        What the gate is.
        """;

    private static readonly FileCompareBaseline[] Opened =
        [new("opened", "As opened", Older)];

    private static readonly FileCompareBaseline[] Two =
    [
        new("opened", "As opened", Older),
        new("commit", "Last commit", "# Domain\n\nSomething older still.\n")
    ];

    /// <summary>Stands in for the host's editor, which is the only thing that
    /// knows what a keystroke means.</summary>
    private static RenderFragment Editor() => builder =>
    {
        builder.OpenElement(0, "textarea");
        builder.AddAttribute(1, "data-testid", "supplied-editor");
        builder.AddAttribute(2, "aria-label", "File source");
        builder.AddContent(3, Body);
        builder.CloseElement();
    };

    private static IRenderedComponent<FileView> Render(
        BunitContext context,
        Action<ComponentParameterCollectionBuilder<FileView>> extra) =>
        context.Render<FileView>(parameters =>
        {
            parameters
                .Add(v => v.Name, "domain.md")
                .Add(v => v.Body, Body)
                .Add(v => v.TestId, "file");
            extra(parameters);
        });

    [Fact]
    public void A_header_nobody_asked_anything_of_offers_nothing()
    {
        // Not even the group: an empty toolbar is chrome a reader has to read
        // past to find out it does nothing.
        using var context = new BunitContext();

        var view = Render(context, _ => { });

        Assert.Empty(view.FindAll(".file-view__actions"));
        Assert.Empty(view.FindAll("[data-testid='file-copy']"));
        Assert.Empty(view.FindAll("[data-testid='file-edit']"));
        Assert.Empty(view.FindAll("[data-testid='file-compare']"));
        Assert.Empty(view.FindAll(".file-view__baselines"));
    }

    [Fact]
    public void The_actions_are_named_by_the_file_they_belong_to()
    {
        // "Edit" and "Compare" on their own say nothing about which of several
        // files on a page they operate on, and the name is already here.
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.AllowCopy, true)
            .Add(v => v.CanEdit, true));

        var group = view.Find(".file-view__actions");

        Assert.Equal("group", group.GetAttribute("role"));
        Assert.Equal("domain.md actions", group.GetAttribute("aria-label"));
        Assert.Equal("Copy file domain.md", view.Find("[data-testid='file-copy']").GetAttribute("aria-label"));
    }

    [Fact]
    public void Copying_the_file_hands_over_its_source_and_not_its_rendering()
    {
        // The source is what round trips, which is the same call the read view
        // makes.
        using var context = new BunitContext();
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var view = Render(context, parameters => parameters.Add(v => v.AllowCopy, true));

        view.Find("[data-testid='file-copy']").Click();

        Assert.Equal(Body, Assert.Single(context.JSInterop.Invocations["backlogClipboard.copy"]).Arguments[0]);
        Assert.Equal("Copied", view.Find("[data-testid='file-copy-status']").TextContent);
    }

    [Fact]
    public void Editing_swaps_in_the_body_the_host_supplied_and_the_button_becomes_Done()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.CanEdit, true)
            .Add(v => v.EditBodyContent, Editor()));

        // Read is the resting state: the host's editor is not on screen until it
        // is asked for.
        Assert.Empty(view.FindAll("[data-testid='supplied-editor']"));
        Assert.Equal("Edit", view.Find("[data-testid='file-edit']").TextContent);

        view.Find("[data-testid='file-edit']").Click();

        Assert.NotNull(view.Find(".file-view__body [data-testid='supplied-editor']"));
        Assert.Empty(view.FindAll(".file-view__body .md-heading"));
        Assert.Equal("Done", view.Find("[data-testid='file-done']").TextContent);
        Assert.Empty(view.FindAll("[data-testid='file-edit']"));
    }

    [Fact]
    public void Done_gives_the_file_back_the_way_it_was_being_read()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.CanEdit, true)
            .Add(v => v.EditBodyContent, Editor()));

        view.Find("[data-testid='file-edit']").Click();
        view.Find("[data-testid='file-done']").Click();

        Assert.Empty(view.FindAll("[data-testid='supplied-editor']"));
        Assert.Equal("Domain", view.Find(".file-view__body .md-heading").TextContent);
    }

    [Fact]
    public void Asking_to_edit_with_nothing_to_edit_and_nobody_listening_offers_no_button()
    {
        // Permission is not enough: pressing it has to do something. With no
        // writable body and no host holding the mode, the button would announce an
        // edit and leave the same file on the screen — the same lie as a checkbox
        // that writes nowhere, which this component already refuses.
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters.Add(v => v.CanEdit, true));

        Assert.Empty(view.FindAll("[data-testid='file-edit']"));
        Assert.Equal("Domain", view.Find(".file-view__body .md-heading").TextContent);
        Assert.Equal("0", view.Find(".file-view__body").GetAttribute("tabindex"));
    }

    [Fact]
    public void A_host_that_swaps_its_own_body_is_enough_to_earn_the_button()
    {
        // The other honest case: no EditBodyContent, but the host holds Editing and
        // can swap the body it supplies. It is listening, so the press means
        // something, so the button is offered.
        using var context = new BunitContext();
        var editing = new List<bool>();

        var view = Render(context, parameters => parameters
            .Add(v => v.CanEdit, true)
            .Add(v => v.EditingChanged, EventCallback.Factory.Create<bool>(this, editing.Add)));

        view.Find("[data-testid='file-edit']").Click();

        Assert.Equal([true], editing);
    }

    [Fact]
    public void A_host_that_holds_the_mode_is_told_each_time_it_changes()
    {
        using var context = new BunitContext();
        var editing = new List<bool>();

        var view = Render(context, parameters => parameters
            .Add(v => v.CanEdit, true)
            .Add(v => v.EditBodyContent, Editor())
            .Add(v => v.EditingChanged, EventCallback.Factory.Create<bool>(this, editing.Add)));

        view.Find("[data-testid='file-edit']").Click();
        view.Find("[data-testid='file-done']").Click();

        Assert.Equal([true, false], editing);
    }

    [Fact]
    public void A_host_hears_that_comparing_started_before_it_has_to_answer_for_it()
    {
        // Which is the whole point of the callback: reading a committed version
        // costs a process, and the moment to pay for it is when a reader asks.
        using var context = new BunitContext();
        var comparing = new List<bool>();

        var view = Render(context, parameters => parameters
            .Add(v => v.CompareBaselines, Opened)
            .Add(v => v.ComparingChanged, EventCallback.Factory.Create<bool>(this, comparing.Add)));

        view.Find("[data-testid='file-compare']").Click();
        view.Find("[data-testid='file-compare']").Click();

        Assert.Equal([true, false], comparing);
    }

    [Fact]
    public void Comparing_is_a_state_the_pane_is_in_rather_than_a_word_that_flips()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters.Add(v => v.CompareBaselines, Opened));

        var toggle = view.Find("[data-testid='file-compare']");

        Assert.Equal("false", toggle.GetAttribute("aria-pressed"));
        Assert.Equal("Compare", toggle.TextContent);

        toggle.Click();

        Assert.Equal("true", view.Find("[data-testid='file-compare']").GetAttribute("aria-pressed"));
        Assert.Equal("Compare", view.Find("[data-testid='file-compare']").TextContent);
    }

    [Fact]
    public void Asking_to_compare_while_editing_stops_the_editing()
    {
        // A pane cannot be both the file being typed into and the file being read
        // against its history, and leaving the editor on under the comparison is
        // the version of this that works until the reader wonders which text they
        // are editing.
        using var context = new BunitContext();
        var editing = new List<bool>();

        var view = Render(context, parameters => parameters
            .Add(v => v.CanEdit, true)
            .Add(v => v.EditBodyContent, Editor())
            .Add(v => v.EditingChanged, EventCallback.Factory.Create<bool>(this, editing.Add))
            .Add(v => v.CompareBaselines, Opened));

        view.Find("[data-testid='file-edit']").Click();
        view.Find("[data-testid='file-compare']").Click();

        Assert.Equal([true, false], editing);
        Assert.Empty(view.FindAll("[data-testid='supplied-editor']"));
        Assert.NotNull(view.Find("[data-testid='file-compare-view']"));
        Assert.Equal("Edit", view.Find("[data-testid='file-edit']").TextContent);
    }

    [Fact]
    public void Asking_to_edit_while_comparing_stops_the_comparison()
    {
        using var context = new BunitContext();
        var comparing = new List<bool>();

        var view = Render(context, parameters => parameters
            .Add(v => v.CanEdit, true)
            .Add(v => v.EditBodyContent, Editor())
            .Add(v => v.CompareBaselines, Opened)
            .Add(v => v.Comparing, true)
            .Add(v => v.ComparingChanged, EventCallback.Factory.Create<bool>(this, comparing.Add)));

        Assert.NotNull(view.Find("[data-testid='file-compare-view']"));

        view.Find("[data-testid='file-edit']").Click();

        Assert.Equal([false], comparing);
        Assert.Empty(view.FindAll("[data-testid='file-compare-view']"));
        Assert.NotNull(view.Find("[data-testid='supplied-editor']"));
    }

    [Fact]
    public void The_baseline_picker_waits_for_a_reader_who_is_comparing()
    {
        // A picker beside a file nobody is comparing is a question nobody asked.
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters.Add(v => v.CompareBaselines, Two));

        Assert.Empty(view.FindAll(".file-view__baselines"));

        view.Find("[data-testid='file-compare']").Click();

        Assert.Equal("Compare domain.md against", view.Find(".file-view__baselines").GetAttribute("aria-label"));
        Assert.Equal(
            ["As opened", "Last commit"],
            view.FindAll(".file-view__baselines button").Select(button => button.TextContent));
    }

    [Fact]
    public void One_baseline_is_a_label_and_not_a_choice_so_no_picker_is_drawn()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.CompareBaselines, Opened)
            .Add(v => v.Comparing, true));

        Assert.Empty(view.FindAll(".file-view__baselines"));
        Assert.NotNull(view.Find("[data-testid='file-compare-view']"));
    }

    [Fact]
    public void The_first_baseline_is_the_one_compared_against_until_a_reader_says_otherwise()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.CompareBaselines, Two)
            .Add(v => v.Comparing, true));

        Assert.Equal("true", view.Find("[data-testid='file-baseline-opened']").GetAttribute("aria-pressed"));
        Assert.Equal("false", view.Find("[data-testid='file-baseline-commit']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void Picking_a_baseline_reports_it_and_compares_against_it()
    {
        using var context = new BunitContext();
        var picked = new List<string>();

        var view = Render(context, parameters => parameters
            .Add(v => v.CompareBaselines, Two)
            .Add(v => v.Comparing, true)
            .Add(v => v.CompareBaselineIdChanged, EventCallback.Factory.Create<string>(this, picked.Add)));

        view.Find("[data-testid='file-baseline-commit']").Click();

        Assert.Equal("commit", Assert.Single(picked));
        Assert.Equal("true", view.Find("[data-testid='file-baseline-commit']").GetAttribute("aria-pressed"));

        // And the comparison on screen is against that version, not the first.
        Assert.Equal(
            "domain.md, Last commit to Now",
            view.Find(".file-view__body").GetAttribute("aria-label"));
    }

    [Fact]
    public void A_baseline_named_by_the_host_is_what_is_compared_against()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.CompareBaselines, Two)
            .Add(v => v.Comparing, true)
            .Add(v => v.CompareBaselineId, "commit"));

        Assert.Equal("true", view.Find("[data-testid='file-baseline-commit']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void A_baseline_with_nothing_behind_it_says_so_rather_than_calling_every_line_new()
    {
        // "There is no committed version of this file yet" is an ordinary answer
        // and it is not the same answer as an empty file.
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.CompareBaselines, new FileCompareBaseline[]
            {
                new("commit", "Last commit", Unavailable: "This chapter has never been committed.")
            })
            .Add(v => v.Comparing, true));

        var empty = view.Find("[data-testid='file-compare-unavailable']");

        Assert.Equal("Nothing to compare against", empty.QuerySelector(".empty-state__title")!.TextContent);
        Assert.Equal("This chapter has never been committed.", empty.QuerySelector(".empty-state__body")!.TextContent);
        Assert.Empty(view.FindAll("[data-testid='file-compare-view']"));
        Assert.Empty(view.FindAll(".md-compare-block"));
    }

    [Fact]
    public void A_code_file_is_not_offered_a_comparison_this_component_cannot_make()
    {
        // The comparison aligns two documents by their headings, which is what
        // makes a diff of a chapter readable and a diff of a C# file meaningless.
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "Program.cs")
            .Add(v => v.Body, "var app = builder.Build();")
            .Add(v => v.TestId, "file")
            .Add(v => v.CompareBaselines, Opened));

        Assert.Empty(view.FindAll("[data-testid='file-compare']"));
        Assert.Empty(view.FindAll(".file-view__actions"));
    }

    [Fact]
    public void A_file_asked_to_compare_anyway_still_shows_the_file()
    {
        // The state is the host's to set, and a host may set it for a file that
        // has nothing to compare. Nothing is claimed that cannot be backed up.
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "Program.cs")
            .Add(v => v.Body, "var app = builder.Build();")
            .Add(v => v.TestId, "file")
            .Add(v => v.CompareBaselines, Opened)
            .Add(v => v.Comparing, true));

        Assert.Empty(view.FindAll("[data-testid='file-compare-view']"));
        Assert.NotNull(view.Find(".file-view__body .code-view"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_file_with_no_text_is_not_offered_a_comparison_either(string? body)
    {
        using var context = new BunitContext();

        var view = context.Render<FileView>(parameters => parameters
            .Add(v => v.Name, "domain.md")
            .Add(v => v.Body, body)
            .Add(v => v.TestId, "file")
            .Add(v => v.CompareBaselines, Opened));

        Assert.Empty(view.FindAll("[data-testid='file-compare']"));
    }

    [Fact]
    public void The_comparison_is_drawn_Bare_so_the_pane_keeps_one_frame_and_one_region()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.CompareBaselines, Opened)
            .Add(v => v.Comparing, true));

        Assert.NotNull(view.Find(".file-view__body .md-compare__bare"));
        Assert.Empty(view.FindAll(".md-compare__header"));
        Assert.Empty(view.FindAll(".md-compare__body"));

        // One tab stop, which is the pane's own body: a comparison with its own
        // scroll region inside this one would be a second stop the reader has to
        // walk through.
        Assert.Single(view.FindAll("[tabindex='0']"));
    }

    [Fact]
    public void Comparing_keeps_the_regions_tab_stop_even_where_a_host_supplied_a_body()
    {
        // The tabindex follows what is being drawn and not what was handed over:
        // while comparing, the body is this component's own reading of the file,
        // and that one scrolls with nothing focusable in it.
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.BodyContent, Editor())
            .Add(v => v.CompareBaselines, Opened));

        Assert.Null(view.Find(".file-view__body").GetAttribute("tabindex"));

        view.Find("[data-testid='file-compare']").Click();

        Assert.Equal("0", view.Find(".file-view__body").GetAttribute("tabindex"));
        Assert.Empty(view.FindAll("[data-testid='supplied-editor']"));
    }

    [Fact]
    public void Editing_gives_up_the_tab_stop_the_way_any_supplied_body_does()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.CanEdit, true)
            .Add(v => v.EditBodyContent, Editor()));

        Assert.Equal("0", view.Find(".file-view__body").GetAttribute("tabindex"));

        view.Find("[data-testid='file-edit']").Click();

        Assert.Null(view.Find(".file-view__body").GetAttribute("tabindex"));
    }

    [Fact]
    public void The_scroll_region_is_named_for_the_relationship_while_it_shows_one()
    {
        // Two regions called "domain.md" in one app leave a screen-reader user no
        // way to tell the pane from the comparison, and the difference between
        // them is precisely what comparing is for.
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.CompareBaselines, Opened)
            .Add(v => v.AfterLabel, "Working copy"));

        Assert.Equal("domain.md", view.Find(".file-view__body").GetAttribute("aria-label"));

        view.Find("[data-testid='file-compare']").Click();

        Assert.Equal("domain.md, As opened to Working copy", view.Find(".file-view__body").GetAttribute("aria-label"));

        view.Find("[data-testid='file-compare']").Click();

        Assert.Equal("domain.md", view.Find(".file-view__body").GetAttribute("aria-label"));
    }

    [Fact]
    public void The_comparison_is_of_the_baseline_against_the_file_on_screen()
    {
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.CompareBaselines, Opened)
            .Add(v => v.Comparing, true));

        var changed = view.Find(".md-compare-block--changed");

        Assert.Contains("What the domain was.", changed.TextContent, StringComparison.Ordinal);
        Assert.Contains("What the domain is.", changed.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_header_is_the_same_header_whichever_mode_the_body_is_in()
    {
        // The identity is what a reader trusts the content against, and which
        // mode the pane is in does not change which file it is.
        using var context = new BunitContext();

        var view = Render(context, parameters => parameters
            .Add(v => v.Path, @".domain\backlog\domain.md")
            .Add(v => v.Source, "Repository")
            .Add(v => v.CompareBaselines, Opened));

        view.Find("[data-testid='file-compare']").Click();

        Assert.Equal("domain.md", view.Find(".file-view__name").TextContent);
        Assert.Equal(@".domain\backlog\domain.md", view.Find(".file-view__path").TextContent);
        Assert.Equal("Repository", view.Find(".file-view__meta").TextContent);
    }
}
