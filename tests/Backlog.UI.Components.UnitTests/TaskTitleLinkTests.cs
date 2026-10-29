namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A web address in a task title, on the three surfaces that draw one: the row,
/// the board card and the open task's panel. The address is link-styled text in the
/// title that a pointer can follow without opening or renaming the task, and a real
/// anchor beside the title for everyone else.
/// <para>
/// A file of its own rather than more of <c>TaskListTests</c>, <c>TaskCardTests</c>
/// and <c>TaskPanelTests</c>: the rule is one rule drawn three times, and keeping its
/// proofs together is what says so.
/// </para>
/// </summary>
public sealed class TaskTitleLinkTests
{
    private const string Url = "https://claude.ai/artifact/UkBhdNFEcb95CNmbrJbzWr";
    private const string Title = "Concurrency issue: " + Url;

    private static BunitContext LooseContext()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    private static IRenderedComponent<TaskItem> Row(
        BunitContext context,
        string title,
        Action<ComponentParameterCollectionBuilder<TaskItem>>? configure = null) =>
        context.Render<TaskItem>(p =>
        {
            p.Add(t => t.Task, new TaskRow("a", title)).Add(t => t.TestId, "row");
            configure?.Invoke(p);
        });

    // --- The row -------------------------------------------------------------

    [Fact]
    public void A_row_offers_one_followable_link_outside_its_open_button()
    {
        using var context = LooseContext();

        var row = Row(context, Title);

        var anchor = Assert.Single(row.FindAll("a[href]"));

        Assert.Equal(Url, anchor.GetAttribute("href"));
        Assert.Equal("_blank", anchor.GetAttribute("target"));
        Assert.Contains("noopener", anchor.GetAttribute("rel"), StringComparison.Ordinal);
        Assert.Null(anchor.Closest("[data-testid='row-open']"));

        // In the slot that stops the press and the click, so following it is not
        // opening the row and not the start of a drag.
        Assert.NotNull(anchor.Closest(".task-item__badges"));
    }

    [Fact]
    public void The_link_beside_the_title_is_labelled_by_its_host_and_named_by_its_address()
    {
        using var context = LooseContext();

        var anchor = Row(context, Title).Find("[data-testid='row-title-link']");

        Assert.Equal("claude.ai ↗", anchor.TextContent);
        Assert.Equal($"Open {Url}", anchor.GetAttribute("aria-label"));
    }

