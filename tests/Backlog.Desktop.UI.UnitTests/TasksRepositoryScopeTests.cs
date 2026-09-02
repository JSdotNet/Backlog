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

    [Fact]
    public async Task An_area_spelled_like_a_repository_is_still_offered_as_an_area_chip()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        await host.WriteEntryAsync("# Buy milk\n`task` `@backlog`\n");

        // It used to be hidden, on the grounds that a repository is a scope rather
        // than another chip. That reasoning went with the conflation under it: this
        // is a pile called "backlog", nothing else on the row says so, and a filter
        // rebuilt from what people actually typed has no business dropping it.
        Assert.Contains(host.State.AreaFilters, option => option.Value == "backlog");
    }
}
