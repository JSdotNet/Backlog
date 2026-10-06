using Backlog.Infrastructure.AzureFoundry;

namespace Backlog.Infrastructure.AzureFoundry.UnitTests;

/// <summary>
/// How much of an Azure answer the chat, embeddings and cost clients quote when
/// they describe a failure. One helper for the three of them, pinned here.
/// </summary>
public sealed class FoundryMessagesTests
{
    [Fact]
    public void Surrounding_whitespace_is_trimmed()
    {
        Assert.Equal("bad request", FoundryMessages.TrimForMessage("  bad request \r\n"));
    }

    [Fact]
    public void Three_hundred_characters_come_back_unchanged()
    {
        var value = new string('x', 300);

        Assert.Equal(value, FoundryMessages.TrimForMessage(value));
    }

    [Theory]
    [InlineData(301)]
    [InlineData(5000)]
    public void Anything_longer_is_cut_to_three_hundred_and_marked(int length)
    {
        var value = new string('y', 300) + new string('z', length - 300);

        Assert.Equal(new string('y', 300) + "...", FoundryMessages.TrimForMessage(value));
    }

    [Fact]
    public void The_limit_applies_after_trimming()
    {
        var value = "   " + new string('x', 300) + "   ";

        Assert.Equal(new string('x', 300), FoundryMessages.TrimForMessage(value));
    }
}
