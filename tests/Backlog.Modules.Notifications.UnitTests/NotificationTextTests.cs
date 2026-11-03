using Backlog.Modules.Notifications.Abstractions;

namespace Backlog.Modules.Notifications.UnitTests;

/// <summary>
/// The one cut every notification text goes through: within its bound in UTF-16 units — the
/// owner checks <see cref="string.Length"/> — ending on a whole character a person reads, never
/// half an emoji, and an ellipsis marking the cut.
/// </summary>
public sealed class NotificationTextTests
{
    private const string Grinning = "\U0001F600";
    private const string Family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";
    private const string NetherlandsFlag = "\U0001F1F3\U0001F1F1";

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("forty characters exactly, no more no les")]
    public void Text_that_fits_comes_back_unchanged(string text)
    {
        Assert.Same(text, NotificationText.Cut(text, 40));
    }

    [Fact]
    public void A_cut_inside_a_surrogate_pair_drops_the_whole_pair()
    {
        var text = new string('a', 38) + Grinning + "tail";

        Assert.Equal(new string('a', 38) + "…", NotificationText.Cut(text, 40));
    }

    [Fact]
    public void A_cut_inside_a_joined_emoji_drops_the_whole_emoji()
    {
        var text = new string('a', 35) + Family + "tail";

        Assert.Equal(new string('a', 35) + "…", NotificationText.Cut(text, 40));
    }

    [Fact]
    public void A_cut_inside_a_flag_drops_the_whole_flag()
    {
        var text = new string('a', 36) + NetherlandsFlag + "tail";

        Assert.Equal(new string('a', 36) + "…", NotificationText.Cut(text, 40));
    }

    [Fact]
    public void A_first_character_longer_than_the_bound_is_cut_between_code_points()
    {
        var text = "e" + string.Concat(Enumerable.Repeat("\u0301", 50));

        var cut = NotificationText.Cut(text, 10);

        Assert.Equal(text[..9] + "…", cut);
    }

    [Fact]
    public void Every_cut_is_within_its_bound_and_never_ends_in_half_a_pair()
    {
        var text = "Ask " + Grinning + " the " + Family + " team in " + NetherlandsFlag + " about " + Grinning + Grinning + " soon";

        for (var length = 1; length < text.Length; length++)
        {
            var cut = NotificationText.Cut(text, length);

            Assert.True(cut.Length <= length, $"Cut to {length} is {cut.Length} long.");
            Assert.EndsWith("…", cut, StringComparison.Ordinal);
            var kept = cut[..^1];
            Assert.True(kept.Length == 0 || !char.IsHighSurrogate(kept[^1]), $"Cut to {length} ends in half a pair.");
            Assert.StartsWith(kept, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_bound_below_one_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NotificationText.Cut("abc", 0));
    }
}
