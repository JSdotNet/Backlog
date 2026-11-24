using Backlog.Infrastructure.GitHub;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The alias moves the registry records beside its renames (local ADR 0060): what a
/// relabel leaves behind, so an old alias still written anywhere resolves to the row
/// it became and every alias-keyed store can follow it.
/// </summary>
public sealed class RepositoryAliasMoveTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _temp = new("repository-alias-move-tests");

    private string WorkspaceRoot => _temp.Combine("workspace");

    private GitHubSettingsStore Store(string install = "install-1", TimeProvider? clock = null) =>
        new(_temp.Combine(install, "github.json"), () => WorkspaceRoot, clock);

    public void Dispose() => _temp.Dispose();

    // --- §2: the record and Find --------------------------------------------------

    [Fact]
    public void A_registry_with_alias_moves_reads_them_back_oldest_first()
    {
        WriteRegistry("""
            {
              "updatedAt": "2026-11-24T09:00:00+00:00",
              "repositories": [ { "id": "JSdotNet/app-defaults", "alias": "app-defaults" } ],
              "aliasMoves": [
                { "from": "guidelines", "to": "project-guidelines-mcp", "id": "JSdotNet/app-defaults", "at": "2026-11-20T09:00:00+00:00" },
                { "from": "Project-Guidelines-MCP", "to": "app-defaults", "id": "JSdotNet/app-defaults", "at": "2026-11-24T09:00:00+00:00" }
              ]
            }
            """);

        var moves = Store().Current.AliasMoves;

        Assert.Equal(
        [
            new AliasMove("guidelines", "project-guidelines-mcp", "JSdotNet/app-defaults", new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero)),
            new AliasMove("project-guidelines-mcp", "app-defaults", "JSdotNet/app-defaults", Morning)
        ], moves);
    }

    [Fact]
    public void A_malformed_alias_move_is_dropped()
    {
        WriteRegistry("""
            {
              "repositories": [ { "id": "JSdotNet/app-defaults", "alias": "app-defaults" } ],
              "aliasMoves": [
                { "from": "", "to": "app-defaults", "id": "JSdotNet/app-defaults", "at": "2026-11-24T09:00:00+00:00" },
                { "from": "old", "to": "app-defaults", "id": "not-a-coordinate", "at": "2026-11-24T09:00:00+00:00" },
                { "from": "kept", "to": "app-defaults", "id": "JSdotNet/app-defaults", "at": "2026-11-24T09:00:00+00:00" }
              ]
            }
            """);

        var move = Assert.Single(Store().Current.AliasMoves);
        Assert.Equal("kept", move.OldAlias);
    }

    [Fact]
    public void The_alias_moves_survive_an_unrelated_save_and_are_absent_from_a_file_that_has_none()
    {
        var plain = Store();
        Assert.Null(plain.SetRepositories([new GitHubRepositoryRef("backlog", "JSdotNet", "Backlog")]));
        Assert.DoesNotContain("aliasMoves", File.ReadAllText(plain.RegistryPath), StringComparison.OrdinalIgnoreCase);

        WriteRegistry("""
            {
              "repositories": [ { "id": "JSdotNet/app-defaults", "alias": "app-defaults" } ],
              "aliasMoves": [ { "from": "project-guidelines-mcp", "to": "app-defaults", "id": "JSdotNet/app-defaults", "at": "2026-11-24T09:00:00+00:00" } ]
            }
            """);

        var store = Store();
        Assert.Null(store.SetRepositoryColour("app-defaults", 3));

        Assert.Contains("\"aliasMoves\"", File.ReadAllText(store.RegistryPath), StringComparison.Ordinal);
        Assert.Equal("project-guidelines-mcp", Assert.Single(Store().Current.AliasMoves).OldAlias);
    }

    [Fact]
    public void An_old_alias_resolves_through_a_chain_of_moves_to_the_row_it_became()
    {
        var settings = new GitHubSettings
        {
            Repositories = [new GitHubRepositoryRef("c", "JSdotNet", "C")],
            AliasMoves =
            [
                new AliasMove("a", "b", "JSdotNet/C", Morning),
                new AliasMove("b", "c", "JSdotNet/C", Morning.AddMinutes(1))
            ]
        };

        Assert.Equal("JSdotNet/C", settings.Find("A")!.FullName);
        Assert.Equal("JSdotNet/C", settings.Find("b")!.FullName);
        Assert.Equal("c", settings.AliasMovedTo("a"));
    }

    [Fact]
    public void The_latest_move_of_an_alias_is_the_one_followed()
    {
        var settings = new GitHubSettings
        {
            Repositories = [new GitHubRepositoryRef("first", "JSdotNet", "First"), new GitHubRepositoryRef("second", "JSdotNet", "Second")],
            AliasMoves =
            [
                new AliasMove("old", "first", "JSdotNet/First", Morning),
                new AliasMove("old", "second", "JSdotNet/Second", Morning.AddDays(1))
            ]
        };

        Assert.Equal("JSdotNet/Second", settings.Find("old")!.FullName);
    }

    [Fact]
    public void A_cycle_of_moves_ends_and_finds_nothing_when_no_row_carries_either_alias()
    {
        var settings = new GitHubSettings
        {
            Repositories = [new GitHubRepositoryRef("other", "JSdotNet", "Other")],
            AliasMoves =
            [
                new AliasMove("a", "b", "JSdotNet/Gone", Morning),
                new AliasMove("b", "a", "JSdotNet/Gone", Morning.AddMinutes(1))
            ]
        };

        Assert.Null(settings.Find("a"));
    }

    /// <summary>After a swap both aliases are carried by rows, so neither is moved
    /// anywhere: each answers with the row that carries it.</summary>
    [Fact]
    public void An_alias_a_row_carries_has_not_moved_even_when_a_move_names_it()
    {
        var settings = new GitHubSettings
        {
            Repositories = [new GitHubRepositoryRef("docs", "JSdotNet", "Backlog"), new GitHubRepositoryRef("backlog", "JSdotNet", "Docs")],
            AliasMoves =
            [
                new AliasMove("backlog", "docs", "JSdotNet/Backlog", Morning),
                new AliasMove("docs", "backlog", "JSdotNet/Docs", Morning)
            ]
        };

        Assert.Equal("backlog", settings.AliasMovedTo("backlog"));
        Assert.Equal("JSdotNet/Docs", settings.Find("backlog")!.FullName);
    }

    [Fact]
    public void A_row_that_carries_an_old_alias_wins_over_the_move()
    {
        var settings = new GitHubSettings
        {
            Repositories = [new GitHubRepositoryRef("app-defaults", "JSdotNet", "app-defaults"), new GitHubRepositoryRef("project-guidelines-mcp", "JSdotNet", "Project-Guidelines-MCP")],
            AliasMoves = [new AliasMove("project-guidelines-mcp", "app-defaults", "JSdotNet/app-defaults", Morning)]
        };

        Assert.Equal("JSdotNet/Project-Guidelines-MCP", settings.Find("project-guidelines-mcp")!.FullName);
    }

    // --- §1: when a rename relabels -------------------------------------------

    [Fact]
    public void A_rename_relabels_a_repository_whose_alias_is_its_old_name_and_records_the_move()
    {
        var clock = Clock();
        var store = Store(clock: clock);
        Assert.Null(store.SetRepositories(Parse("JSdotNet/Project-Guidelines-MCP")));

        Assert.Null(store.RenameRepository("project-guidelines-mcp", "JSdotNet/app-defaults", out var rename));

        var row = Assert.Single(store.Current.Repositories);
        Assert.Equal("app-defaults", row.Alias);
        Assert.Equal(
            new AliasMove("project-guidelines-mcp", "app-defaults", "JSdotNet/app-defaults", rename!.At),
            Assert.Single(Store().Current.AliasMoves));
        Assert.Same(row, store.Current.Find("project-guidelines-mcp"));
    }

    [Fact]
    public void A_rename_keeps_a_custom_alias_and_records_no_move()
    {
        var store = Store();
        Assert.Null(store.SetRepositories(Parse("guidelines = JSdotNet/Project-Guidelines-MCP")));

        Assert.Null(store.RenameRepository("guidelines", "JSdotNet/app-defaults", out _));

        Assert.Equal("guidelines", Assert.Single(store.Current.Repositories).Alias);
        Assert.Empty(store.Current.AliasMoves);
    }

    [Theory]
    [InlineData("Someone/Backlog")]
    [InlineData("Someone/BACKLOG")]
    public void A_change_of_owner_or_of_case_alone_records_no_move(string newName)
    {
        var store = Store();
        Assert.Null(store.SetRepositories(Parse("JSdotNet/Backlog")));

        Assert.Null(store.RenameRepository("backlog", newName, out _));

        Assert.Equal("backlog", Assert.Single(store.Current.Repositories).Alias);
        Assert.Empty(store.Current.AliasMoves);
    }

    [Fact]
    public void A_rename_onto_a_name_another_repository_answers_to_takes_the_owner_name_form()
    {
        var store = Store();
        Assert.Null(store.SetRepositories(Parse("JSdotNet/Old", "app-defaults = Other/Thing")));

        Assert.Null(store.RenameRepository("old", "JSdotNet/app-defaults", out _));

        Assert.Equal("jsdotnet-app-defaults", store.Current.Find("JSdotNet/app-defaults")!.Alias);
        Assert.Equal("Other/Thing", store.Current.Find("app-defaults")!.FullName);

        var move = Assert.Single(store.Current.AliasMoves);
        Assert.Equal(("old", "jsdotnet-app-defaults"), (move.OldAlias, move.NewAlias));
    }

    [Fact]
    public void A_merge_moves_the_renamed_alias_to_the_kept_one_even_when_it_was_custom()
    {
        var clock = Clock();
        var store = Store(clock: clock);
        Assert.Null(store.SetRepositories(Parse("finance = JSdotNet/finance", "placeholder = finance/finance")));

        Assert.Null(store.RenameRepository("placeholder", "jsdotnet/Finance", out var rename));

        Assert.Equal(
            new AliasMove("placeholder", "finance", "JSdotNet/finance", rename!.At),
            Assert.Single(store.Current.AliasMoves));
        Assert.Equal("JSdotNet/finance", store.Current.Find("placeholder")!.FullName);
    }

    // --- §4: a manual relabel -------------------------------------------------

    [Fact]
    public void Relabelling_a_row_in_the_list_records_one_move()
    {
        var clock = Clock();
        var store = Store(clock: clock);
        Assert.Null(store.SetRepositories(Parse("JSdotNet/Backlog", "JSdotNet/Docs")));

        Assert.Null(store.SetRepositories(Parse("bl = JSdotNet/Backlog", "JSdotNet/Docs")));

        Assert.Equal(new AliasMove("backlog", "bl", "JSdotNet/Backlog", Morning), Assert.Single(store.Current.AliasMoves));
    }

    [Fact]
    public void Swapping_two_aliases_records_two_moves_with_one_time()
    {
        var store = Store(clock: Clock());
        Assert.Null(store.SetRepositories(Parse("JSdotNet/Backlog", "JSdotNet/Docs")));

        Assert.Null(store.SetRepositories(Parse("docs = JSdotNet/Backlog", "backlog = JSdotNet/Docs")));

        Assert.Equal(
        [
            new AliasMove("backlog", "docs", "JSdotNet/Backlog", Morning),
            new AliasMove("docs", "backlog", "JSdotNet/Docs", Morning)
        ], store.Current.AliasMoves);
    }

    [Fact]
    public void Adding_or_removing_a_row_records_no_move()
    {
        var store = Store();
        Assert.Null(store.SetRepositories(Parse("JSdotNet/Backlog", "JSdotNet/Docs")));

        Assert.Null(store.SetRepositories(Parse("JSdotNet/Backlog", "JSdotNet/Almanac")));

        Assert.Empty(store.Current.AliasMoves);
    }

    // --- The local edit's notice ----------------------------------------------

    [Fact]
    public void An_edit_that_moves_aliases_announces_exactly_its_moves()
    {
        var store = Store(clock: Clock());
        Assert.Null(store.SetRepositories(Parse("JSdotNet/Backlog", "JSdotNet/Docs")));
        var announced = new List<IReadOnlyList<AliasMove>>();
        store.AliasesMoved += announced.Add;

        Assert.Null(store.SetRepositoryColour("backlog", 3));
        Assert.Null(store.SetRepositories(Parse("JSdotNet/Backlog", "JSdotNet/Docs")));
        Assert.Null(store.SetRepositories(Parse("bl = JSdotNet/Backlog", "JSdotNet/Docs")));
        Assert.Null(store.RenameRepository("docs", "JSdotNet/Docs-2", out _));

        Assert.Equal(2, announced.Count);
        Assert.Equal(new AliasMove("backlog", "bl", "JSdotNet/Backlog", Morning), Assert.Single(announced[0]));
        Assert.Equal(new AliasMove("docs", "docs-2", "JSdotNet/Docs-2", Morning), Assert.Single(announced[1]));
    }

    [Fact]
    public void A_reload_announces_no_move()
    {
        var store = Store();
        Assert.Null(store.SetRepositories(Parse("JSdotNet/Backlog")));
        Assert.Null(store.SetRepositories(Parse("bl = JSdotNet/Backlog")));
        var announced = 0;
        store.AliasesMoved += _ => announced++;

        store.Reload();

        Assert.Equal(0, announced);
        Assert.Single(store.Current.AliasMoves);
    }

    // --- §5: the rename already made is relabelled once -----------------------

    /// <summary>The registry a rename made before local ADR 0060 left: the row under
    /// its new id, still labelled with its old name, and the rename record.</summary>
    private const string RenamedBeforeTheDecision = """
        {
          "updatedAt": "2026-11-24T08:00:00+00:00",
          "repositories": [
            { "id": "JSdotNet/app-defaults", "alias": "project-guidelines-mcp" },
            { "id": "JSdotNet/Backlog", "alias": "backlog" }
          ],
          "renames": [ { "from": "JSdotNet/Project-Guidelines-MCP", "to": "JSdotNet/app-defaults", "at": "2026-11-24T08:00:00+00:00" } ]
        }
        """;

    [Fact]
    public void A_repository_renamed_before_the_decision_is_relabelled_to_its_new_name()
    {
        WriteRegistry(RenamedBeforeTheDecision);
        var store = Store(clock: Clock());
        var announced = new List<IReadOnlyList<AliasMove>>();
        store.AliasesMoved += announced.Add;

        var moves = store.RelabelRenamedRepositories();

        var expected = new AliasMove("project-guidelines-mcp", "app-defaults", "JSdotNet/app-defaults", Morning);
        Assert.Equal([expected], moves);
        Assert.Equal([expected], Assert.Single(announced));
        Assert.Equal("app-defaults", Store().Current.Find("JSdotNet/app-defaults")!.Alias);
        Assert.Equal("backlog", Store().Current.Find("JSdotNet/Backlog")!.Alias);
        Assert.Equal("JSdotNet/app-defaults", Store().Current.Find("project-guidelines-mcp")!.FullName);
    }

    [Fact]
    public void The_retroactive_relabel_fires_once()
    {
        WriteRegistry(RenamedBeforeTheDecision);
        Assert.Single(Store(clock: Clock()).RelabelRenamedRepositories());

        var restarted = Store(clock: Clock());
        var announced = 0;
        restarted.AliasesMoved += _ => announced++;

        Assert.Empty(restarted.RelabelRenamedRepositories());
        Assert.Equal(0, announced);
        Assert.Single(restarted.Current.AliasMoves);
    }

    /// <summary>A person who relabelled back after the pass fired has a move naming
    /// that alias, and the pass leaves their choice alone.</summary>
    [Fact]
    public void An_alias_a_move_already_names_is_not_relabelled_again()
    {
        WriteRegistry(RenamedBeforeTheDecision);
        var store = Store(clock: Clock());
        Assert.Single(store.RelabelRenamedRepositories());
        Assert.Null(store.SetRepositories(Parse("project-guidelines-mcp = JSdotNet/app-defaults", "JSdotNet/Backlog")));

        Assert.Empty(Store(clock: Clock()).RelabelRenamedRepositories());
        Assert.Equal("project-guidelines-mcp", Store().Current.Find("JSdotNet/app-defaults")!.Alias);
    }

    [Fact]
    public void A_custom_alias_and_a_row_no_rename_leads_to_are_untouched()
    {
        WriteRegistry("""
            {
              "repositories": [
                { "id": "JSdotNet/app-defaults", "alias": "guidelines" },
                { "id": "JSdotNet/Backlog", "alias": "old-backlog" }
              ],
              "renames": [ { "from": "JSdotNet/Project-Guidelines-MCP", "to": "JSdotNet/app-defaults", "at": "2026-11-24T08:00:00+00:00" } ]
            }
            """);
        var store = Store();

        Assert.Empty(store.RelabelRenamedRepositories());
        Assert.Equal(["guidelines", "old-backlog"], store.Current.Repositories.Select(r => r.Alias));
        Assert.Empty(store.Current.AliasMoves);
    }

    /// <summary>A merge made under local ADR 0060 leaves a rename record whose old
    /// name can equal the kept row's custom alias; the merge's own alias move marks
    /// that record as already followed, so §5 leaves the kept row's alias alone.</summary>
    [Fact]
    public void A_merge_made_after_the_decision_does_not_relabel_the_kept_row()
    {
        var store = Store(clock: Clock());
        Assert.Null(store.SetRepositories(Parse("backlog = JSdotNet/Backlog-app", "jsdotnet-backlog = JSdotNet/backlog")));
        Assert.Null(store.RenameRepository("jsdotnet-backlog", "JSdotNet/Backlog-app", out _));

        var restarted = Store(clock: Clock());

        Assert.Empty(restarted.RelabelRenamedRepositories());
        Assert.Equal("backlog", Assert.Single(restarted.Current.Repositories).Alias);
    }

    /// <summary>A relabel that could not be saved reports no moves, so nothing is
    /// re-keyed to an alias the next start will not know.</summary>
    [Fact]
    public void A_retroactive_relabel_whose_save_failed_reports_no_moves()
    {
        WriteRegistry(RenamedBeforeTheDecision);
        var store = Store(clock: Clock());
        var announced = 0;
        store.AliasesMoved += _ => announced++;

        // A folder where the registry file is, so the write fails.
        File.Delete(store.RegistryPath);
        Directory.CreateDirectory(store.RegistryPath);

        Assert.Empty(store.RelabelRenamedRepositories());
        Assert.Equal(0, announced);
    }

    [Fact]
    public void The_retroactive_relabel_refuses_while_the_registry_is_unreadable()
    {
        WriteRegistry("{ not json");
        var store = Store();

        Assert.Empty(store.RelabelRenamedRepositories());
        Assert.Equal("{ not json", File.ReadAllText(store.RegistryPath));
    }

    // --- Helpers --------------------------------------------------------------

    private static List<GitHubRepositoryRef> Parse(params string[] lines)
    {
        var (repositories, errors) = GitHubSettings.ParseText(string.Join('\n', lines));
        Assert.Empty(errors);
        return repositories;
    }

    private void WriteRegistry(string json)
    {
        var path = Path.Combine(WorkspaceRoot, GitHubSettingsStore.RegistryFolderName, "repos.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private static FakeTimeProvider Clock() => new(Morning);
}
