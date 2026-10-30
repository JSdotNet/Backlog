namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The editable combobox with list autocomplete (ARIA 1.2): the wiring a screen reader
/// reads, each key the pattern names, and the three ways a value is committed — a pick,
/// Enter, and a native change — each reported once.
/// </summary>
public sealed class ComboboxTests
{
    private static readonly SelectorOption[] Options =
    [
        new("0.5", "0.5h"),
        new("1", "1h"),
        new("2", "2h")
    ];

    private sealed class Host
    {
        public List<string> Values { get; } = [];

        public List<(string Value, ComboboxCommitReason Reason)> Commits { get; } = [];

        public Func<ComboboxCommit, bool> Refuse { get; set; } = _ => false;
    }

    private static IRenderedComponent<Combobox> Render(BunitContext context, Host host, string? value = null) =>
        context.Render<Combobox>(parameters => parameters
            .Add(c => c.Options, Options)
            .Add(c => c.AriaLabel, "Hours")
            .Add(c => c.TestId, "combo")
            .Add(c => c.Value, value)
            .Add(c => c.ValueChanged, (string text) => host.Values.Add(text))
            .Add(c => c.OnCommit, (ComboboxCommit commit) =>
            {
                host.Commits.Add((commit.Value, commit.Reason));
                if (host.Refuse(commit)) commit.Refuse();
            }));

    private static void Key(IRenderedComponent<Combobox> combo, string key, bool alt = false) =>
        combo.Find("input").KeyDown(new KeyboardEventArgs { Key = key, AltKey = alt });

    private static string? Active(IRenderedComponent<Combobox> combo) =>
        combo.Find("input").GetAttribute("aria-activedescendant");

    private static string OptionId(IRenderedComponent<Combobox> combo, int index) =>
        combo.FindAll("[role='option']")[index].Id!;

    [Fact]
    public void The_input_is_a_combobox_controlling_a_listbox_of_options()
    {
        using var context = new BunitContext();
        var combo = Render(context, new Host());

        var input = combo.Find("input");
        Assert.Equal("combobox", input.GetAttribute("role"));
        Assert.Equal("list", input.GetAttribute("aria-autocomplete"));
        Assert.Equal("false", input.GetAttribute("aria-expanded"));
        Assert.Equal("Hours", input.GetAttribute("aria-label"));
        Assert.Equal("text", input.GetAttribute("type"));
        Assert.Empty(combo.FindAll("[role='listbox']"));

        Key(combo, "ArrowDown");

        input = combo.Find("input");
        Assert.Equal("true", input.GetAttribute("aria-expanded"));
        var list = combo.Find("[role='listbox']");
        Assert.Equal(input.GetAttribute("aria-controls"), list.Id);
        Assert.Equal(["0.5h", "1h", "2h"], combo.FindAll("[role='option']").Select(option => option.TextContent));
    }

    [Fact]
    public void Down_opens_on_the_first_option_and_then_walks_and_wraps()
    {
        using var context = new BunitContext();
        var combo = Render(context, new Host());

        Key(combo, "ArrowDown");
        Assert.Equal(OptionId(combo, 0), Active(combo));
        Assert.Equal("true", combo.FindAll("[role='option']")[0].GetAttribute("aria-selected"));

        Key(combo, "ArrowDown");
        Assert.Equal(OptionId(combo, 1), Active(combo));

        Key(combo, "ArrowDown");
        Key(combo, "ArrowDown");
        Assert.Equal(OptionId(combo, 0), Active(combo));
    }

    [Fact]
    public void Down_opens_on_the_option_the_field_already_holds()
    {
        using var context = new BunitContext();
        var combo = Render(context, new Host(), value: "2");

        Key(combo, "ArrowDown");

        Assert.Equal(OptionId(combo, 2), Active(combo));
    }

