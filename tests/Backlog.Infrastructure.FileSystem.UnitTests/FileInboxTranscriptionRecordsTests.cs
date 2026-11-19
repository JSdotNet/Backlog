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

    [Fact]
    public async Task A_failure_counts_its_attempts_keeps_the_last_reason_and_survives_a_restart()
    {
        var sha = new string('E', 64);
        var note = Guid.CreateVersion7();
        var file = Guid.CreateVersion7();

        await Records().RecordFailureAsync(sha, note, file, "first", DateTimeOffset.UnixEpoch, Cancellation);
        var second = await Records().RecordFailureAsync(sha, note, file, "second", DateTimeOffset.UnixEpoch.AddHours(1), Cancellation);

        Assert.Equal(2, second.Attempts);
        var found = await Records().FindFailureAsync(sha.ToLowerInvariant(), Cancellation);
        Assert.Equal(new InboxTranscriptionFailureDto(sha.ToLowerInvariant(), note, file, 2, "second", DateTimeOffset.UnixEpoch.AddHours(1)), found);
        Assert.False(found!.WaitsForRetry);
        Assert.True((await Records().RecordFailureAsync(sha, note, file, "third", DateTimeOffset.UnixEpoch, Cancellation)).WaitsForRetry);
    }

    [Fact]
    public async Task A_cleared_failure_is_gone_and_the_others_are_listed()
    {
        var records = Records();
        await records.RecordFailureAsync(new string('1', 64), Guid.CreateVersion7(), Guid.CreateVersion7(), "one", DateTimeOffset.UnixEpoch, Cancellation);
        await records.RecordFailureAsync(new string('2', 64), Guid.CreateVersion7(), Guid.CreateVersion7(), "two", DateTimeOffset.UnixEpoch, Cancellation);

        await records.ClearFailureAsync(new string('1', 64), Cancellation);

        Assert.Null(await records.FindFailureAsync(new string('1', 64), Cancellation));
        Assert.Equal(["two"], (await Records().ListFailuresAsync(Cancellation)).Select(failure => failure.Reason));
    }

    [Fact]
    public async Task Failures_live_beside_the_done_records_and_leave_their_file_as_it_was()
    {
        var sha = new string('f', 64);
        await Records().SaveAsync(Record(sha, "kept"), Cancellation);
        var done = await File.ReadAllTextAsync(Path.Combine(_folder, FileInboxTranscriptionRecords.FileName), Cancellation);

        await Records().RecordFailureAsync(new string('9', 64), Guid.CreateVersion7(), Guid.CreateVersion7(), "x", DateTimeOffset.UnixEpoch, Cancellation);

        Assert.Equal(done, await File.ReadAllTextAsync(Path.Combine(_folder, FileInboxTranscriptionRecords.FileName), Cancellation));
        Assert.True(File.Exists(Path.Combine(_folder, FileInboxTranscriptionRecords.FailuresFileName)));
        Assert.Equal("kept", (await Records().FindAsync(sha, Cancellation))?.SectionsMd);
    }

    [Fact]
    public async Task A_corrupt_failure_file_is_set_aside_and_counting_starts_again()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, FileInboxTranscriptionRecords.FailuresFileName);
        await File.WriteAllTextAsync(path, "{ not json", Cancellation);
        var sha = new string('7', 64);

        var failure = await Records().RecordFailureAsync(sha, Guid.CreateVersion7(), Guid.CreateVersion7(), "x", DateTimeOffset.UnixEpoch, Cancellation);

        Assert.Equal(1, failure.Attempts);
        Assert.Equal(1, (await Records().FindFailureAsync(sha, Cancellation))?.Attempts);
        var aside = Assert.Single(Directory.GetFiles(_folder, FileInboxTranscriptionRecords.FailuresFileName + ".corrupt-*"));
        Assert.Equal("{ not json", await File.ReadAllTextAsync(aside, Cancellation));
    }

    [Fact]
    public async Task Writes_leave_no_temporary_file_behind()
    {
        await Records().SaveAsync(Record(new string('3', 64), "x"), Cancellation);
        await Records().RecordFailureAsync(new string('4', 64), Guid.CreateVersion7(), Guid.CreateVersion7(), "x", DateTimeOffset.UnixEpoch, Cancellation);

        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Fact]
    public async Task No_failure_file_lists_and_finds_nothing()
    {
        Assert.Empty(await Records().ListFailuresAsync(Cancellation));
        Assert.Null(await Records().FindFailureAsync(new string('a', 64), Cancellation));
        await Records().ClearFailureAsync(new string('a', 64), Cancellation);
    }

    private FileInboxTranscriptionRecords Records() => new(() => _folder);

    private static InboxTranscriptionRecordDto Record(string sha, string sections) =>
        new(sha, Guid.CreateVersion7(), Guid.CreateVersion7(), sections, DateTimeOffset.UnixEpoch);
}
