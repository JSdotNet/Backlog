using Backlog.Modules.Dashboard.Abstractions;

namespace Backlog.Modules.Dashboard.UnitTests;

/// <summary>
/// The repository focus: the shell's scope as the dashboard sees it. What matters
/// is that it compares by value — the parts decide whether to re-fetch by comparing
/// scopes — and that the anchor and the order survive while duplicates and blanks
/// do not.
/// </summary>
public sealed class RepositoryFocusTests
{
    [Fact]
    public void Nothing_in_focus_is_all_and_contains_everything()
    {
        Assert.True(RepositoryFocus.All.IsAll);
        Assert.Null(RepositoryFocus.All.Anchor);
        Assert.Empty(RepositoryFocus.All.Aliases);
        Assert.True(RepositoryFocus.All.Contains("backlog"));
        Assert.True(RepositoryFocus.All.Contains(null));
        Assert.Equal("*", RepositoryFocus.All.Key);
        Assert.Equal(string.Empty, RepositoryFocus.All.Label);
    }

    [Fact]
    public void Blanks_and_duplicates_drop_and_the_order_taken_is_kept()
    {
        var focus = RepositoryFocus.Of("docs", " ", "backlog", "Docs", null);

        Assert.Equal(["docs", "backlog"], focus.Aliases);
        Assert.Equal("docs", focus.Anchor);
        Assert.Equal("docs, backlog", focus.Label);
        Assert.False(focus.IsAll);
    }

    [Fact]
    public void Only_blanks_is_all()
    {
        Assert.Same(RepositoryFocus.All, RepositoryFocus.Of("", "  ", null));
        Assert.Same(RepositoryFocus.All, RepositoryFocus.Of([]));
    }

    [Fact]
    public void Membership_ignores_case_and_refuses_the_unnamed()
    {
        var focus = RepositoryFocus.Of("backlog");

        Assert.True(focus.Contains("Backlog"));
        Assert.False(focus.Contains("docs"));
        Assert.False(focus.Contains(null));
    }

    /// <summary>
    /// Two focuses on the same repositories are one focus, whatever order they were
    /// taken in and however they were cased — because a scope built from them has
    /// to be equal, or every part re-fetches for a change that is not one.
    /// </summary>
    [Fact]
    public void Equality_is_by_repositories_not_by_order_or_case()
    {
        var left = RepositoryFocus.Of("backlog", "docs");
        var right = RepositoryFocus.Of("Docs", "BACKLOG");

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.Equal(left.Key, right.Key);
        Assert.NotEqual(left, RepositoryFocus.Of("backlog"));
        Assert.NotEqual(left, RepositoryFocus.All);

        // And therefore the record around it compares by value too.
        Assert.Equal(new DashboardScope(left), new DashboardScope(right));
        Assert.NotEqual(new DashboardScope(left), DashboardScope.Default);
    }

    [Fact]
    public void A_scope_built_with_nothing_reads_all_repositories()
    {
        Assert.True(new DashboardScope().IsAllRepositories);
        Assert.True(new DashboardScope(Repositories: null).IsAllRepositories);
        Assert.Same(RepositoryFocus.All, DashboardScope.Default.Repositories);
    }
}