    [Fact]
    public void Alt_down_opens_the_list_with_no_option_active()
    {
        using var context = new BunitContext();
        var combo = Render(context, new Host());

        Key(combo, "ArrowDown", alt: true);

        Assert.Equal("true", combo.Find("input").GetAttribute("aria-expanded"));
        Assert.Null(Active(combo));
    }

    [Fact]
    public void Up_walks_back_and_wraps_to_the_last_option()
    {
        using var context = new BunitContext();
        var combo = Render(context, new Host());

        Key(combo, "ArrowDown");
        Key(combo, "ArrowUp");

        Assert.Equal(OptionId(combo, 2), Active(combo));

        Key(combo, "ArrowUp");
        Assert.Equal(OptionId(combo, 1), Active(combo));
    }

    [Fact]
    public void Enter_on_an_active_option_picks_it_and_closes_the_list()
    {
        using var context = new BunitContext();
        var host = new Host();
        var combo = Render(context, host);

        Key(combo, "ArrowDown");
        Key(combo, "ArrowDown");
        Key(combo, "Enter");

        Assert.Equal("1", combo.Find("input").GetAttribute("value"));
        Assert.Equal(["1"], host.Values);
        Assert.Equal([("1", ComboboxCommitReason.Picked)], host.Commits);
        Assert.Equal("false", combo.Find("input").GetAttribute("aria-expanded"));
        Assert.Empty(combo.FindAll("[role='listbox']"));
    }

    [Fact]
    public void A_click_on_an_option_picks_it()
    {
        using var context = new BunitContext();
        var host = new Host();
        var combo = Render(context, host);

        combo.Find("input").Click();
        combo.FindAll("[role='option']")[2].Click();

        Assert.Equal("2", combo.Find("input").GetAttribute("value"));
        Assert.Equal([("2", ComboboxCommitReason.Picked)], host.Commits);
    }

    /// <summary>A list opened by a click starts on the figure already in the field,
    /// as one opened by Down does; Down then walks on from it rather than back to
    /// the first suggestion.</summary>
    [Fact]
    public void A_click_opens_the_list_on_the_option_matching_the_field()
    {
        using var context = new BunitContext();
        var combo = Render(context, new Host(), value: "1");

        combo.Find("input").Click();

        Assert.Equal(OptionId(combo, 1), Active(combo));
        Assert.Equal("true", combo.FindAll("[role='option']")[1].GetAttribute("aria-selected"));

        Key(combo, "ArrowDown");

        Assert.Equal(OptionId(combo, 2), Active(combo));
    }

    [Fact]
    public void A_click_on_a_field_no_option_matches_highlights_nothing()
    {
        using var context = new BunitContext();
        var combo = Render(context, new Host(), value: "2.75");

        combo.Find("input").Click();

        Assert.Equal("true", combo.Find("input").GetAttribute("aria-expanded"));
        Assert.Null(Active(combo));
    }

    [Fact]
    public void Enter_with_no_option_active_commits_what_was_typed()
    {
        using var context = new BunitContext();
        var host = new Host();
        var combo = Render(context, host);

        combo.Find("input").Input("2.75");
        Key(combo, "Enter");

        Assert.Equal([("2.75", ComboboxCommitReason.Entered)], host.Commits);
    }

    /// <summary>Typing past a highlighted suggestion means the text, not the suggestion.</summary>
    [Fact]
    public void Typing_clears_the_active_option_so_enter_commits_the_text()
    {
        using var context = new BunitContext();
        var host = new Host();
        var combo = Render(context, host);

        Key(combo, "ArrowDown");
        combo.Find("input").Input("5");

        Assert.Null(Active(combo));

        Key(combo, "Enter");
        Assert.Equal([("5", ComboboxCommitReason.Entered)], host.Commits);
    }

