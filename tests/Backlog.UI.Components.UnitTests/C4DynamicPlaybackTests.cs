namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A dynamic view on the C4 zoom tab, played step by step: the numbered arrows, the
/// steps panel with its from → to, description and technology, Previous / Play / Next
/// and their bounds, and Play under reduced motion.
/// <para>
/// The case is the requirement's own (<c>.devbook/domain/devbook/requirements.md</c>,
/// "The supplementary views move in their own way", "Stepping a dynamic view"). The
/// timer Play walks on is real; the tests shorten it through <c>PlayInterval</c>.
/// </para>
/// </summary>
public sealed class C4DynamicPlaybackTests : IDisposable
{
    private const string Attach = "backlogC4Zoom.attach";
    private const string ReducedMotion = "backlogC4Zoom.reducedMotion";

    private const string Source = """
        workspace "Backlog" "For the playback tests" {
            !identifiers hierarchical
            model {
                me = person "ME" "The owner"
                backlog = softwareSystem "Prompt Backlog" "Local-first work management" {
                    desktop = container "Desktop App" "Windows client" ".NET MAUI Blazor Hybrid"
                    store = container "Local Task Store" "Entries" "SQLite" "Database"
                    cloud = container "Cloud Service" "Relays events" "ASP.NET Core"
                }
                github = softwareSystem "GitHub" "Issues and webhooks" "External"
                me -> backlog.desktop "Captures work"
                backlog.desktop -> backlog.store "Writes the entry" "file system"
                backlog.desktop -> github "Syncs issues" "HTTPS"
                backlog.desktop -> github "Creates the issue" "HTTPS, gh CLI"
                github -> backlog.cloud "Delivers the webhook" "HTTPS"
            }
            views {
                systemContext backlog "context-backlog" "System context" { include * }
                container backlog "containers-backlog" "Containers" { include * }
                dynamic backlog "capture-to-issue" "Capture to issue" {
                    me -> backlog.desktop "Captures an entry"
                    backlog.desktop -> backlog.store "Writes the entry"
                    backlog.desktop -> github "Creates the issue"
                    github -> backlog.cloud "Delivers the webhook"
                }
            }
        }
        """;

    private static readonly C4Workspace Workspace = C4DslReader.Read(Source);

    private readonly BunitContext _context = new();

    public C4DynamicPlaybackTests()
    {
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.JSInterop.Setup<bool>(Attach, _ => true).SetResult(true);
    }

    public void Dispose() => _context.Dispose();

    private static C4View View(string key) => Workspace.View(key) ?? throw new InvalidOperationException(key);

    private IRenderedComponent<C4ZoomExplorer> RenderOnDynamic(TimeSpan? interval = null)
    {
        var explorer = _context.Render<C4ZoomExplorer>(parameters => parameters
            .Add(c => c.Workspace, Workspace)
            .Add(c => c.HintDismissed, true)
            .Add(c => c.PlayInterval, interval ?? TimeSpan.FromMinutes(10)));

        explorer.FindAll("[data-testid='c4-zoom-also-link']")
            .Single(link => link.TextContent.Trim() == "Dynamic")
            .Click();

        Assert.Equal("capture-to-issue", explorer.Instance.ViewKey);
        return explorer;
    }

    private static string Position(IRenderedComponent<C4ZoomExplorer> explorer) =>
        explorer.Find("[data-testid='c4-dynamic-position']").TextContent.Trim();

    private static string Diagram(IRenderedComponent<C4ZoomExplorer> explorer) =>
        explorer.Find("[data-testid='c4-zoom-diagram']").InnerHtml;

    private static void Press(IRenderedComponent<C4ZoomExplorer> explorer, string testId) =>
        explorer.Find($"[data-testid='{testId}']").Click();

    private static bool Disabled(IRenderedComponent<C4ZoomExplorer> explorer, string testId) =>
        explorer.Find($"[data-testid='{testId}']").GetAttribute("aria-disabled") == "true";

    // ---- the steps, as the model says them -----------------------------------------

    [Fact]
    public void The_test_workspace_parses_clean() => Assert.Empty(Workspace.Problems);

