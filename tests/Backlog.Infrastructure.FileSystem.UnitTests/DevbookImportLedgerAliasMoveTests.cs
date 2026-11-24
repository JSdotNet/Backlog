using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The one-time devbook import's marks follow a relabelled repository (local ADR 0060
/// §3, local stores): a repository imported under its old alias is not imported again
/// under its new one. Each device keeps its own marks, so the ledger is local.
/// </summary>
public sealed class DevbookImportLedgerAliasMoveTests : IDisposable
{
    private static readonly DateTimeOffset Earlier = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _temp = new("devbook-import-ledger-alias-move-tests");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private DevbookImportLedger Ledger() => new(_temp.Path);

    private static IRepositoryAliasKeyedStore Port(DevbookImportLedger ledger) => ledger;

    private string FileOf => Path.Combine(_temp.Path, DevbookImportLedger.FolderName, DevbookImportLedger.FileName);

    public void Dispose() => _temp.Dispose();

    private static Dictionary<string, string> Moving(params (string From, string To)[] moves) =>
        moves.ToDictionary(move => move.From, move => move.To, StringComparer.Ordinal);

    [Fact]
    public void The_ledger_stays_on_this_device() =>
        Assert.Equal(AliasKeyedStoreKind.Local, Port(Ledger()).Kind);

    [Fact]
    public void It_holds_the_aliases_it_has_marked()
    {
        var ledger = Ledger();
        ledger.MarkImported("Backlog", Earlier);
        ledger.MarkImported("devbook", Earlier);

        Assert.Equal(["backlog", "devbook"], Port(Ledger()).HeldAliases().Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_repository_imported_under_its_old_alias_counts_as_imported_under_its_new_one()
    {
        var ledger = Ledger();
        ledger.MarkImported("project-guidelines-mcp", Earlier);

        var moved = await Port(ledger).MoveAliasesAsync(Moving(("project-guidelines-mcp", "app-defaults")), Cancellation);

        Assert.True(moved);
        Assert.True(ledger.HasImported("app-defaults"));
        Assert.False(ledger.HasImported("project-guidelines-mcp"));
        Assert.True(Ledger().HasImported("app-defaults"));
        Assert.False(Ledger().HasImported("project-guidelines-mcp"));
    }

    [Fact]
    public async Task Where_the_new_alias_is_already_marked_its_own_mark_wins()
    {
        var ledger = Ledger();
        ledger.MarkImported("backlog-old", Later);
        ledger.MarkImported("backlog", Earlier);

        await Port(ledger).MoveAliasesAsync(Moving(("backlog-old", "backlog")), Cancellation);

        var text = File.ReadAllText(FileOf);
        Assert.Contains("2026-11-02", text, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-11-20", text, StringComparison.Ordinal);
        Assert.DoesNotContain("backlog-old", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_swap_exchanges_the_two_marks()
    {
        var ledger = Ledger();
        ledger.MarkImported("alpha", Earlier);

        var moved = await Port(ledger).MoveAliasesAsync(Moving(("alpha", "beta"), ("beta", "alpha")), Cancellation);

        Assert.True(moved);
        Assert.True(Ledger().HasImported("beta"));
        Assert.False(Ledger().HasImported("alpha"));
    }

    [Fact]
    public async Task A_mapping_that_names_no_marked_alias_changes_nothing_and_writes_nothing()
    {
        var ledger = Ledger();
        ledger.MarkImported("backlog", Earlier);
        var bytes = File.ReadAllBytes(FileOf);
        var written = File.GetLastWriteTimeUtc(FileOf);

        var moved = await Port(ledger).MoveAliasesAsync(Moving(("devbook", "devbook-stack")), Cancellation);

        Assert.False(moved);
        Assert.Equal(bytes, File.ReadAllBytes(FileOf));
        Assert.Equal(written, File.GetLastWriteTimeUtc(FileOf));
    }

    [Fact]
    public async Task A_second_call_with_the_same_mapping_changes_nothing()
    {
        var ledger = Ledger();
        ledger.MarkImported("project-guidelines-mcp", Earlier);
        var mapping = Moving(("project-guidelines-mcp", "app-defaults"));

        Assert.True(await Port(ledger).MoveAliasesAsync(mapping, Cancellation));
        Assert.False(await Port(ledger).MoveAliasesAsync(mapping, Cancellation));
        Assert.True(Ledger().HasImported("app-defaults"));
    }
}
