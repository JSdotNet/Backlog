namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Which rows the header's repository scope keeps.
/// <para>
/// The scope reads the same field the row's own picker does — the entry's
/// <c>`repo:`</c> targets — because a scope that disagreed with the control beside
/// the row about which repository the row belongs to would be two answers to one
/// question. It used to read the <c>`@area`</c>, which is what put every imported
/// entry outside every scope: a plan files its entries under a pile and names the
/// repository in <c>repo:</c>.
/// </para>
/// </summary>
public class TasksRepositoryScopeTests
{
    [Fact]
    public async Task A_scope_keeps_the_rows_whose_repo_token_names_it()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs");
        var mine = await host.WriteEntryAsync("# Provision the box\n`task` `@repos` `repo:backlog`\n");
        var theirs = await host.WriteEntryAsync("# Write it up\n`task` `@repos` `repo:docs`\n");

        host.State.SetRepositoryFilter("backlog");

        Assert.Contains(mine, host.State.FilteredRows);
        Assert.DoesNotContain(theirs, host.State.FilteredRows);
    }

    [Fact]
    public async Task An_entry_targeting_two_repositories_is_in_both_scopes()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs");
        var row = await host.WriteEntryAsync("# Provision the box\n`task` `repo:backlog` `repo:docs`\n");

        // Any of its targets, not the first: an entry that says it belongs to two
        // repositories belongs to both scopes, and hiding it from one would be the
        // scope contradicting the entry's own text.
        host.State.SetRepositoryFilter("backlog");
        Assert.Contains(row, host.State.FilteredRows);

        host.State.SetRepositoryFilter("docs");
        Assert.Contains(row, host.State.FilteredRows);
    }

    [Fact]
    public async Task An_area_spelled_like_the_repository_does_not_put_a_row_in_its_scope()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var piled = await host.WriteEntryAsync("# Buy milk\n`task` `@backlog`\n");

        host.State.SetRepositoryFilter("backlog");

        // An area is the person's own pile (.domain/backlog/naming.md#area) and one
        // of them happening to be spelled like a configured repository does not make
        // it that repository's work.
        Assert.DoesNotContain(piled, host.State.FilteredRows);
    }

    /// <summary>The scope the header's chips cannot offer, because they name the
    /// repositories that exist and this names their absence. Without it an entry
    /// targeting nothing is reachable only by scoping to all repositories and
    /// reading past everything that does have one.</summary>
    [Fact]
    public async Task The_no_repository_scope_keeps_the_rows_no_repository_claims()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var piled = await host.WriteEntryAsync("# Buy milk\n`task` `@backlog`\n");
        var targeted = await host.WriteEntryAsync("# Provision the box\n`task` `repo:backlog`\n");
        await host.State.SelectAsync(null);

        host.State.SetNoRepositoryFilter(true);

        // An area is the person's own pile (.domain/backlog/naming.md#area), so one
        // spelled like a configured repository still leaves the entry claimed by no
        // repository — the same conflation An_area_spelled_like_the_repository_does_not_put_a_row_in_its_scope
        // pins from the other side.
        Assert.Contains(piled, host.State.FilteredRows);
        Assert.DoesNotContain(targeted, host.State.FilteredRows);
    }

    /// <summary>A `repo:` naming something that is not configured is not a
    /// repository the reader has: the row shows no repository badge, so the row
    /// belongs where the row says it does.</summary>
    [Fact]
    public async Task A_repo_token_naming_nothing_configured_counts_as_no_repository()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var stray = await host.WriteEntryAsync("# Buy milk\n`task` `repo:retired`\n");
        await host.State.SelectAsync(null);

        host.State.SetNoRepositoryFilter(true);

        Assert.Contains(stray, host.State.FilteredRows);
    }

    /// <summary>The two scopes ask for opposite things, and the honest answer to
    /// both at once is nothing. They are left composable rather than made exclusive
    /// because the chip carries a count: it reads 0 before it is pressed, which says
    /// so on the bar instead of in an empty list.</summary>
    [Fact]
    public async Task The_no_repository_scope_narrows_the_repository_scope_rather_than_replacing_it()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        await host.WriteEntryAsync("# Buy milk\n`task` `@backlog`\n");
        await host.WriteEntryAsync("# Provision the box\n`task` `repo:backlog`\n");
        await host.State.SelectAsync(null);

        host.State.SetRepositoryFilter("backlog");
        host.State.SetNoRepositoryFilter(true);

        Assert.Empty(host.State.FilteredRows);
        Assert.Equal("backlog", host.State.SelectedRepositoryAlias);

        // And releasing it hands the repository scope back untouched.
        host.State.SetNoRepositoryFilter(false);

        Assert.Single(host.State.FilteredRows);
    }
}
