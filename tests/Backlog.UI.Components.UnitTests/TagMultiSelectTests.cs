namespace Backlog.UI.Components.UnitTests;

public sealed class TagMultiSelectTests
{
    private static readonly SelectorOption[] Options =
    [
        new("desktop", "desktop"),
        new("spacing", "spacing"),
        new("sync", "sync")
    ];

    [Fact]
    public void The_input_is_wired_as_a_combobox_over_the_option_list()
    {
        using var context = new BunitContext();

        var select = context.Render<TagMultiSelect>(parameters => parameters.Add(s => s.Options, Options));
        var input = select.Find("input");

        Assert.Equal("combobox", input.GetAttribute("role"));
        Assert.Equal("false", input.GetAttribute("aria-expanded"));

        input.Focus();
        input.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        var reopened = select.Find("input");
        Assert.Equal("true", reopened.GetAttribute("aria-expanded"));
        Assert.Equal(select.Find("[role='option']").Id, reopened.GetAttribute("aria-activedescendant"));
        Assert.Equal(reopened.GetAttribute("aria-controls"), select.Find("[role='listbox']").Id);
    }

    [Fact]
    public void Choosing_an_option_turns_it_into_a_chip()
    {
        using var context = new BunitContext();
        IReadOnlyList<string>? reported = null;

        var select = context.Render<TagMultiSelect>(parameters => parameters
            .Add(s => s.Options, Options)
            .Add(s => s.SelectedValuesChanged, (IReadOnlyList<string> values) => reported = values));

        select.Find("input").Focus();
        select.FindAll("[role='option']")[1].Click();

        Assert.Equal(["spacing"], reported);
        Assert.Equal("spacing", select.Find(".tag-select__chip .tag-chip__label").TextContent);
    }

    [Fact]
    public void Backspace_on_an_empty_input_drops_the_last_chip()
    {
        using var context = new BunitContext();
        IReadOnlyList<string>? reported = null;

        var select = context.Render<TagMultiSelect>(parameters => parameters
            .Add(s => s.Options, Options)
            .Add(s => s.SelectedValues, new[] { "desktop", "sync" })
            .Add(s => s.SelectedValuesChanged, (IReadOnlyList<string> values) => reported = values));

        select.Find("input").KeyDown(new KeyboardEventArgs { Key = "Backspace" });

        Assert.Equal(["desktop"], reported);
    }

    [Fact]
    public void Backspace_leaves_the_chips_alone_while_there_is_still_a_query_to_delete()
    {
        using var context = new BunitContext();
        var changes = 0;

        var select = context.Render<TagMultiSelect>(parameters => parameters
            .Add(s => s.Options, Options)
            .Add(s => s.SelectedValues, new[] { "desktop" })
            .Add(s => s.SelectedValuesChanged, (IReadOnlyList<string> _) => changes++));

        select.Find("input").Input("sy");
        select.Find("input").KeyDown(new KeyboardEventArgs { Key = "Backspace" });

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Allow_create_commits_whatever_was_typed()
    {
        using var context = new BunitContext();
        IReadOnlyList<string>? reported = null;

        var select = context.Render<TagMultiSelect>(parameters => parameters
            .Add(s => s.Options, Options)
            .Add(s => s.AllowCreate, true)
            .Add(s => s.SelectedValuesChanged, (IReadOnlyList<string> values) => reported = values));

        select.Find("input").Input("qa-new-tag");
        select.FindAll("[role='option']").Last().Click();

        Assert.Equal(["qa-new-tag"], reported);
    }

    [Fact]
    public void Chips_past_the_visible_limit_collapse_into_a_count()
    {
        using var context = new BunitContext();

        var select = context.Render<TagMultiSelect>(parameters => parameters
            .Add(s => s.Options, Options)
            .Add(s => s.MaxVisible, 1)
            .Add(s => s.SelectedValues, new[] { "desktop", "spacing", "sync" }));

        Assert.Single(select.FindAll(".tag-select__chip"));
        Assert.Equal("+2", select.Find(".tag-select__overflow").TextContent);
    }
}
