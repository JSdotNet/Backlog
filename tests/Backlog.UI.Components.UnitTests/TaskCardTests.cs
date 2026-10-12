namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The board's card: what it states, in what order, and the one gesture it is.
/// <para>
/// A file of its own rather than more of <c>TaskListTests</c>, because the card is
/// a second view of the same <see cref="TaskRow"/>; the row's tests are about a
/// line, these are about the stack the Board draws in a column.
/// </para>
/// </summary>
public sealed class TaskCardTests
{
    private static readonly TaskRow Card = new(
        "card",
        "Extract TaskCard from the TaskItem row",
        Due: "Due Oct 8",
        StepsDone: 3,
        StepCount: 5,
        Tags: ["+task-views"],
        Kind: new TaskKind("📋", "Task"));

    private static IRenderedComponent<TaskCard> Render(
        BunitContext context,
        TaskRow task,
        Action<ComponentParameterCollectionBuilder<TaskCard>>? configure = null) =>
        context.Render<TaskCard>(parameters =>
        {
            parameters.Add(c => c.Task, task).Add(c => c.TestId, "card");
            configure?.Invoke(parameters);
        });

    [Fact]
    public void The_card_stacks_type_title_tags_and_a_bottom_line_in_that_order()
    {
        using var context = new BunitContext();

        var card = Render(context, Card, p => p
            .Add(c => c.Repository, "backlog")
            .Add(c => c.Effort, 5));

        var article = card.Find("article.task-card");
        var parts = article.Children.Select(child => child.ClassName).ToArray();

        Assert.Equal(["task-card__top", "task-card__title", "task-card__tags", "task-card__meta"], parts);
        Assert.Equal("Task", card.Find("[data-testid='card-kind']").TextContent);
        Assert.Equal("Extract TaskCard from the TaskItem row", card.Find("[data-testid='card-title']").TextContent);
        Assert.Contains("task-views", card.Find(".task-card__tags").TextContent);

        var meta = card.Find(".task-card__meta").Children.Select(child => child.GetAttribute("data-testid")).ToArray();
        Assert.Equal(["card-repository", "card-steps", "card-date", "card-effort"], meta);
        Assert.Contains("backlog", card.Find("[data-testid='card-repository']").TextContent);
        Assert.Contains("3/5", card.Find("[data-testid='card-steps']").TextContent);
        Assert.Contains("Due Oct 8", card.Find("[data-testid='card-date']").TextContent);
        Assert.Equal("5 pts", card.Find("[data-testid='card-effort']").TextContent);
    }

    [Theory]
    [InlineData("High", "High priority")]
    [InlineData("Critical", "Critical priority")]
    [InlineData("critical", "Critical priority")]
    public void High_and_critical_priority_draw_an_up_chevron_named_by_the_priority(string priority, string label)
    {
        using var context = new BunitContext();

        var mark = Render(context, Card, p => p.Add(c => c.Priority, priority))
            .Find("[data-testid='card-priority']");

        Assert.Equal(label, mark.GetAttribute("aria-label"));
        Assert.Equal(label, mark.GetAttribute("title"));
        Assert.Equal("img", mark.GetAttribute("role"));
        Assert.NotNull(mark.QuerySelector("svg.chevron-up-icon"));
    }

    [Theory]
    [InlineData("Medium")]
    [InlineData("Low")]
    [InlineData(null)]
    [InlineData("")]
    public void Medium_low_and_no_priority_stay_quiet(string? priority)
    {
        using var context = new BunitContext();

        var card = Render(context, Card, p => p.Add(c => c.Priority, priority));

        Assert.Empty(card.FindAll("[data-testid='card-priority']"));
        Assert.Empty(card.FindAll(".chevron-up-icon"));
    }

    [Fact]
    public void A_blocked_card_names_what_it_waits_on()
    {
        using var context = new BunitContext();
        var status = new TaskChainStatus(
            "card",
            TaskReadiness.Blocked,
            [new TaskDependency("board", "Board view", InList: true), new TaskDependency("ghost", null)],
            InCycle: false);

        var card = Render(context, Card, p => p.Add(c => c.Status, status));

        var notice = card.Find("[data-testid='card-waiting']");
        Assert.Equal("Waits on Board view, ghost", notice.TextContent.Replace("⏳", string.Empty).Trim());
        Assert.Contains("task-card--blocked", card.Find("article").ClassName);
    }

