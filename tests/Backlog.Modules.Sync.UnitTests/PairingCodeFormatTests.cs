using Backlog.Modules.Sync.Abstractions;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// The format a person reads off one screen and types into another. These are
/// the mistakes that format exists to absorb.
/// </summary>
public class PairingCodeFormatTests
{
    [Theory]
    [InlineData("abcd-efgh", "ABCDEFGH")]
    [InlineData("ABCD EFGH", "ABCDEFGH")]
    [InlineData(" 2345-6789 ", "23456789")]
    public void Normalize_strips_the_separators_a_person_types(string typed, string expected) =>
        Assert.Equal(expected, PairingCodeFormat.Normalize(typed));

    [Fact]
    public void Normalize_drops_the_letters_the_alphabet_left_out()
    {
        // 0/O, 1/I and L are not in the alphabet, so they are removed rather
        // than folded onto a lookalike: folding would make two different codes
        // hash the same way.
        Assert.Equal("ABUC", PairingCodeFormat.Normalize("A0O1IBLUC"));
    }

    [Fact]
    public void Normalize_of_nothing_is_nothing() =>
        Assert.Equal(string.Empty, PairingCodeFormat.Normalize(string.Empty));

    [Fact]
    public void The_alphabet_holds_no_lookalikes()
    {
        Assert.Equal(31, PairingCodeFormat.Alphabet.Length);
        Assert.Equal(PairingCodeFormat.Alphabet.Length, PairingCodeFormat.Alphabet.Distinct().Count());

        foreach (var excluded in "01OIL")
        {
            Assert.DoesNotContain(excluded, PairingCodeFormat.Alphabet);
        }
    }

    [Theory]
    [InlineData("23456789", true)]
    [InlineData("2345678", false)]
    [InlineData("234567890", false)]
    [InlineData("2345678O", false)]
    [InlineData("", false)]
    public void IsWellFormed_asks_only_about_shape(string normalized, bool expected) =>
        Assert.Equal(expected, PairingCodeFormat.IsWellFormed(normalized));

    [Fact]
    public void Display_chunks_a_code_into_two_groups_of_four() =>
        Assert.Equal("ABCD-EFGH", PairingCodeFormat.Display("ABCDEFGH"));

    [Fact]
    public void Display_leaves_anything_that_is_not_a_code_alone() =>
        Assert.Equal("ABC", PairingCodeFormat.Display("ABC"));

    [Fact]
    public void A_displayed_code_normalizes_back_to_itself()
    {
        const string code = "ABCDEFGH";
        Assert.Equal(code, PairingCodeFormat.Normalize(PairingCodeFormat.Display(code)));
    }
}
