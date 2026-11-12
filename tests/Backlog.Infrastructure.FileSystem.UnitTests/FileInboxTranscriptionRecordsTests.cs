using Backlog.Infrastructure.FileSystem.Inbox;
using Backlog.Modules.Inbox.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>The recordings transcribed here, by sha256, in the inbox folder (local ADR 0049
/// §1): found again after a restart, replaced rather than duplicated, and never rewritten
/// over a file that could not be read.</summary>
public sealed class FileInboxTranscriptionRecordsTests : IDisposable
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"inbox-records-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    [Fact]
    public async Task A_record_is_found_by_its_sha256_after_a_restart_whatever_its_case()
    {
        var sha = new string('A', 64);
        await Records().SaveAsync(Record(sha, "## Transcript\n\nfirst"), Cancellation);

        var found = await Records().FindAsync(sha.ToLowerInvariant(), Cancellation);

        Assert.Equal("## Transcript\n\nfirst", found?.SectionsMd);
        Assert.Null(await Records().FindAsync(new string('b', 64), Cancellation));
    }

    [Fact]
    public async Task Saving_the_same_sha256_again_replaces_the_record()
    {
        var records = Records();
        var sha = new string('c', 64);

        await records.SaveAsync(Record(sha, "one"), Cancellation);
        await records.SaveAsync(Record(sha, "two"), Cancellation);

        Assert.Equal("two", (await records.FindAsync(sha, Cancellation))?.SectionsMd);
        var text = await File.ReadAllTextAsync(Path.Combine(_folder, FileInboxTranscriptionRecords.FileName), Cancellation);
        Assert.Equal(2, text.Split("\"sha256\"").Length);
    }

    [Fact]
    public async Task An_unreadable_file_finds_nothing_and_is_not_overwritten()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, FileInboxTranscriptionRecords.FileName);
        await File.WriteAllTextAsync(path, "{ not json", Cancellation);

        Assert.Null(await Records().FindAsync(new string('d', 64), Cancellation));
        await Assert.ThrowsAsync<IOException>(() => Records().SaveAsync(Record(new string('d', 64), "x"), Cancellation));
        Assert.Equal("{ not json", await File.ReadAllTextAsync(path, Cancellation));
    }

    private FileInboxTranscriptionRecords Records() => new(() => _folder);

    private static InboxTranscriptionRecordDto Record(string sha, string sections) =>
        new(sha, Guid.CreateVersion7(), Guid.CreateVersion7(), sections, DateTimeOffset.UnixEpoch);
}
