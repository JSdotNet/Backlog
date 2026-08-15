namespace Backlog.UI.Components.UnitTests;

public sealed class SearchBoxTests
{
    [Fact]
    public void Typing_reports_the_query_once_the_debounce_has_elapsed()
    {
        using var context = new BunitContext();
        var reported = new List<string>();

        var search = context.Render<SearchBox>(parameters => parameters
            .Add(s => s.DebounceMilliseconds, 30)
            .Add(s => s.ValueChanged, (string value) => reported.Add(value)));

        search.Find("input").Input("spike");

        // The delay is real, so the assertion has to be allowed to arrive late
        // rather than the test sleeping for a fixed guess.
        search.WaitForAssertion(() => Assert.Equal(["spike"], reported));
    }

    [Fact]
    public void The_clear_button_empties_the_value_and_reports_it()
    {
        using var context = new BunitContext();
        string? reported = null;

        var search = context.Render<SearchBox>(parameters => parameters
            .Add(s => s.Value, "spike")
            .Add(s => s.ValueChanged, (string value) => reported = value));

        search.Find(".search-box__clear").Click();

        Assert.Equal(string.Empty, reported);
        Assert.Equal(string.Empty, search.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void There_is_nothing_to_clear_while_the_box_is_empty()
    {
        using var context = new BunitContext();

        var search = context.Render<SearchBox>();

        Assert.Empty(search.FindAll(".search-box__clear"));
        Assert.Equal("search", search.Find("div").GetAttribute("role"));
    }
}