    [Fact]
    public void The_title_reads_verbatim_with_its_address_as_an_unfocusable_run_of_its_own()
    {
        using var context = LooseContext();

        var row = Row(context, Title);

        Assert.Equal(Title, row.Find("[data-testid='row-title']").TextContent);

        var run = row.Find("[data-testid='row-title'] .task-title-url");
        Assert.Equal(Url, run.TextContent);
        Assert.Equal(Url, run.GetAttribute("data-title-url"));
        Assert.Equal("span", run.LocalName);
        Assert.False(run.HasAttribute("tabindex"));
        Assert.False(run.HasAttribute("href"));

        // The open button's name is its content, and the content still carries the
        // whole title after the word that says what the button does.
        Assert.Contains(Title, row.Find("[data-testid='row-open']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Clicking_the_address_in_the_title_neither_opens_nor_renames_the_row()
    {
        using var context = LooseContext();
        var opened = 0;
        var renamed = 0;

        var row = Row(context, Title, p => p
            .Add(t => t.OnSelected, (string _) => opened++)
            .Add(t => t.OnRename, (TaskRename _) => renamed++));

        ClickAddress(row.Find("[data-testid='row-title'] .task-title-url"));

        Assert.Equal(0, opened);
        Assert.Equal(0, renamed);
        Assert.Empty(row.FindAll("[data-testid='row-rename']"));

        // The control: the prose of the same title still opens the row, so the zero
        // above is the address keeping its click and not a row that ignores them.
        row.Find("[data-testid='row-open']").Click();
        Assert.Equal(1, opened);
    }

    [Fact]
    public void The_rename_field_and_every_label_keep_the_raw_title()
    {
        using var context = LooseContext();

        var row = Row(context, Title, p => p.Add(t => t.OnRename, (TaskRename _) => { }));

        Assert.Equal($"Rename {Title}", row.Find("[data-testid='row-edit']").GetAttribute("aria-label"));

        row.Find("[data-testid='row-edit']").Click();

        Assert.Equal(Title, row.Find("[data-testid='row-rename']").GetAttribute("value"));
    }

    [Fact]
    public void A_row_copies_its_title_with_the_address_in_it()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        Row(context, Title).Find("[data-testid='row-copy']").Click();

        Assert.Equal(Title, Assert.Single(context.JSInterop.Invocations["backlogClipboard.copy"]).Arguments[0]);
    }

    [Fact]
    public void Tags_around_an_address_are_chips_and_a_hash_inside_it_is_not()
    {
        using var context = LooseContext();

        var row = Row(context, "Fix #deploy https://x.com/a#frag @bob");

        var chips = row.FindAll("[data-testid='row-title'] .task-item__title-tag").Select(c => c.TextContent.Trim());

        Assert.Equal(["#deploy", "@bob"], chips);
        Assert.Equal("https://x.com/a#frag", row.Find("[data-testid='row-title'] .task-title-url").TextContent);
    }

    [Fact]
    public void Two_distinct_addresses_offer_two_links_in_title_order_and_a_repeat_offers_one()
    {
        using var context = LooseContext();

        var two = Row(context, "Compare https://b.example/y with https://a.example/x")
            .FindAll("[data-testid='row-title-link']")
            .Select(a => a.GetAttribute("href"));
        var repeated = Row(context, "https://a.example/x then https://a.example/x again")
            .FindAll("[data-testid='row-title-link']");

        Assert.Equal(["https://b.example/y", "https://a.example/x"], two);
        Assert.Single(repeated);
    }

    [Theory]
    [InlineData("Run javascript:alert(1) now")]
    [InlineData("Embed data:text/html;base64,PGI+ now")]
    [InlineData("Fetch ftp://files.example.com/a now")]
    [InlineData("Mail mailto:bob@example.com now")]
    [InlineData("Visit www.example.com now")]
    public void Only_http_and_https_become_links(string title)
    {
        using var context = LooseContext();

        var row = Row(context, title);

        Assert.Empty(row.FindAll("a"));
        Assert.Empty(row.FindAll(".task-title-url"));
        Assert.Equal(title, row.Find("[data-testid='row-title']").TextContent);
    }

    [Fact]
    public void Punctuation_and_brackets_around_an_address_stay_out_of_its_link()
    {
        using var context = LooseContext();

        var period = Row(context, "see https://example.com/a.").Find("[data-testid='row-title-link']");
        var bracketed = Row(context, "(https://example.com/a)").Find("[data-testid='row-title-link']");

        Assert.Equal("https://example.com/a", period.GetAttribute("href"));
        Assert.Equal("https://example.com/a", bracketed.GetAttribute("href"));
    }

    [Fact]
    public void A_title_with_no_address_draws_no_link_and_no_empty_slot()
    {
        using var context = LooseContext();

        var row = Row(context, "Wire up the store #deploy");

        Assert.Empty(row.FindAll("a"));
        Assert.Empty(row.FindAll(".task-title-url"));
        Assert.Empty(row.FindAll(".task-item__badges"));
    }

    [Fact]
    public void A_linked_row_keeps_its_source_first_and_adds_the_title_link_after_it()
    {
        using var context = LooseContext();

        var row = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", Title, Source: new TaskSource("#412", "https://example.com/412", "GitHub")))
            .Add(t => t.TestId, "row"));

        var slot = row.Find(".task-item__badges");
        var links = slot.QuerySelectorAll("a[href]").Select(a => a.GetAttribute("data-testid")).ToList();

        Assert.Equal(["row-source", "row-title-link"], links);
    }

    [Fact]
    public void The_drag_script_leaves_a_press_on_the_address_in_a_title_to_the_address()
    {
        var script = File.ReadAllText(RepositoryRoot.File(
            "src", "Core", "Backlog.UI.Components", "wwwroot", "components.js"));
        var excluded = script[script.IndexOf("const TASK_DRAG_EXCLUDED", StringComparison.Ordinal)..];
        excluded = excluded[..excluded.IndexOf(';')];

        Assert.Contains("'[data-title-url],'", excluded, StringComparison.Ordinal);
        Assert.Contains(".task-item__badges", excluded, StringComparison.Ordinal);
    }

    [Fact]
    public void The_script_opens_the_address_inside_the_click_and_only_a_web_address()
    {
        // Opened by a native click listener rather than from .NET: a window opened
        // after a circuit round trip can fall outside the click's user activation
        // and be dropped by the popup blocker.
        var script = File.ReadAllText(RepositoryRoot.File(
            "src", "Core", "Backlog.UI.Components", "wwwroot", "components.js"));
        var at = script.IndexOf(".closest('[data-title-url]')", StringComparison.Ordinal);
        Assert.True(at > 0, "No click listener reads data-title-url.");

        var listenerStart = script.LastIndexOf("document.addEventListener(", at, StringComparison.Ordinal);
        var listener = script[listenerStart..script.IndexOf("});", at, StringComparison.Ordinal)];

        Assert.StartsWith("document.addEventListener('click'", listener, StringComparison.Ordinal);
        Assert.Contains("window.open(url, '_blank', 'noopener,noreferrer')", listener, StringComparison.Ordinal);
        Assert.Contains("https?:", listener, StringComparison.Ordinal);
    }

    /// <summary>
    /// Clicks an address in a title. The span has no Blazor handler of its own —
    /// opening it is components.js's — and its <c>stopPropagation</c> ends the walk
    /// that would find one on an ancestor, so bUnit may report that nothing handled
    /// the click. That is the outcome under test; anything else it throws is not.
    /// </summary>
    private static void ClickAddress(AngleSharp.Dom.IElement span)
    {
        var thrown = Record.Exception(() => span.Click());

        Assert.True(thrown is null or MissingEventHandlerException, thrown?.ToString());
    }

    // --- The board card ------------------------------------------------------

    [Fact]
    public void A_card_offers_the_link_in_its_badge_slot_and_following_it_does_not_open_the_card()
    {
        using var context = LooseContext();
        var opened = 0;

        var card = context.Render<TaskCard>(p => p
            .Add(c => c.Task, new TaskRow("card", Title))
            .Add(c => c.OnSelected, (string _) => opened++)
            .Add(c => c.TestId, "card"));

        var anchor = Assert.Single(card.FindAll("a[href]"));
        Assert.Equal(Url, anchor.GetAttribute("href"));
        Assert.Equal("_blank", anchor.GetAttribute("target"));
        Assert.NotNull(anchor.Closest("[data-testid='card-badges']"));
        Assert.Equal(Title, card.Find("[data-testid='card-title']").TextContent);

        var address = card.Find("[data-testid='card-title'] .task-title-url");
        Assert.Equal(Url, address.GetAttribute("data-title-url"));

        ClickAddress(address);

        Assert.Equal(0, opened);

        // The control: the card still opens from its title's prose.
        card.Find("[data-testid='card-title']").Click();
        Assert.Equal(1, opened);
    }

    [Fact]
    public void A_card_with_no_address_draws_no_link_and_no_slot()
    {
        using var context = LooseContext();

        var card = context.Render<TaskCard>(p => p
            .Add(c => c.Task, new TaskRow("card", "Extract TaskCard from the row"))
            .Add(c => c.TestId, "card"));

        Assert.Empty(card.FindAll("a"));
        Assert.Empty(card.FindAll("[data-testid='card-badges']"));
    }

    // --- The panel heading ---------------------------------------------------

    [Fact]
    public void The_panel_offers_the_link_on_its_heading_line_and_the_address_does_not_start_a_rename()
    {
        using var context = LooseContext();
        var renamed = 0;

        var panel = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, Title)
            .Add(t => t.OnRename, (string _) => renamed++)
            .Add(t => t.TestId, "panel"));

