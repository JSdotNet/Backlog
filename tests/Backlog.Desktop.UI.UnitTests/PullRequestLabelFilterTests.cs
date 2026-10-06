using Backlog.Desktop.UI.PullRequests;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Which pull requests a repository's label filter lets onto the list: any of the
/// filter's labels is enough, labels compare without regard to case, no filter lets
/// everything through, and a pinned pull request is never filtered out.
/// </summary>
public sealed class PullRequestLabelFilterTests
{
    [Fact]
    public void A_repository_without_a_filter_lists_every_pull_request()
    {
        Assert.True(PullRequestLabelFilter.Passes(["anything"], filter: [], pinned: false));
        Assert.True(PullRequestLabelFilter.Passes([], filter: [], pinned: false));
    }

    [Fact]
    public void A_pull_request_carrying_any_of_the_labels_is_listed()
    {
        Assert.True(PullRequestLabelFilter.Passes(["docs", "backend"], filter: ["frontend", "backend"], pinned: false));
    }

    [Fact]
    public void Labels_compare_without_regard_to_case()
    {
        Assert.True(PullRequestLabelFilter.Passes(["FrontEnd"], filter: ["frontend"], pinned: false));
    }

    [Fact]
    public void A_pull_request_carrying_none_of_the_labels_is_left_out()
    {
        Assert.False(PullRequestLabelFilter.Passes(["docs"], filter: ["frontend"], pinned: false));
        Assert.False(PullRequestLabelFilter.Passes([], filter: ["frontend"], pinned: false));
    }

    [Fact]
    public void A_pinned_pull_request_is_listed_whatever_its_labels()
    {
        Assert.True(PullRequestLabelFilter.Passes(["docs"], filter: ["frontend"], pinned: true));
    }

    /// <summary>A label that only starts like a filter label is a different label.</summary>
    [Fact]
    public void A_label_matches_whole_not_by_prefix()
    {
        Assert.False(PullRequestLabelFilter.Passes(["frontend-legacy"], filter: ["frontend"], pinned: false));
    }
}
