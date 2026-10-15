using Microsoft.Extensions.Time.Testing;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The search box: a debounced report of what is typed, and a clear button.
/// <para>
/// The debounce is measured on a <see cref="FakeTimeProvider"/> the tests move by
/// hand, and it is a minute long: only the clock the box was handed can get through
/// it inside a wait, so a box that slept on the real clock fails here rather than
/// passing slowly. It used to be thirty real milliseconds, and under a loaded suite
/// the delay and the report after it could slip past the wait.
/// </para>
/// </summary>
public sealed class SearchBoxTests
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMinutes(1);

    [Fact]
    public void Typing_reports_the_query_once_the_debounce_has_elapsed()
    {
        using var context = new BunitContext();
        var clock = new FakeTimeProvider();
        var reported = new List<string>();
        var search = Render(context, clock, reported);

        Type(search, "spike");
        clock.Advance(Debounce - TimeSpan.FromMilliseconds(1));
        Assert.Empty(reported);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        search.WaitForAssertion(() => Assert.Equal(["spike"], reported));
    }

    /// <summary>A second keystroke inside the debounce starts it again, and only
    /// the text in the box is reported.</summary>
    [Fact]
    public void A_keystroke_inside_the_debounce_starts_it_again()
    {
        using var context = new BunitContext();
        var clock = new FakeTimeProvider();
        var reported = new List<string>();
        var search = Render(context, clock, reported);

        Type(search, "sp");
        clock.Advance(Debounce * 2 / 3);

        // Past the first keystroke's debounce by now, but not the second's.
        Type(search, "spike");
        clock.Advance(Debounce * 2 / 3);
        Assert.Empty(reported);

        clock.Advance(Debounce / 3);
        search.WaitForAssertion(() => Assert.Equal(["spike"], reported));
    }

    private static IRenderedComponent<SearchBox> Render(BunitContext context, TimeProvider clock, List<string> reported) =>
        context.Render<SearchBox>(parameters => parameters
            .Add(s => s.DebounceMilliseconds, (int)Debounce.TotalMilliseconds)
            .Add(s => s.Clock, clock)
            .Add(s => s.ValueChanged, (string value) => reported.Add(value)));

    /// <summary>A keystroke, and then the box showing it — which it does before it
    /// starts the debounce, so the clock is never moved past a delay that has not
    /// begun.</summary>
    private static void Type(IRenderedComponent<SearchBox> search, string text)
    {
        search.Find("input").Input(text);
        search.WaitForAssertion(() => Assert.Equal(text, search.Find("input").GetAttribute("value")));
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
