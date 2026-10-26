using Backlog.UI.Components.Goals;
using Backlog.UI.Components.Progress;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// What a goal's attach search reads as a pull request
/// (<c>.devbook/domain/progress/features.md#attaching-work-to-a-goal</c>): <c>#N</c>, <c>N</c>,
/// <c>owner/name#N</c> or the pull request's GitHub address. A bare number is read in the one
/// repository in scope, or in each of the repositories the host hands in.
/// </summary>
public sealed class PullRequestReferenceTests
{
    private static readonly IReadOnlyList<string> One = ["JSdotNet/Backlog"];
    private static readonly IReadOnlyList<string> Two = ["JSdotNet/Backlog", "JSdotNet/Marketplace"];

    [Theory]
    [InlineData("#1477")]
    [InlineData("1477")]
    [InlineData("  #1477  ")]
    public void A_bare_number_with_one_repository_names_the_pull_request_there(string query)
    {
        Assert.Equal([GoalSubject.ForPullRequest("JSdotNet/Backlog", 1477)], PullRequestReference.Resolve(query, One));
    }

    [Fact]
    public void A_bare_number_with_several_repositories_names_one_pull_request_in_each_in_their_order()
    {
        Assert.Equal(
            [GoalSubject.ForPullRequest("JSdotNet/Backlog", 1477), GoalSubject.ForPullRequest("JSdotNet/Marketplace", 1477)],
            PullRequestReference.Resolve("#1477", Two));
    }

    [Fact]
    public void A_bare_number_with_no_repository_names_nothing()
    {
        Assert.Empty(PullRequestReference.Resolve("#1477", []));
    }

    [Fact]
    public void A_repository_listed_twice_or_blank_is_read_once()
    {
        Assert.Equal(
            [GoalSubject.ForPullRequest("JSdotNet/Backlog", 7)],
            PullRequestReference.Resolve("7", ["JSdotNet/Backlog", " ", "jsdotnet/backlog"]));
    }

    [Theory]
    [InlineData("JSdotNet/Backlog#1477")]
    [InlineData("https://github.com/JSdotNet/Backlog/pull/1477")]
    [InlineData("http://www.github.com/JSdotNet/Backlog/pull/1477/")]
    [InlineData("https://github.com/JSdotNet/Backlog/pull/1477/files")]
    [InlineData("https://github.com/JSdotNet/Backlog/pull/1477#discussion_r1")]
    [InlineData("github.com/JSdotNet/Backlog/pull/1477")]
    public void A_named_repository_names_that_pull_request_alone(string query)
    {
        Assert.Equal([GoalSubject.ForPullRequest("JSdotNet/Backlog", 1477)], PullRequestReference.Resolve(query, Two));
    }

    [Fact]
    public void A_named_repository_need_not_be_one_the_host_lists()
    {
        // Attaching is by identity alone: the status line says what GitHub knows of it.
        Assert.Equal(
            [GoalSubject.ForPullRequest("dotnet/runtime", 12)],
            PullRequestReference.Resolve("dotnet/runtime#12", One));
    }

    [Fact]
    public void A_named_repository_the_host_lists_takes_its_spelling()
    {
        Assert.Equal(
            [GoalSubject.ForPullRequest("JSdotNet/Backlog", 3)],
            PullRequestReference.Resolve("jsdotnet/backlog#3", One));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("#")]
    [InlineData("0")]
    [InlineData("#0")]
    [InlineData("-3")]
    [InlineData("12a")]
    [InlineData("pay")]
    [InlineData("99999999999")]
    [InlineData("JSdotNet#3")]
    [InlineData("JSdotNet/Backlog#")]
    [InlineData("a/b/c#3")]
    [InlineData("https://github.com/JSdotNet/Backlog/issues/1477")]
    [InlineData("https://gitlab.com/JSdotNet/Backlog/pull/1477")]
    public void Anything_else_names_no_pull_request(string? query)
    {
        Assert.Empty(PullRequestReference.Resolve(query, Two));
    }
}
