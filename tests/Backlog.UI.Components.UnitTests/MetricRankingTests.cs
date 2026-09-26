namespace Backlog.UI.Components.UnitTests;

public sealed class MetricRankingTests
{
    private static readonly IReadOnlyList<MetricRankItem> ToolTime =
    [
        new("Edit", 2m, "22 calls"),
        new("Shell", 400m, "74 calls"),
        new("QA", 100m, "36 calls · 2 failed")
    ];

    [Fact]
    public void Rows_are_ranked_largest_first_whatever_order_they_arrive_in()
    {
        using var context = new BunitContext();

        var ranking = context.Render<MetricRanking>(parameters => parameters
            .Add(r => r.Items, ToolTime));

        Assert.Equal(
            ["Shell", "QA", "Edit"],
            ranking.FindAll(".metric-ranking__name").Select(name => name.TextContent));
    }

    [Fact]
    public void Equal_values_keep_the_order_they_were_given_in()
    {
        using var context = new BunitContext();

        var ranking = context.Render<MetricRanking>(parameters => parameters
            .Add(r => r.Items, [new MetricRankItem("Agents", 5m), new MetricRankItem("Agent tasks", 5m), new MetricRankItem("Shell", 9m)]));

        Assert.Equal(
            ["Shell", "Agents", "Agent tasks"],
            ranking.FindAll(".metric-ranking__name").Select(name => name.TextContent));
    }

    [Fact]
    public void Bars_are_scaled_to_the_leader_rather_than_to_the_total()
    {
        // The rows need not add up to anything — a run's servers overlap its tool
        // categories — so the leader fills its track and the rest read against it.
        using var context = new BunitContext();

        var ranking = context.Render<MetricRanking>(parameters => parameters
            .Add(r => r.Items, ToolTime));

        Assert.Equal(
            ["width: 100%", "width: 25%", "width: 0.5%"],
            ranking.FindAll(".metric-ranking__fill").Select(fill => fill.GetAttribute("style")));
    }

    [Fact]
    public void Every_figure_is_printed_and_the_bars_are_hidden()
    {
        using var context = new BunitContext();

        var ranking = context.Render<MetricRanking>(parameters => parameters
            .Add(r => r.Items, ToolTime)
            .Add(r => r.FormatValue, value => $"{value}s"));

        var qa = ranking.FindAll(".metric-ranking__item")[1];

        Assert.Equal("36 calls · 2 failed", qa.QuerySelector(".metric-ranking__detail")!.TextContent);
        Assert.Equal("100s", qa.QuerySelector(".metric-ranking__value")!.TextContent);
        Assert.All(ranking.FindAll(".metric-ranking__track"), track => Assert.Equal("true", track.GetAttribute("aria-hidden")));
    }

    [Fact]
    public void A_row_worth_nothing_is_listed_with_no_mark()
    {
        using var context = new BunitContext();

        var ranking = context.Render<MetricRanking>(parameters => parameters
            .Add(r => r.Items, [new MetricRankItem("Shell", 10m), new MetricRankItem("Read", 0m), new MetricRankItem("Odd", -3m)]));

        var items = ranking.FindAll(".metric-ranking__item");

        Assert.Equal(3, items.Count);
        Assert.Null(items[1].QuerySelector(".metric-ranking__fill"));
        Assert.Null(items[2].QuerySelector(".metric-ranking__fill"));
    }

    [Fact]
    public void No_rows_is_the_empty_state_rather_than_an_empty_list()
    {
        using var context = new BunitContext();

        var ranking = context.Render<MetricRanking>(parameters => parameters
            .Add(r => r.EmptyMessage, "No tool calls recorded."));

        Assert.Empty(ranking.FindAll(".metric-ranking__list"));
        Assert.Contains("No tool calls recorded.", ranking.Markup);
    }

    [Fact]
    public void A_labelled_ranking_names_its_list()
    {
        using var context = new BunitContext();

        var ranking = context.Render<MetricRanking>(parameters => parameters
            .Add(r => r.Label, "Time by tool")
            .Add(r => r.Items, ToolTime));

        var label = ranking.Find(".metric-ranking__label");

        Assert.Equal(label.Id, ranking.Find(".metric-ranking__list").GetAttribute("aria-labelledby"));
    }

    [Fact]
    public void Names_take_the_callers_class_and_the_block_class_is_replaceable()
    {
        using var context = new BunitContext();

        var ranking = context.Render<MetricRanking>(parameters => parameters
            .Add(r => r.Items, ToolTime)
            .Add(r => r.NameCssClass, "data-table__mono")
            .Add(r => r.BaseClass, "run-tools"));

        var name = ranking.Find(".run-tools__name");

        Assert.Contains("data-table__mono", name.ClassList);
        Assert.Empty(ranking.FindAll(".metric-ranking__name"));
    }
}