    [Fact]
    public void Each_step_names_its_ends_and_takes_its_technology_from_the_model_relationship()
    {
        var steps = C4DynamicPlayback.Steps(Workspace, View("capture-to-issue"));

        Assert.Equal([1, 2, 3, 4], steps.Select(step => step.Order));
        Assert.Equal(new C4PlaybackStep(2, "Desktop App", "Local Task Store", "Writes the entry", "file system"), steps[1]);

        // Two relationships join the Desktop App to GitHub; the one the step describes wins.
        Assert.Equal("HTTPS, gh CLI", steps[2].Technology);

        // The model's ME -> Desktop App says "Captures work" and names no technology.
        Assert.Null(steps[0].Technology);
    }

    [Fact]
    public void A_view_that_is_not_dynamic_has_no_steps_to_play() =>
        Assert.Empty(C4DynamicPlayback.Steps(Workspace, View("containers-backlog")));

    [Theory]
    [InlineData(0, 4, 1)]
    [InlineData(2, 4, 3)]
    [InlineData(3, 4, 3)]
    [InlineData(0, 0, 0)]
    public void Next_is_held_at_the_last_step(int current, int count, int expected) =>
        Assert.Equal(expected, C4DynamicPlayback.Next(current, count));

    [Theory]
    [InlineData(3, 4, 2)]
    [InlineData(1, 4, 0)]
    [InlineData(0, 4, 0)]
    [InlineData(0, 0, 0)]
    public void Previous_is_held_at_the_first_step(int current, int count, int expected) =>
        Assert.Equal(expected, C4DynamicPlayback.Previous(current, count));

    // ---- the panel and the picture -------------------------------------------------