    [Fact]
    public void A_ready_card_has_no_waiting_notice()
    {
        using var context = new BunitContext();
        var status = new TaskChainStatus("card", TaskReadiness.Ready, [], InCycle: false);

        var card = Render(context, Card, p => p.Add(c => c.Status, status));

        Assert.Empty(card.FindAll("[data-testid='card-waiting']"));
        Assert.DoesNotContain("task-card--blocked", card.Find("article").ClassName);
    }

    [Fact]
    public void An_overdue_open_card_paints_its_due_date_in_the_error_ink()
    {
        using var context = new BunitContext();

        var date = Render(context, Card with { Due = "Overdue 5 days" }, p => p.Add(c => c.Overdue, true))
            .Find("[data-testid='card-date']");

        Assert.Contains("task-card__date--overdue", date.ClassName);
        Assert.Equal("Overdue", date.GetAttribute("title"));
        Assert.Contains("Overdue 5 days", date.TextContent);
    }

    [Fact]
    public void A_done_card_shows_when_it_was_finished_and_not_its_deadline()
    {
        using var context = new BunitContext();

        var card = Render(context, Card with { Done = true }, p => p
            .Add(c => c.Finished, "Finished Oct 5")
            .Add(c => c.Overdue, true));

        var date = card.Find("[data-testid='card-date']");
        Assert.Contains("task-card__date--finished", date.ClassName);
        Assert.DoesNotContain("task-card__date--overdue", date.ClassName);
        Assert.Contains("Finished Oct 5", date.TextContent);
        Assert.DoesNotContain("Due Oct 8", card.Markup);
        Assert.Contains("task-card--done", card.Find("article").ClassName);
    }

    [Fact]
    public void A_done_card_with_no_finished_date_draws_no_date()
    {
        using var context = new BunitContext();

        var card = Render(context, Card with { Done = true });

        Assert.Empty(card.FindAll("[data-testid='card-date']"));
    }

    [Theory]
    [InlineData(null, "Not estimated")]
    [InlineData(1, "1 pt")]
    [InlineData(8, "8 pts")]
    public void The_effort_is_said_on_the_far_end_of_the_bottom_line(int? effort, string text)
    {
        using var context = new BunitContext();

        var meta = Render(context, Card, p => p.Add(c => c.Effort, effort)).Find(".task-card__meta");

        Assert.Equal(text, meta.LastElementChild!.TextContent);
        Assert.Contains("task-card__effort", meta.LastElementChild.ClassName);
    }

    [Fact]
    public void The_identity_edge_is_the_class_the_host_hands_in()
    {
        using var context = new BunitContext();

        var shown = Render(context, Card, p => p.Add(c => c.CssClass, "repo-mark repo-mark--2")).Find("article");
        Assert.Contains("repo-mark", shown.ClassList);
        Assert.Contains("repo-mark--2", shown.ClassList);

        // Colours off: the host hands nothing, and the card draws no edge of its own.
        var hidden = Render(context, Card, p => p.Add(c => c.CssClass, null)).Find("article");
        Assert.DoesNotContain("repo-mark", hidden.ClassList);
    }

    [Fact]
    public void The_whole_card_is_one_focusable_control_that_opens_the_task()
    {
        using var context = new BunitContext();
        var opened = new List<string>();

        var card = Render(context, Card, p => p.Add(c => c.OnSelected, (string id) => opened.Add(id)));
        var article = card.Find("article");

        Assert.Equal("0", article.GetAttribute("tabindex"));
        Assert.Equal(Card.Title, article.GetAttribute("aria-label"));

        article.Click();
        card.Find("article").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        card.Find("article").KeyDown(new KeyboardEventArgs { Key = " " });
        card.Find("article").KeyDown(new KeyboardEventArgs { Key = "a" });

        Assert.Equal(["card", "card", "card"], opened);
    }