        var anchor = panel.Find("[data-testid='panel-title-link']");
        Assert.Equal(Url, anchor.GetAttribute("href"));
        Assert.Equal("_blank", anchor.GetAttribute("target"));
        Assert.Null(anchor.Closest("[data-testid='panel-title']"));
        Assert.NotNull(anchor.Closest(".task-panel__header"));
        Assert.Equal(Title, panel.Find("[data-testid='panel-title']").TextContent);

        var address = panel.Find("[data-testid='panel-title'] .task-title-url");
        Assert.Equal(Url, address.GetAttribute("data-title-url"));

        ClickAddress(address);

        Assert.Empty(panel.FindAll("[data-testid='panel-rename']"));

        // The control: the title's prose still starts the rename, seeded with the
        // whole title, address and all.
        panel.Find("[data-testid='panel-title']").Click();
        Assert.Equal(Title, panel.Find("[data-testid='panel-rename']").GetAttribute("value"));
        Assert.Equal(0, renamed);
    }

    [Fact]
    public void A_read_only_panel_still_offers_the_link_and_still_marks_the_address()
    {
        using var context = LooseContext();

        var panel = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, Title)
            .Add(t => t.TestId, "panel"));

        Assert.NotNull(panel.Find("[data-testid='panel-title-link']"));
        Assert.Equal(Url, panel.Find(".task-panel__title .task-title-url").GetAttribute("data-title-url"));
    }

    [Fact]
    public void A_panel_with_no_address_draws_no_link()
    {
        using var context = LooseContext();

        var panel = context.Render<TaskPanel>(p => p
            .Add(t => t.Title, "Wire the pane into the shell")
            .Add(t => t.TestId, "panel"));

        Assert.Empty(panel.FindAll("[data-testid='panel-title-link']"));
        Assert.Empty(panel.FindAll(".task-title-url"));
        Assert.Empty(panel.FindAll(".task-panel__links"));
    }
}