    [Fact]
    public void A_view_that_is_not_dynamic_shows_no_steps_panel()
    {
        var explorer = _context.Render<C4ZoomExplorer>(parameters => parameters
            .Add(c => c.Workspace, Workspace)
            .Add(c => c.HintDismissed, true));

        Assert.Empty(explorer.FindAll("[data-testid='c4-dynamic-steps']"));
        Assert.DoesNotContain("data-c4-step", Diagram(explorer), StringComparison.Ordinal);
        Assert.DoesNotContain("c4-zoom-explorer--dynamic", explorer.Find("[data-testid='c4-zoom-explorer']").ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dynamic_view_opens_on_step_one_with_every_step_listed_in_order()
    {
        var explorer = RenderOnDynamic();

        Assert.Equal("Step 1 of 4", Position(explorer));
        Assert.Equal(1, explorer.Instance.CurrentStep);

        var rows = explorer.FindAll("[data-testid='c4-dynamic-step']");
        Assert.Equal(["1", "2", "3", "4"], rows.Select(row => row.GetAttribute("data-order")));
        Assert.Contains("ME → Desktop App", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Desktop App → GitHub", rows[2].TextContent, StringComparison.Ordinal);
        Assert.Equal("Creates the issue · HTTPS, gh CLI", rows[2].QuerySelector("[data-testid='c4-dynamic-step-detail']")!.TextContent);
        Assert.Equal("Captures an entry", rows[0].QuerySelector("[data-testid='c4-dynamic-step-detail']")!.TextContent);
    }

    [Fact]
    public void Every_arrow_carries_its_number_and_only_the_current_one_is_drawn_as_current()
    {
        var explorer = RenderOnDynamic();
        var markup = Diagram(explorer);

        foreach (var order in new[] { 1, 2, 3, 4 })
        {
            Assert.Contains($"data-c4-step=\"{order}\"", markup, StringComparison.Ordinal);
        }

        Assert.Equal(4, Count(markup, "class=\"c4-diagram__step-badge\""));
        Assert.Equal(1, Count(markup, "c4-diagram__edge--current"));
        Assert.Equal(1, Count(markup, "c4-diagram__edge-label--current"));
        Assert.Matches("class=\"c4-diagram__edge c4-diagram__edge--step c4-diagram__edge--current\" data-c4-step=\"1\"", markup);
    }

    // ---- stepping and its bounds ---------------------------------------------------

    [Fact]
    public void Stepping_forward_from_step_three_draws_step_four_as_current_and_the_rest_not()
    {
        var explorer = RenderOnDynamic();
        Press(explorer, "c4-dynamic-next");
        Press(explorer, "c4-dynamic-next");
        Assert.Equal("Step 3 of 4", Position(explorer));

        Press(explorer, "c4-dynamic-next");

        Assert.Equal("Step 4 of 4", Position(explorer));
        Assert.Equal(4, explorer.Instance.CurrentStep);
        Assert.Contains("class=\"c4-diagram__edge c4-diagram__edge--step c4-diagram__edge--current\" data-c4-step=\"4\"", Diagram(explorer), StringComparison.Ordinal);
        Assert.Equal(1, Count(Diagram(explorer), "c4-diagram__edge--current"));

        var rows = explorer.FindAll("[data-testid='c4-dynamic-step']");
        Assert.Equal(["4"], rows.Where(row => row.GetAttribute("aria-current") == "step").Select(row => row.GetAttribute("data-order")));
        Assert.Single(rows, row => row.ClassList.Contains("c4-dynamic-steps__step--current"));
    }

    [Fact]
    public void Previous_is_disabled_on_the_first_step_and_next_on_the_last()
    {
        var explorer = RenderOnDynamic();

        Assert.True(Disabled(explorer, "c4-dynamic-previous"));
        Assert.False(Disabled(explorer, "c4-dynamic-next"));

        for (var press = 0; press < 3; press++) Press(explorer, "c4-dynamic-next");

        Assert.Equal("Step 4 of 4", Position(explorer));
        Assert.True(Disabled(explorer, "c4-dynamic-next"));
        Assert.False(Disabled(explorer, "c4-dynamic-previous"));

        // A press on Next at the bound changes nothing, and the button stays a button
        // the focus can stay on.
        Press(explorer, "c4-dynamic-next");
        Assert.Equal("Step 4 of 4", Position(explorer));
        Assert.False(explorer.Find("[data-testid='c4-dynamic-next']").HasAttribute("disabled"));

        Press(explorer, "c4-dynamic-previous");
        Assert.Equal("Step 3 of 4", Position(explorer));

        for (var press = 0; press < 4; press++) Press(explorer, "c4-dynamic-previous");
        Assert.Equal("Step 1 of 4", Position(explorer));
        Assert.True(Disabled(explorer, "c4-dynamic-previous"));
    }

    [Fact]
    public void The_current_step_is_announced_with_its_ends_and_what_it_does()
    {
        var explorer = RenderOnDynamic();
        Press(explorer, "c4-dynamic-next");
        Press(explorer, "c4-dynamic-next");

        var announcement = explorer.Find("[data-testid='c4-dynamic-announcement']");
        Assert.Equal("polite", announcement.GetAttribute("aria-live"));
        Assert.Equal("Step 3 of 4: Desktop App → GitHub. Creates the issue · HTTPS, gh CLI", announcement.TextContent);
    }

    [Fact]
    public void Two_quick_play_presses_are_a_play_and_a_pause()
    {
        var explorer = RenderOnDynamic(TimeSpan.FromMilliseconds(20));

        Press(explorer, "c4-dynamic-play");
        Press(explorer, "c4-dynamic-play");
        var stoppedOn = explorer.Instance.CurrentStep;

        Assert.False(explorer.Instance.Playing);
        Thread.Sleep(150);
        Assert.Equal(stoppedOn, explorer.Instance.CurrentStep);
    }

    [Fact]
    public async Task Closing_the_tab_while_playing_stops_the_walk()
    {
        var explorer = RenderOnDynamic(TimeSpan.FromMilliseconds(20));
        Press(explorer, "c4-dynamic-play");

        await explorer.Instance.DisposeAsync();

        Assert.False(explorer.Instance.Playing);
        var stoppedOn = explorer.Instance.CurrentStep;
        await Task.Delay(150, TestContext.Current.CancellationToken);
        Assert.Equal(stoppedOn, explorer.Instance.CurrentStep);
    }

    [Fact]
    public void Picking_a_step_in_the_list_makes_it_current()
    {
        var explorer = RenderOnDynamic();

        explorer.FindAll("[data-testid='c4-dynamic-step']")[2].Click();

        Assert.Equal(3, explorer.Instance.CurrentStep);
        Assert.Equal("Step 3 of 4", Position(explorer));
    }

    [Fact]
    public void Each_new_current_step_is_handed_to_the_viewer_with_its_new_picture()
    {
        var explorer = RenderOnDynamic();
        var before = _context.JSInterop.Invocations[Attach].Count;

        Press(explorer, "c4-dynamic-next");

        Assert.Equal(before + 1, _context.JSInterop.Invocations[Attach].Count);
    }

    [Fact]
    public void Leaving_and_coming_back_starts_the_view_again_on_step_one()
    {
        var explorer = RenderOnDynamic();
        Press(explorer, "c4-dynamic-next");
        Assert.Equal(2, explorer.Instance.CurrentStep);

        explorer.Find("[data-testid='c4-zoom-step'][data-level='1']").Click();
        Assert.Null(explorer.Instance.CurrentStep);

        explorer.FindAll("[data-testid='c4-zoom-also-link']").Single(link => link.TextContent.Trim() == "Dynamic").Click();
        Assert.Equal(1, explorer.Instance.CurrentStep);
    }

    // ---- Play -----------------------------------------------------------------------

    [Fact]
    public void Play_walks_the_steps_and_stops_on_the_last()
    {
        var explorer = RenderOnDynamic(TimeSpan.FromMilliseconds(10));

        Press(explorer, "c4-dynamic-play");
        Assert.True(explorer.Instance.Playing);

        explorer.WaitForAssertion(() =>
        {
            Assert.Equal("Step 4 of 4", Position(explorer));
            Assert.Equal("Play", explorer.Find("[data-testid='c4-dynamic-play']").TextContent.Trim());
        }, TimeSpan.FromSeconds(5));

        Assert.Equal(4, explorer.Instance.CurrentStep);
        Assert.False(explorer.Instance.Playing);
    }

    [Fact]
    public void Play_pressed_while_playing_pauses_where_it_is()
    {
        var explorer = RenderOnDynamic(TimeSpan.FromMinutes(10));

        Press(explorer, "c4-dynamic-play");
        Assert.Equal("Pause", explorer.Find("[data-testid='c4-dynamic-play']").TextContent.Trim());
        Assert.Equal("Pause", explorer.Find("[data-testid='c4-dynamic-play']").GetAttribute("aria-label"));

        Press(explorer, "c4-dynamic-play");

        Assert.False(explorer.Instance.Playing);
        Assert.Equal(1, explorer.Instance.CurrentStep);
    }

    [Fact]
    public void Play_on_the_last_step_starts_again_from_the_first()
    {
        var explorer = RenderOnDynamic(TimeSpan.FromMinutes(10));
        for (var press = 0; press < 3; press++) Press(explorer, "c4-dynamic-next");

        Press(explorer, "c4-dynamic-play");

        Assert.Equal(1, explorer.Instance.CurrentStep);
        Assert.True(explorer.Instance.Playing);
    }

    [Fact]
    public void A_step_pressed_while_playing_stops_the_walk()
    {
        var explorer = RenderOnDynamic(TimeSpan.FromMinutes(10));

        Press(explorer, "c4-dynamic-play");
        Press(explorer, "c4-dynamic-next");

        Assert.False(explorer.Instance.Playing);
        Assert.Equal(2, explorer.Instance.CurrentStep);
    }

    [Fact]
    public void Under_reduced_motion_play_moves_one_step_per_press_and_never_walks()
    {
        _context.JSInterop.Setup<bool>(ReducedMotion).SetResult(true);
        var explorer = RenderOnDynamic(TimeSpan.FromMilliseconds(10));

        explorer.WaitForElement("[data-testid='c4-dynamic-reduced-motion']");
        Assert.Equal("Play the next step", explorer.Find("[data-testid='c4-dynamic-play']").GetAttribute("aria-label"));

        Press(explorer, "c4-dynamic-play");

        Assert.False(explorer.Instance.Playing);
        Assert.Equal(2, explorer.Instance.CurrentStep);

        for (var press = 0; press < 3; press++) Press(explorer, "c4-dynamic-play");

        // From the last step a press goes back to the first, as Play does without it.
        Assert.Equal(1, explorer.Instance.CurrentStep);
        Assert.False(explorer.Instance.Playing);
    }

    // ---- the C4 tab is untouched ---------------------------------------------------

    /// <summary>The C4 tab's explorer hands the writer no options; its dynamic arrows
    /// keep the "1. " prefix and draw no badge and no step state (local ADR 0042).</summary>
    [Fact]
    public void Without_step_options_the_writer_draws_a_dynamic_view_as_before()
    {
        var diagram = C4LayoutEngine.Build(Workspace, View("capture-to-issue"));

        var plain = C4SvgWriter.Write(diagram, "plain");
        var none = C4SvgWriter.Write(diagram, "plain", options: C4SvgOptions.None);

        Assert.Equal(plain, none);
        Assert.Contains("<tspan class=\"c4-diagram__edge-order\">1. </tspan>", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("c4-diagram__step-badge", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("data-c4-step", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("--current", plain, StringComparison.Ordinal);
        Assert.Contains("<g class=\"c4-diagram__edge\">", plain, StringComparison.Ordinal);
    }

    private static int Count(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;
}