    /// <summary>
    /// Space opens the card and must not also scroll the column it sits in
    /// (#1039). The card is an article, not a button, so the browser's own
    /// default for Space is a page scroll; components.js refuses it at the keydown,
    /// on the focused card only, and lets the event bubble to the card's handler.
    /// Not a state-bound <c>:preventDefault</c>, which would apply to the key after
    /// the one that set it — so the card's markup carries none.
    /// </summary>
    [Fact]
    public void Space_on_a_focused_card_is_refused_its_scroll_in_components_js_not_by_a_bound_flag()
    {
        using var context = new BunitContext();

        var card = Render(context, Card, p => p.Add(c => c.OnSelected, (string _) => { }));
        card.Find("article").KeyDown(new KeyboardEventArgs { Key = " " });

        Assert.Null(card.Find("article").GetAttribute("blazor:onkeydown:preventdefault"));

        var script = File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.js"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        var start = script.IndexOf("// Space on a focused TaskCard.", StringComparison.Ordinal);
        Assert.True(start >= 0, "components.js no longer refuses Space on a focused card.");

        var listener = script[start..script.IndexOf("\n    });", start, StringComparison.Ordinal)];

        Assert.Contains("event.key !== ' '", listener, StringComparison.Ordinal);
        Assert.Contains("target.matches('.task-card[tabindex=\"0\"]')", listener, StringComparison.Ordinal);
        Assert.Contains("event.preventDefault()", listener, StringComparison.Ordinal);
        Assert.DoesNotContain("stopPropagation", listener, StringComparison.Ordinal);
    }

    [Fact]
    public void Nobody_listening_leaves_the_card_out_of_the_tab_order()
    {
        using var context = new BunitContext();

        var article = Render(context, Card).Find("article");

        Assert.Null(article.GetAttribute("tabindex"));
        Assert.DoesNotContain("task-card--opens", article.ClassList);
    }

