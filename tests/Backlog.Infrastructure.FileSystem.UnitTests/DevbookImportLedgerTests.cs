using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The mark the one-time devbook import leaves per repository once it has run to
/// the end: one file under <c>devbook-imports</c> in the storage folder, kept by
/// this device alone. A repository marked here is never imported again.
/// </summary>
public sealed class DevbookImportLedgerTests : IDisposable
{
    private const string Repository = "JSdotNet/Backlog";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "devbook-import-ledger-tests-" + Guid.NewGuid().ToString("N"));

    private readonly DateTimeOffset _at = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    public DevbookImportLedgerTests() => Directory.CreateDirectory(_root);

    private DevbookImportLedger Ledger() => new(_root);

    private string Folder => Path.Combine(_root, DevbookImportLedger.FolderName);

    [Fact]
    public void A_repository_nothing_marked_has_not_been_imported() =>
        Assert.False(Ledger().HasImported(Repository));

    [Fact]
    public void A_mark_survives_a_new_ledger_over_the_same_folder()
    {
        Ledger().MarkImported(Repository, _at);

        Assert.True(Ledger().HasImported(Repository));
        Assert.False(Ledger().HasImported("JSdotNet/devbook"));
    }

    /// <summary>An alias is matched without regard to case, as every devbook store
    /// matches one.</summary>
    [Fact]
    public void A_mark_answers_for_every_spelling_of_the_alias()
    {
        Ledger().MarkImported(Repository, _at);

        Assert.True(Ledger().HasImported("jsdotnet/backlog"));
    }

    [Fact]
    public void A_file_nobody_can_read_counts_as_no_marks()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, DevbookImportLedger.FileName), "{ not json");

        var ledger = Ledger();

        Assert.False(ledger.HasImported(Repository));
        ledger.MarkImported(Repository, _at);
        Assert.True(Ledger().HasImported(Repository));
    }

    /// <summary>The ledger follows the storage folder like the stores it records
    /// imports into: a backlog moved from Settings takes the marks with the states
    /// and remarks, so the new place does not import them a second time.</summary>
    [Fact]
    public void A_moved_backlog_takes_the_marks_along() =>
        Assert.Contains(DevbookImportLedger.FolderName, WorkspaceSettingsStore.OwnedRootFolders);

    [Fact]
    public void A_ledger_asked_of_a_moved_root_reads_the_new_place()
    {
        var root = _root;
        var ledger = new DevbookImportLedger(() => root);
        ledger.MarkImported(Repository, _at);

        root = Path.Combine(_root, "moved");
        Directory.CreateDirectory(root);

        Assert.False(ledger.HasImported(Repository));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder left behind is not a test failure.
        }
    }
}
