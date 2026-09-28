using Backlog.UI.Components.Badges;

using Microsoft.AspNetCore.Components.Web;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A suggested chip: the dashed variant whatever the text names, the keycap for
/// the key that takes it, and a remove control named for turning it down.
/// </summary>
public sealed class TagChipTests
{
    [Fact]
    public void A_plain_chip_is_not_suggested_and_draws_no_key()
    {
        using var context = new BunitContext();

        var chip = context.Render<TagChip>(parameters => parameters
            .Add(c => c.Text, "#sync")
            .Add(c => c.Removable, true));

        Assert.DoesNotContain("tag-chip--suggested", chip.Find(".tag-chip").ClassList);
        Assert.Empty(chip.FindAll(".tag-chip__key"));
        Assert.Equal("Remove #sync", chip.Find(".tag-chip__remove").GetAttribute("aria-label"));
    }

    [Fact]
    public void A_suggested_chip_draws_its_key_announces_it_and_names_its_remove_control_as_the_caller_asks()
    {
        using var context = new BunitContext();
        var accepted = 0;
        string? dismissed = null;

        var chip = context.Render<TagChip>(parameters => parameters
            .Add(c => c.Text, "@alice")
            .Add(c => c.Suggested, true)
            .Add(c => c.Shortcut, "2")
            .Add(c => c.Label, "Add @alice")
            .Add(c => c.OnClick, (MouseEventArgs _) => accepted++)
            .Add(c => c.Removable, true)
            .Add(c => c.RemoveLabel, "Dismiss @alice")
            .Add(c => c.OnRemove, (string text) => dismissed = text));

        var classes = chip.Find(".tag-chip").ClassList;
        Assert.Contains("tag-chip--suggested", classes);
        Assert.Contains("tag-chip--person", classes);
        Assert.Equal("2", chip.Find(".tag-chip__key").TextContent);
        Assert.Equal("true", chip.Find(".tag-chip__key").GetAttribute("aria-hidden"));
        Assert.Equal("2", chip.Find("button.tag-chip__label").GetAttribute("aria-keyshortcuts"));

        chip.Find("button.tag-chip__label").Click();
        chip.Find("[aria-label='Dismiss @alice']").Click();

        Assert.Equal(1, accepted);
        Assert.Equal("@alice", dismissed);
    }
}