    [Fact]
    public void Presses_inside_the_badge_slot_never_open_the_card()
    {
        using var context = new BunitContext();
        var opened = 0;
        var followed = 0;

        var card = Render(context, Card, p => p
            .Add(c => c.OnSelected, (string _) => opened++)
            .Add(c => c.Badges, (TaskRow _) => builder =>
            {
                builder.OpenElement(0, "button");
                builder.AddAttribute(1, "type", "button");
                builder.AddAttribute(2, "class", "work-badge");
                builder.AddAttribute(3, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, () => followed++));
                builder.AddAttribute(4, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, () => { }));
                builder.AddAttribute(5, "onmousedown", EventCallback.Factory.Create<MouseEventArgs>(this, () => { }));
                builder.AddContent(6, "#1019");
                builder.CloseElement();
            }));

        card.Find(".work-badge").Click();
        card.Find(".work-badge").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        card.Find(".work-badge").MouseDown();

        Assert.Equal(1, followed);
        Assert.Equal(0, opened);

        // The control: the same press on the card itself does open it, so the
        // zero above is the slot stopping the event and not a card that ignores it.
        card.Find(".task-card__title").Click();
        Assert.Equal(1, opened);
    }

    [Fact]
    public void A_pressable_tag_filters_without_opening_the_card()
    {
        using var context = new BunitContext();
        var opened = 0;
        var filtered = new List<string>();

        var card = Render(context, Card, p => p
            .Add(c => c.OnSelected, (string _) => opened++)
            .Add(c => c.OnTagSelected, (string tag) => filtered.Add(tag)));

        card.Find(".task-card__tags button").Click();

        Assert.Equal(["+task-views"], filtered);
        Assert.Equal(0, opened);
    }

    [Fact]
    public void A_linked_task_leads_the_badge_slot_with_its_source()
    {
        using var context = new BunitContext();

        var card = Render(context, Card with { Source = new TaskSource("#412", "https://example.com/412", "GitHub") });

        Assert.NotNull(card.Find("[data-testid='card-badges'] [data-testid='card-source']"));
    }

    [Fact]
    public void A_card_with_nothing_for_the_slot_draws_no_slot()
    {
        using var context = new BunitContext();

        Assert.Empty(Render(context, Card).FindAll("[data-testid='card-badges']"));
    }

    [Fact]
    public void My_day_reads_on_the_top_line()
    {
        using var context = new BunitContext();

        var top = Render(context, Card with { InMyDay = true }).Find(".task-card__top");

        Assert.Contains("My Day", top.QuerySelector("[data-testid='card-my-day']")!.TextContent);
        Assert.NotNull(top.QuerySelector(".sun-icon"));
    }

    private static BunitContext CopyContext()
    {
        var context = new BunitContext();
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);
        return context;
    }

    private static string Copied(BunitContext context) =>
        (string)Assert.Single(context.JSInterop.Invocations["backlogClipboard.copy"]).Arguments[0]!;

    /// <summary>The row's copy button, on the card's top line, named for the task
    /// it copies — and only when the host turns it on: a card standing alone, the
    /// In progress view's, is a summary rather than something to paste.</summary>
    [Fact]
    public void The_copy_button_sits_on_the_top_line_named_for_the_task_and_only_when_allowed()
    {
        using var context = CopyContext();

        Assert.Empty(Render(context, Card).FindAll("[data-testid='card-copy']"));

        var card = Render(context, Card, p => p.Add(c => c.AllowCopy, true));
        var button = card.Find(".task-card__top [data-testid='card-copy']");

        Assert.Equal("Copy Extract TaskCard from the TaskItem row", button.GetAttribute("aria-label"));
        Assert.NotNull(button.Closest(".task-card__copy"));
    }

    /// <summary>Unset, a card copies what a row would: the title, and the body
    /// under it after a blank line when there is one. Set, the host's text wins.</summary>
    [Fact]
    public void A_card_copies_the_rows_text_unless_the_host_says_what_it_copies()
    {
        using (var context = CopyContext())
        {
            Render(context, Card with { Body = "  The card is the row, stacked.  " }, p => p.Add(c => c.AllowCopy, true))
                .Find("[data-testid='card-copy']").Click();

            Assert.Equal("Extract TaskCard from the TaskItem row\n\nThe card is the row, stacked.", Copied(context));
        }

        using (var context = CopyContext())
        {
            Render(context, Card, p => p.Add(c => c.AllowCopy, true).Add(c => c.CopyValue, "the host's brief"))
                .Find("[data-testid='card-copy']").Click();

            Assert.Equal("the host's brief", Copied(context));
        }
    }

    /// <summary>The badges' rule: a press, a click or a key on the copy button is
    /// the button's, and never opens the card under it.</summary>
    [Fact]
    public void Copying_a_card_never_opens_it()
    {
        using var context = CopyContext();
        var opened = 0;

        var card = Render(context, Card, p => p
            .Add(c => c.AllowCopy, true)
            .Add(c => c.OnSelected, (string _) => opened++));

        card.Find("[data-testid='card-copy']").Click();

        Assert.Single(context.JSInterop.Invocations["backlogClipboard.copy"]);
        Assert.Equal(0, opened);

        // The control: the card itself still opens, so the zero is the slot.
        card.Find(".task-card__title").Click();
        Assert.Equal(1, opened);
    }

    /// <summary>A card marked blocked refuses the copy the way its row does: the
    /// button stays, disabled, and says why.</summary>
    [Fact]
    public void A_card_marked_blocked_refuses_the_copy_and_says_why()
    {
        using var context = CopyContext();

        var button = Render(context, Card with { MarkedBlocked = true }, p => p.Add(c => c.AllowCopy, true))
            .Find("[data-testid='card-copy']");

        Assert.True(button.HasAttribute("disabled"));
        Assert.Equal("Blocked — unblock to copy", button.GetAttribute("title"));
    }

    /// <summary>taskListDrag never picks a card up from its copy button.</summary>
    [Fact]
    public void The_drag_script_leaves_a_press_on_the_copy_button_to_the_button()
    {
        var script = File.ReadAllText(RepositoryRoot.File(
            "src", "Core", "Backlog.UI.Components", "wwwroot", "components.js"));
        var excluded = script[script.IndexOf("const TASK_DRAG_EXCLUDED", StringComparison.Ordinal)..];
        excluded = excluded[..excluded.IndexOf(';')];

        Assert.Contains("'.task-card__copy,'", excluded, StringComparison.Ordinal);
    }
}
