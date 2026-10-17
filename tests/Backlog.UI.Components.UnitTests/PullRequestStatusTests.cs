using Backlog.UI.Components.Progress;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The pull request status line as drawn
/// (<c>.devbook/domain/progress/requirements.md#pull-request-status</c>): the pill, a mark
/// per count with its glyph hidden, a visible count with no words, and one phrase per mark
/// for a screen reader; a merged pull request reads "merged" alone.
/// </summary>
public sealed class PullRequestStatusTests
{
    private static IRenderedComponent<PullRequestStatus> Render(BunitContext context, PullRequestStatusModel model, bool showNumber = true) =>
        context.Render<PullRequestStatus>(p => p
            .Add(c => c.Model, model)
            .Add(c => c.ShowNumber, showNumber)
            .Add(c => c.TestId, "pr"));

    /// <summary>Scenario "Read by a screen reader": #1341 shows 14/14 and 1/2, and hears
    /// "14 of 14 checks passed" and "1 of 2 approvals"; the visible text holds neither word.</summary>
    [Fact]
    public void The_visible_text_holds_counts_and_a_screen_reader_hears_the_words()
    {
        using var context = new BunitContext();
        var line = Render(context, new PullRequestStatusModel(1341, "open", new PullRequestChecks(14, 0, 0), new PullRequestApprovals(1, 2)));

        var checks = line.Find("[data-testid='pr-checks']");
        var approvals = line.Find("[data-testid='pr-approvals']");

        Assert.Equal("passed", checks.GetAttribute("data-variant"));
        Assert.Equal("14/14", checks.QuerySelector(".pull-request-status__count")!.TextContent);
        Assert.Equal("true", checks.QuerySelector(".pull-request-status__count")!.GetAttribute("aria-hidden"));
        Assert.Equal("14 of 14 checks passed", checks.QuerySelector(".sr-only")!.TextContent);
        Assert.Equal("true", checks.QuerySelector("svg")!.GetAttribute("aria-hidden"));
        Assert.Contains("circle-check-icon", checks.QuerySelector("svg")!.ClassList);

        Assert.Equal("short", approvals.GetAttribute("data-variant"));
        Assert.Equal("1/2", approvals.QuerySelector(".pull-request-status__count")!.TextContent);
        Assert.Equal("1 of 2 approvals", approvals.QuerySelector(".sr-only")!.TextContent);
        Assert.Contains("user-check-icon", approvals.QuerySelector("svg")!.ClassList);

        var visible = string.Concat(line.FindAll("*")
            .Where(element => element.Children.Length == 0 && !element.ClassList.Contains("sr-only"))
            .Select(element => element.TextContent));
        Assert.DoesNotContain("checks", visible, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("approvals", visible, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_pill_is_the_gh_badge_for_the_state()
    {
        using var context = new BunitContext();
        var line = Render(context, new PullRequestStatusModel(1358, "draft", Url: "https://example.com/pull/1358"));

        var pill = line.Find("[data-testid='pr-number']");
        Assert.Equal("#1358", pill.TextContent);
        Assert.Contains("badge--gh-draft", pill.ClassList);
        Assert.Equal("https://example.com/pull/1358", pill.GetAttribute("href"));
    }

    [Theory]
    [InlineData(9, 0, 5, "running", "circle-dashed-icon")]
    [InlineData(2, 1, 11, "failing", "circle-x-icon")]
    public void A_running_or_failing_check_draws_its_own_glyph(int passed, int failed, int pending, string variant, string glyph)
    {
        using var context = new BunitContext();
        var line = Render(context, new PullRequestStatusModel(1358, "open", new PullRequestChecks(passed, failed, pending)));

        var checks = line.Find("[data-testid='pr-checks']");
        Assert.Equal(variant, checks.GetAttribute("data-variant"));
        Assert.Contains($"pull-request-status__mark--{variant}", checks.ClassList);
        Assert.Contains(glyph, checks.QuerySelector("svg")!.ClassList);
    }

    [Fact]
    public void A_merged_pull_request_reads_merged_and_draws_no_marks()
    {
        using var context = new BunitContext();
        var line = Render(context, new PullRequestStatusModel(1352, "merged", new PullRequestChecks(14, 0, 0), new PullRequestApprovals(2, 2)));

        Assert.Equal("merged", line.Find("[data-testid='pr-merged']").TextContent);
        Assert.Empty(line.FindAll("[data-mark]"));
    }

    [Fact]
    public void No_checks_leave_their_mark_out_and_no_required_total_shows_the_count_alone()
    {
        using var context = new BunitContext();
        var line = Render(context, new PullRequestStatusModel(1360, "open", null, new PullRequestApprovals(1, null)));

        Assert.Empty(line.FindAll("[data-testid='pr-checks']"));
        var approvals = line.Find("[data-testid='pr-approvals']");
        Assert.Equal("1", approvals.QuerySelector(".pull-request-status__count")!.TextContent);
        Assert.Equal("1 approval, none required", approvals.QuerySelector(".sr-only")!.TextContent);
    }

    [Fact]
    public void Marks_only_leaves_the_pill_to_the_host()
    {
        using var context = new BunitContext();
        var line = Render(context, new PullRequestStatusModel(1341, "open", new PullRequestChecks(14, 0, 0)), showNumber: false);

        Assert.Empty(line.FindAll("[data-testid='pr-number']"));
        Assert.Single(line.FindAll("[data-mark]"));
    }
}

/// <summary>The four glyphs the status line draws: Lucide outlines, decorative, sized by
/// the host and inked by what they sit in.</summary>
public sealed class StatusGlyphTests
{
    public static TheoryData<Type, string> Glyphs => new()
    {
        { typeof(CircleCheckIcon), "circle-check-icon" },
        { typeof(CircleDashedIcon), "circle-dashed-icon" },
        { typeof(CircleXIcon), "circle-x-icon" },
        { typeof(UserCheckIcon), "user-check-icon" }
    };

    [Theory]
    [MemberData(nameof(Glyphs))]
    public void A_glyph_is_a_hidden_outline_at_the_size_it_is_given(Type icon, string cssClass)
    {
        using var context = new BunitContext();
        var glyph = context.Render(builder =>
        {
            builder.OpenComponent(0, icon);
            builder.AddAttribute(1, "Size", "20");
            builder.AddAttribute(2, "CssClass", "extra");
            builder.CloseComponent();
        });

        var svg = glyph.Find("svg");
        Assert.Equal("true", svg.GetAttribute("aria-hidden"));
        Assert.Equal("false", svg.GetAttribute("focusable"));
        Assert.Equal("20", svg.GetAttribute("width"));
        Assert.Equal("currentColor", svg.GetAttribute("stroke"));
        Assert.Equal("none", svg.GetAttribute("fill"));
        Assert.Contains(cssClass, svg.ClassList);
        Assert.Contains("extra", svg.ClassList);
    }
}
