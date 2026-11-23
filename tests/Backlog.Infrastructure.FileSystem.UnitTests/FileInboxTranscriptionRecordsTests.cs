using Backlog.Infrastructure.FileSystem.Inbox;
using Backlog.Modules.Inbox.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>The recordings transcribed here, by sha256, in the inbox folder (local ADR 0049
/// §1): found again after a restart, replaced rather than duplicated, and never rewritten
/// over a file that could not be read. Beside them, the failure records that bound the
/// attempts at a file, in a file of their own.</summary>
public sealed class FileInboxTranscriptionRecordsTests : IDisposable
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private readonly TemporaryDirectory _temp = new("inbox-records");

    private string _folder => _temp.Combine("data");

    public void Dispose() => _temp.Dispose();

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
    public async Task A_failure_is_counted_per_sha256_and_kept_after_a_restart_with_its_last_reason()
    {
        var sha = new string('E', 64);
        var first = DateTimeOffset.UnixEpoch.AddHours(1);
        var second = first.AddMinutes(5);

        Assert.Equal(1, (await Records().CountFailureAsync(sha, "Part 1 of 2 could not be transcribed.", first, Cancellation)).Attempts);
        var counted = await Records().CountFailureAsync(sha.ToLowerInvariant(), "The summary could not be written.", second, Cancellation);

        Assert.Equal(new InboxTranscriptionFailureDto(sha.ToLowerInvariant(), 2, "The summary could not be written.", second), counted);
        Assert.Equal(counted, await Records().FindFailureAsync(sha, Cancellation));
        Assert.Null(await Records().FindFailureAsync(new string('f', 64), Cancellation));
        Assert.True(File.Exists(Path.Combine(_folder, FileInboxTranscriptionRecords.FailuresFileName)));
    }

    [Fact]
    public async Task A_cleared_failure_is_gone_and_clearing_none_changes_nothing()
    {
        var records = Records();
        var sha = new string('1', 64);
        await records.CountFailureAsync(sha, "x", DateTimeOffset.UnixEpoch, Cancellation);

        await records.ClearFailureAsync(sha, Cancellation);
        await records.ClearFailureAsync(new string('2', 64), Cancellation);

        Assert.Null(await records.FindFailureAsync(sha, Cancellation));
    }

    [Fact]
    public async Task The_latest_failure_is_the_most_recent_of_any_file()
    {
        var records = Records();
        Assert.Null(await records.LatestFailureAsync(Cancellation));

        await records.CountFailureAsync(new string('3', 64), "older", DateTimeOffset.UnixEpoch.AddHours(2), Cancellation);
        await records.CountFailureAsync(new string('4', 64), "newer", DateTimeOffset.UnixEpoch.AddHours(3), Cancellation);
        await records.CountFailureAsync(new string('5', 64), "oldest", DateTimeOffset.UnixEpoch.AddHours(1), Cancellation);

        Assert.Equal("newer", (await records.LatestFailureAsync(Cancellation))?.LastReason);
    }

    [Fact]
    public async Task A_failure_older_than_the_last_recording_transcribed_is_not_the_latest()
    {
        // A failure whose note was archived is never cleared; a later success means the
        // last transcription did not fail, whatever that record still says.
        var records = Records();
        var failedAt = DateTimeOffset.UnixEpoch.AddHours(1);
        await records.CountFailureAsync(new string('7', 64), "The deployment was not found.", failedAt, Cancellation);
        await records.SaveAsync(Record(new string('8', 64), "x") with { WrittenAt = failedAt.AddDays(1) }, Cancellation);

        Assert.Null(await records.LatestFailureAsync(Cancellation));

        await records.CountFailureAsync(new string('9', 64), "Part 1 of 1 could not be transcribed.", failedAt.AddDays(2), Cancellation);

        Assert.Equal("Part 1 of 1 could not be transcribed.", (await records.LatestFailureAsync(Cancellation))?.LastReason);
    }

    [Fact]
    public async Task An_unreadable_failures_file_finds_nothing_and_is_not_overwritten()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, FileInboxTranscriptionRecords.FailuresFileName);
        await File.WriteAllTextAsync(path, "{ not json", Cancellation);

        Assert.Null(await Records().FindFailureAsync(new string('6', 64), Cancellation));
        Assert.Null(await Records().LatestFailureAsync(Cancellation));
        await Assert.ThrowsAsync<IOException>(() => Records().CountFailureAsync(new string('6', 64), "x", DateTimeOffset.UnixEpoch, Cancellation));
        Assert.Equal("{ not json", await File.ReadAllTextAsync(path, Cancellation));
    }

    private FileInboxTranscriptionRecords Records() => new(() => _folder);

    private static InboxTranscriptionRecordDto Record(string sha, string sections) =>
        new(sha, Guid.CreateVersion7(), Guid.CreateVersion7(), sections, DateTimeOffset.UnixEpoch);
}
