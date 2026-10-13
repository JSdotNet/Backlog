using Backlog.Infrastructure.GitHub;

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

        // An area is the person's own pile (.devbook/domain/tasks/domain.md#area) and
        // one of them happening to be spelled like a configured repository does not
        // make it that repository's work.
        Assert.DoesNotContain(piled, host.State.FilteredRows);
    }

    // --- The Brain ---------------------------------------------------------------
    //
    // The scope of everything filed against no repository (local ADR 0027): a member
    // of the same ordered set, held as the reserved empty value, so the anchor, Ctrl
    // and the remembered order apply to it as they do to a repository.

    /// <summary>Brain alone keeps the rows no repository claims. Without it an entry
    /// targeting nothing is reachable only by scoping to all repositories and reading
    /// past everything that does have one.</summary>
    [Fact]
    public async Task The_brain_alone_keeps_the_rows_no_repository_claims()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var piled = await host.WriteEntryAsync("# Buy milk\n`task` `@backlog`\n");
        var targeted = await host.WriteEntryAsync("# Provision the box\n`task` `repo:backlog`\n");
        await host.State.SelectAsync(null);

        host.State.SetBrainFilter();

        Assert.True(host.State.BrainInScope);
        Assert.True(host.State.BrainIsAnchor);
        Assert.Equal([BrainScope.Value], host.State.SelectedRepositoryAliases);
        Assert.Equal(string.Empty, host.State.AnchorRepositoryAlias);

        // An area is the person's own pile (.devbook/domain/tasks/domain.md#area), so
        // one spelled like a configured repository still leaves the entry claimed by
        // no repository — the same conflation An_area_spelled_like_the_repository_does_not_put_a_row_in_its_scope
        // pins from the other side.
        Assert.Contains(piled, host.State.FilteredRows);
        Assert.DoesNotContain(targeted, host.State.FilteredRows);

        // RowsInRepositoryScope, what In progress draws from, is the same answer.
        Assert.Equal(host.State.ScopedRows, host.State.RowsInRepositoryScope);
        Assert.DoesNotContain(targeted, host.State.RowsInRepositoryScope);
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

        host.State.SetBrainFilter();

        Assert.Contains(stray, host.State.FilteredRows);
    }

    /// <summary>Ctrl adds the Brain beside a repository as it adds a repository: the
    /// union of the two, with the repository pressed first still the anchor. It used
    /// to be a separate chip that narrowed the repository scope, and the two pressed
    /// together asked for nothing.</summary>
    [Fact]
    public async Task The_brain_beside_a_repository_keeps_both_and_the_anchor_stays()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs");
        var unfiled = await host.WriteEntryAsync("# Buy milk\n`task`\n");
        var mine = await host.WriteEntryAsync("# Provision the box\n`task` `repo:backlog`\n");
        var theirs = await host.WriteEntryAsync("# Write it up\n`task` `repo:docs`\n");
        await host.State.SelectAsync(null);

        host.State.SetRepositoryFilter("backlog");
        host.State.ToggleBrainInScope();

        Assert.Equal(["backlog", BrainScope.Value], host.State.SelectedRepositoryAliases);
        Assert.Equal("backlog", host.State.AnchorRepositoryAlias);
        Assert.False(host.State.BrainIsAnchor);
        Assert.Contains(unfiled, host.State.FilteredRows);
        Assert.Contains(mine, host.State.FilteredRows);
        Assert.DoesNotContain(theirs, host.State.FilteredRows);

        // And Ctrl again takes it back out.
        host.State.ToggleBrainInScope();

        Assert.Equal(["backlog"], host.State.SelectedRepositoryAliases);
        Assert.DoesNotContain(unfiled, host.State.FilteredRows);
    }

    /// <summary>A plain press on the Brain replaces whatever the scope held, as a plain
    /// press on a repository does.</summary>
    [Fact]
    public async Task The_brain_pressed_alone_replaces_the_repositories()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs");

        host.State.SetRepositoryFilter("backlog");
        host.State.ToggleRepositoryInScope("docs");
        host.State.SetBrainFilter();

        Assert.Equal([BrainScope.Value], host.State.SelectedRepositoryAliases);
    }

    /// <summary>The Brain names no repository, so removing repositories from Settings
    /// never takes it out of the scope — the pass that forgets a stale alias resolves
    /// each against Settings, and the Brain's empty value resolves to nothing.</summary>
    [Fact]
    public async Task The_brain_is_never_a_stale_alias()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");

        host.State.SetBrainFilter();
        host.State.ToggleRepositoryInScope("backlog");

        Assert.Null(host.GitHub.Settings.SetRepositories([]));
        host.State.SetReadyInPlanFilter(false);

        Assert.Equal([BrainScope.Value], host.State.SelectedRepositoryAliases);
    }

    /// <summary>A new entry under an anchor of Brain is filed nowhere: it already
    /// belongs to the Brain, so it stays in view without a <c>`repo:`</c>.</summary>
    [Fact]
    public async Task A_new_entry_under_the_brain_is_filed_against_no_repository()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");

        host.State.SetBrainFilter();
        host.State.ToggleRepositoryInScope("backlog");
        host.State.NewRow();

        Assert.DoesNotContain("`repo:", host.State.SelectedRow?.RawText ?? string.Empty);
    }

    /// <summary>Revealing a row the Brain hides widens the scope, as it does for a row
    /// a repository hides.</summary>
    [Fact]
    public async Task Revealing_a_repository_row_lets_go_of_the_brain()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var targeted = await host.WriteEntryAsync("# Provision the box\n`task` `repo:backlog`\n");
        await host.State.SelectAsync(null);
        host.State.SetBrainFilter();

        Assert.True(host.State.Reveal(targeted));

        Assert.Empty(host.State.SelectedRepositoryAliases);
        Assert.Contains(targeted, host.State.FilteredRows);
    }

    /// <summary>A remembered scope comes back with the Brain in the place it was stored
    /// in — the anchor when it was first — and an alias no longer configured dropped
    /// beside it (ADR 0027, §5).</summary>
    [Fact]
    public async Task A_remembered_brain_comes_back_in_its_place()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");

        host.State.RestoreRepositoryScope([BrainScope.Value, "retired", "backlog"]);

        Assert.Equal([BrainScope.Value, "backlog"], host.State.SelectedRepositoryAliases);
        Assert.True(host.State.BrainIsAnchor);

        host.State.RestoreRepositoryScope(["backlog", BrainScope.Value]);

        Assert.Equal(["backlog", BrainScope.Value], host.State.SelectedRepositoryAliases);
        Assert.False(host.State.BrainIsAnchor);

        host.State.RestoreRepositoryScope([BrainScope.Value, "retired"]);

        Assert.Equal([BrainScope.Value], host.State.SelectedRepositoryAliases);
    }

    // --- More than one repository at once ------------------------------------------
    //
    // The scope is an ordered set. A plain press replaces it with one repository —
    // or empties it, when that repository was the whole set — which is the single
    // select the strip always had. A modifier press adds or removes one. The first
    // entry is the anchor: the one the Devbook pane reads, because the pane can
    // read one repository's folders and not several, and the repository the reader
    // was already looking at is the one that should not move when they add another.

    [Fact]
    public async Task Toggling_a_second_repository_into_the_scope_keeps_both_repositories_rows()
    {
        using var host = await TasksPaneHost.CreateAsync(
            "backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs", "site = JSdotNet/Site");
        var mine = await host.WriteEntryAsync("# Provision the box\n`task` `repo:backlog`\n");
        var theirs = await host.WriteEntryAsync("# Write it up\n`task` `repo:docs`\n");
        var elsewhere = await host.WriteEntryAsync("# Publish it\n`task` `repo:site`\n");

        host.State.SetRepositoryFilter("backlog");
        host.State.ToggleRepositoryInScope("docs");

        Assert.Equal(["backlog", "docs"], host.State.SelectedRepositoryAliases);
        Assert.Contains(mine, host.State.FilteredRows);
        Assert.Contains(theirs, host.State.FilteredRows);
        Assert.DoesNotContain(elsewhere, host.State.FilteredRows);
    }

    [Fact]
    public async Task The_first_repository_stays_the_anchor_while_others_join()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs");

        host.State.SetRepositoryFilter("backlog");
        host.State.ToggleRepositoryInScope("docs");

        Assert.Equal("backlog", host.State.AnchorRepositoryAlias);
    }

    [Fact]
    public async Task Toggling_the_anchor_out_hands_the_anchor_to_the_next_repository()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs");

        host.State.SetRepositoryFilter("backlog");
        host.State.ToggleRepositoryInScope("docs");
        host.State.ToggleRepositoryInScope("backlog");

        Assert.Equal(["docs"], host.State.SelectedRepositoryAliases);
        Assert.Equal("docs", host.State.AnchorRepositoryAlias);
    }

    [Fact]
    public async Task Toggling_the_last_repository_out_empties_the_scope()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var row = await host.WriteEntryAsync("# Provision the box\n`task` `repo:backlog`\n");
        var piled = await host.WriteEntryAsync("# Buy milk\n`task`\n");

        host.State.ToggleRepositoryInScope("backlog");
        Assert.DoesNotContain(piled, host.State.FilteredRows);

        host.State.ToggleRepositoryInScope("backlog");

        Assert.Empty(host.State.SelectedRepositoryAliases);
        Assert.Equal(string.Empty, host.State.AnchorRepositoryAlias);
        Assert.Contains(row, host.State.FilteredRows);
        Assert.Contains(piled, host.State.FilteredRows);
    }

    [Fact]
    public async Task A_plain_selection_replaces_the_whole_set()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs");

        host.State.SetRepositoryFilter("backlog");
        host.State.ToggleRepositoryInScope("docs");
        host.State.SetRepositoryFilter("docs");

        Assert.Equal(["docs"], host.State.SelectedRepositoryAliases);
    }

    [Fact]
    public async Task Toggling_an_alias_nothing_is_configured_under_changes_nothing()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");

        host.State.SetRepositoryFilter("backlog");
        host.State.ToggleRepositoryInScope("retired");

        Assert.Equal(["backlog"], host.State.SelectedRepositoryAliases);
    }

    /// <summary>A repository removed from Settings leaves the scope on its own; the
    /// others stay. Dropping the whole set for one missing member would throw away a
    /// selection the reader still has chips on screen for.</summary>
    [Fact]
    public async Task A_repository_removed_from_settings_leaves_the_scope_alone()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs");
        await host.WriteEntryAsync("# Provision the box\n`task` `repo:backlog`\n");

        host.State.SetRepositoryFilter("backlog");
        host.State.ToggleRepositoryInScope("docs");

        var (remaining, _) = GitHubSettings.ParseText("backlog = JSdotNet/Backlog");
        Assert.Null(host.GitHub.Settings.SetRepositories(remaining));
        host.State.SetReadyInPlanFilter(false);

        Assert.Equal(["backlog"], host.State.SelectedRepositoryAliases);
    }

    [Fact]
    public async Task A_new_entry_is_filed_under_the_anchor()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs");

        host.State.SetRepositoryFilter("backlog");
        host.State.ToggleRepositoryInScope("docs");
        host.State.NewRow();

        Assert.Contains("`repo:backlog`", host.State.SelectedRow?.RawText);
        Assert.DoesNotContain("`repo:docs`", host.State.SelectedRow?.RawText);
    }

    /// <summary>Several repositories at once is a list affordance: the only reader
    /// that can show more than one is the backlog list. The shell asks for this
    /// when that list leaves the screen, so the scope goes back to the one
    /// repository every remaining reader can take.</summary>
    [Fact]
    public async Task Narrowing_to_the_anchor_keeps_the_first_repository_and_drops_the_rest()
    {
        using var host = await TasksPaneHost.CreateAsync(
            "backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs", "site = JSdotNet/Site");
        var theirs = await host.WriteEntryAsync("# Write it up\n`task` `repo:docs`\n");

        host.State.SetRepositoryFilter("backlog");
        host.State.ToggleRepositoryInScope("docs");
        host.State.ToggleRepositoryInScope("site");

        host.State.NarrowRepositoryScopeToAnchor();

        Assert.Equal(["backlog"], host.State.SelectedRepositoryAliases);
        Assert.DoesNotContain(theirs, host.State.FilteredRows);
    }

    [Fact]
    public async Task Narrowing_a_scope_of_one_or_none_changes_nothing()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog");
        var changes = 0;
        host.State.Changed += () => changes++;

        host.State.NarrowRepositoryScopeToAnchor();
        Assert.Empty(host.State.SelectedRepositoryAliases);

        host.State.SetRepositoryFilter("backlog");
        var before = changes;
        host.State.NarrowRepositoryScopeToAnchor();

        Assert.Equal(["backlog"], host.State.SelectedRepositoryAliases);
        Assert.Equal(before, changes);
    }

    // --- Restoring a remembered scope ----------------------------------------------
    //
    // The shell remembers the scope across a restart (shell-navigation.json) and
    // hands it back in one call. One call rather than a toggle per alias, because
    // every toggle filters the list again and announces a change: a restore is one
    // decision and is announced once.

    [Fact]
    public async Task A_restored_scope_keeps_its_order_so_the_anchor_comes_back_first()
    {
        using var host = await TasksPaneHost.CreateAsync(
            "backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs", "site = JSdotNet/Site");
        var theirs = await host.WriteEntryAsync("# Write it up\n`task` `repo:docs`\n");
        var elsewhere = await host.WriteEntryAsync("# Publish it\n`task` `repo:site`\n");

        host.State.RestoreRepositoryScope(["docs", "backlog"]);

        Assert.Equal(["docs", "backlog"], host.State.SelectedRepositoryAliases);
        Assert.Equal("docs", host.State.AnchorRepositoryAlias);
        Assert.Contains(theirs, host.State.FilteredRows);
        Assert.DoesNotContain(elsewhere, host.State.FilteredRows);
    }

    /// <summary>The file outlives Settings: a repository removed since the scope
    /// was written leaves it, and the rest stay in their order. When nothing is
    /// left the scope is every repository, which is what an unremembered scope
    /// reads as.</summary>
    [Fact]
    public async Task A_restored_alias_no_longer_configured_is_dropped_and_the_rest_keep_their_order()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs");

        host.State.RestoreRepositoryScope(["retired", "docs", "backlog"]);

        Assert.Equal(["docs", "backlog"], host.State.SelectedRepositoryAliases);

        host.State.RestoreRepositoryScope(["retired"]);

        Assert.Empty(host.State.SelectedRepositoryAliases);
    }

    [Fact]
    public async Task A_restored_alias_takes_its_configured_spelling_and_a_repeat_is_kept_once()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs");

        host.State.RestoreRepositoryScope(["Backlog", "docs", "backlog"]);

        Assert.Equal(["backlog", "docs"], host.State.SelectedRepositoryAliases);
    }

    [Fact]
    public async Task A_restore_is_announced_once_and_restoring_the_scope_already_held_announces_nothing()
    {
        using var host = await TasksPaneHost.CreateAsync(
            "backlog = JSdotNet/Backlog", "docs = JSdotNet/Docs", "site = JSdotNet/Site");
        var changes = 0;
        host.State.Changed += () => changes++;

        host.State.RestoreRepositoryScope(["backlog", "docs", "site"]);
        Assert.Equal(1, changes);

        host.State.RestoreRepositoryScope(["backlog", "docs", "site"]);
        Assert.Equal(1, changes);

        host.State.RestoreRepositoryScope([]);
        Assert.Empty(host.State.SelectedRepositoryAliases);
        Assert.Equal(2, changes);
    }
}
