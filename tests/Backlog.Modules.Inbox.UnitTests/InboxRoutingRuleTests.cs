using Backlog.Modules.Inbox.Abstractions;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// The routing-rule grammar the Settings field is held to: one rule per line,
/// <c>pattern =&gt; owner/name</c>, the pattern's sigil saying what it matches,
/// <c>*</c> for any run of characters and no regard to case.
/// </summary>
public sealed class InboxRoutingRuleTests
{
    [Fact]
    public void Lines_read_as_rules_in_order_skipping_blank_lines_and_repeats()
    {
        var (rules, error) = InboxRoutingRule.Parse("#design => JSdotNet/Design\n\n  @alice=>JSdotNet/Alice  \r\nyoutube => JSdotNet/Watch\n#design => JSdotNet/Design");

        Assert.Null(error);
        Assert.Equal(
            [new InboxRoutingRule("#design", "JSdotNet/Design"), new InboxRoutingRule("@alice", "JSdotNet/Alice"), new InboxRoutingRule("youtube", "JSdotNet/Watch")],
            rules);
        Assert.Equal(
            [InboxRoutingRuleTarget.Tag, InboxRoutingRuleTarget.Person, InboxRoutingRuleTarget.Source],
            rules.Select(rule => rule.Target));
    }

    [Theory]
    [InlineData("#design JSdotNet/Design", "Line 2 has no \"=>\"")]
    [InlineData("\n => JSdotNet/Design", "Line 3 has no pattern")]
    [InlineData("# => JSdotNet/Design", "Line 2 has no pattern")]
    [InlineData("#design => Design", "Line 2 names \"Design\", which is not a repository")]
    [InlineData("#design => a/b/c", "Line 2 names \"a/b/c\", which is not a repository")]
    public void A_line_that_does_not_read_is_named_and_nothing_is_kept(string text, string expected)
    {
        var (rules, error) = InboxRoutingRule.Parse("#ok => JSdotNet/Ok\n" + text);

        Assert.Empty(rules);
        Assert.NotNull(error);
        Assert.StartsWith(expected, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("design", "Design", true)]
    [InlineData("design*", "design-tokens", true)]
    [InlineData("*github.com/JSdotNet/*", "https://github.com/jsdotnet/Backlog/issues/9", true)]
    [InlineData("design", "design-tokens", false)]
    [InlineData("*tokens", "design-tokens-v2", false)]
    [InlineData("a*b*c", "a-long-b-and-c", true)]
    public void A_glob_matches_the_whole_value_ignoring_case(string glob, string value, bool expected)
    {
        Assert.Equal(expected, new InboxRoutingRule(glob, "o/r").Matches(value));
    }

    [Fact]
    public void Nothing_matches_an_empty_value()
    {
        Assert.False(new InboxRoutingRule("*", "o/r").Matches(null));
        Assert.False(new InboxRoutingRule("*", "o/r").Matches("  "));
    }

    [Fact]
    public void Rules_format_back_to_the_text_they_were_read_from()
    {
        var text = "#design => JSdotNet/Design\n@alice => JSdotNet/Alice";

        Assert.Equal(text, InboxRoutingRule.Format(InboxRoutingRule.Parse(text).Rules));
    }
}