    [Fact]
    public void Typing_filters_nothing()
    {
        using var context = new BunitContext();
        var combo = Render(context, new Host());

        combo.Find("input").Click();
        combo.Find("input").Input("7");

        Assert.Equal(3, combo.FindAll("[role='option']").Count);
        Assert.Equal("7", combo.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void Escape_closes_an_open_list_and_keeps_the_text()
    {
        using var context = new BunitContext();
        var host = new Host();
        var combo = Render(context, host, value: "3");

        Key(combo, "ArrowDown");
        Key(combo, "Escape");

        Assert.Equal("false", combo.Find("input").GetAttribute("aria-expanded"));
        Assert.Equal("3", combo.Find("input").GetAttribute("value"));
        Assert.Empty(host.Values);
        Assert.Empty(host.Commits);
    }

    [Fact]
    public void Escape_with_the_list_closed_clears_the_field()
    {
        using var context = new BunitContext();
        var host = new Host();
        var combo = Render(context, host, value: "3");

        Key(combo, "Escape");

        Assert.Equal(string.Empty, combo.Find("input").GetAttribute("value"));
        Assert.Equal([string.Empty], host.Values);
        Assert.Empty(host.Commits);
    }

    [Fact]
    public void Tab_closes_the_list_without_picking()
    {
        using var context = new BunitContext();
        var host = new Host();
        var combo = Render(context, host, value: "3");

        Key(combo, "ArrowDown");
        Key(combo, "Tab");

        Assert.Equal("false", combo.Find("input").GetAttribute("aria-expanded"));
        Assert.Equal("3", combo.Find("input").GetAttribute("value"));
        Assert.Empty(host.Commits);
    }

    [Fact]
    public void Leaving_the_field_closes_the_list()
    {
        using var context = new BunitContext();
        var combo = Render(context, new Host());

        Key(combo, "ArrowDown");
        combo.Find("input").Blur();

        Assert.Empty(combo.FindAll("[role='listbox']"));
    }

    [Fact]
    public void A_change_commits_once_and_the_change_after_enter_is_not_a_second_commit()
    {
        using var context = new BunitContext();
        var host = new Host();
        var combo = Render(context, host, value: "3");

        combo.Find("input").Change("4");
        Assert.Equal([("4", ComboboxCommitReason.Changed)], host.Commits);

        // Enter, then the change a browser raises for the same text.
        combo.Find("input").Input("5");
        Key(combo, "Enter");
        combo.Find("input").Change("5");

        Assert.Equal(
            [("4", ComboboxCommitReason.Changed), ("5", ComboboxCommitReason.Entered)],
            host.Commits);
    }

    [Fact]
    public void A_refused_commit_puts_the_hosts_value_back()
    {
        using var context = new BunitContext();
        var host = new Host { Refuse = _ => true };
        var combo = Render(context, host, value: "3");

        combo.Find("input").Input("lots");
        Assert.Equal("lots", combo.Find("input").GetAttribute("value"));

        combo.Find("input").Change("lots");

        Assert.Equal("3", combo.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void A_new_value_from_the_host_replaces_the_text()
    {
        using var context = new BunitContext();
        var combo = Render(context, new Host(), value: "3");

        combo.Render(parameters => parameters.Add(c => c.Value, "6"));

        Assert.Equal("6", combo.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void The_escape_that_closes_the_list_does_not_reach_the_host_and_the_next_one_does()
    {
        using var context = new BunitContext();
        var harness = context.Render<ComboboxEscapeHarness>();

        // The Down that opens the list met it closed, so the host hears it.
        harness.Find("[data-testid='combo'] input").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.Equal(["ArrowDown"], harness.Instance.HostKeys);

        harness.Find("[data-testid='combo'] input").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Equal(["ArrowDown"], harness.Instance.HostKeys);
        Assert.Empty(harness.FindAll("[role='listbox']"));

        harness.Find("[data-testid='combo'] input").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Equal(["ArrowDown", "Escape"], harness.Instance.HostKeys);
    }

    /// <summary>Review S2 round 1: a two-way host echoes each keystroke back as Value,
    /// and that echo used to reset the last committed text, so leaving the field never
    /// reported Changed.</summary>
    [Fact]
    public void Bound_two_way_leaving_a_typed_field_reports_changed()
    {
        using var context = new BunitContext();
        var harness = context.Render<ComboboxBoundHarness>();

        harness.Find("[data-testid='combo'] input").Input("5");
        Assert.Equal("5", harness.Instance.Text);

        harness.Find("[data-testid='combo'] input").Change("5");

        Assert.Equal([("5", ComboboxCommitReason.Changed)], harness.Instance.Commits);
    }

    /// <summary>Refuse goes back to the last value that stood, not to the refused text
    /// the host is holding as its echo — and tells the host so.</summary>
    [Fact]
    public void Bound_two_way_a_refused_commit_goes_back_to_the_last_value_that_stood()
    {
        using var context = new BunitContext();
        var harness = context.Render<ComboboxBoundHarness>();

        harness.Find("[data-testid='combo'] input").Input("7");
        harness.Find("[data-testid='combo'] input").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Assert.Equal("7", harness.Instance.Text);

        harness.Find("[data-testid='combo'] input").Input("lots");
        harness.Find("[data-testid='combo'] input").Change("lots");

        Assert.Equal("7", harness.Find("[data-testid='combo'] input").GetAttribute("value"));
        Assert.Equal("7", harness.Instance.Text);
        Assert.Equal(
            [("7", ComboboxCommitReason.Entered), ("lots", ComboboxCommitReason.Changed)],
            harness.Instance.Commits);
    }

    /// <summary>The option template draws what each option shows, and the option
    /// around it stays the listbox's option: the role, the id the active
    /// descendant names, and the pick, which still hands over the value.</summary>
    [Fact]
    public void An_option_template_draws_each_option_and_the_option_still_picks()
    {
        using var context = new BunitContext();
        var host = new Host();
        var combo = context.Render<Combobox>(parameters => parameters
            .Add(c => c.Options, Options)
            .Add(c => c.AriaLabel, "Hours")
            .Add(c => c.ValueChanged, (string text) => host.Values.Add(text))
            .Add(c => c.OnCommit, (ComboboxCommit commit) => host.Commits.Add((commit.Value, commit.Reason)))
            .Add(c => c.OptionTemplate, (SelectorOption option) => builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "hours-option");
                builder.AddContent(2, $"{option.Label} planned");
                builder.CloseElement();
            }));

        combo.Find("input").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        var options = combo.FindAll("[role='option']");
        Assert.Equal(["0.5h planned", "1h planned", "2h planned"], options.Select(option => option.TextContent));
        Assert.All(options, option => Assert.NotNull(option.QuerySelector(".hours-option")));

        options[1].Click();

        Assert.Equal([("1", ComboboxCommitReason.Picked)], host.Commits);
    }

    [Fact]
    public void A_disabled_combobox_opens_nothing()
    {
        using var context = new BunitContext();
        var combo = context.Render<Combobox>(parameters => parameters
            .Add(c => c.Options, Options)
            .Add(c => c.Disabled, true));

        combo.Find("input").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        Assert.Empty(combo.FindAll("[role='listbox']"));
        Assert.True(combo.Find("input").HasAttribute("disabled"));
    }

    [Fact]
    public void Typing_leaves_the_list_closed_by_default()
    {
        using var context = new BunitContext();
        var combo = Render(context, new Host());

        combo.Find("input").Input("1");

        Assert.Empty(combo.FindAll("[role='listbox']"));
    }

    /// <summary>A find field: the host narrows the options to what was typed, and the
    /// list opens as the reader types rather than waiting for Down.</summary>
    [Fact]
    public void With_open_on_input_typing_opens_the_list()
    {
        using var context = new BunitContext();
        var combo = context.Render<Combobox>(parameters => parameters
            .Add(c => c.Options, Options)
            .Add(c => c.AriaLabel, "Find")
            .Add(c => c.OpenOnInput, true));

        combo.Find("input").Input("1");

        Assert.Single(combo.FindAll("[role='listbox']"));
        Assert.Equal("true", combo.Find("input").GetAttribute("aria-expanded"));
    }
}
