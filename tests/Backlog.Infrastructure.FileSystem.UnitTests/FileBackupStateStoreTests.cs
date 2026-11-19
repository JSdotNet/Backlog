namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The backup state file reading as <see cref="BackupState.None"/> whenever the
/// disk cannot give it back — absent, locked, or not the JSON it wrote — the
/// same three failures the sibling workspace stores forgive, and no others.
/// </summary>
public sealed class FileBackupStateStoreTests : IDisposable
{
    private readonly TemporaryDirectory _temp = new("backlog-backup-state-tests");

    private string _root => _temp.Combine("data");

    private string StatePath => Path.Combine(_root, "backup-state.json");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void A_missing_file_reads_as_none() =>
        Assert.Equal(BackupState.None, new FileBackupStateStore(StatePath).Current);

    [Theory]
    [InlineData("not json")]
    [InlineData("{ \"attemptedAt\": ")]
    [InlineData("{ \"committed\": \"yes\" }")]
    public void Malformed_json_reads_as_none(string content)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(StatePath, content);

        Assert.Equal(BackupState.None, new FileBackupStateStore(StatePath).Current);
    }

    [Fact]
    public void A_file_another_process_holds_locked_reads_as_none()
    {
        var at = new DateTimeOffset(2026, 9, 14, 18, 0, 0, TimeSpan.Zero);
        new FileBackupStateStore(StatePath).Save(new BackupState(at, at, true, null));

        using var held = new FileStream(StatePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.Equal(BackupState.None, new FileBackupStateStore(StatePath).Current);
    }
}
