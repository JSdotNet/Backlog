using System.Text.Json.Nodes;

using Backlog.Infrastructure.FileSystem.Progress;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Replication;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// What the two stamped settings files share — the iteration settings (local ADR 0036,
/// §1–§3) and the My Day preferences (local ADR 0032, §1–§2): every change stamped past the
/// one held, and the same file as the document that travels, replaced whole by a later copy
/// and left alone by an echo or an older copy. What each file's values are, and the events
/// each raises, are <see cref="IterationSettingsStoreTests"/>' and
/// <see cref="MyDayPreferencesStoreTests"/>'.
/// </summary>
public sealed class StampedSettingsStoreTests : IDisposable
{
    private const string Iteration = "iteration-settings";
    private const string MyDay = "my-day-preferences";

    private static readonly DateTimeOffset Morning = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _temp = new("stamped-settings-tests");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string> Kinds => [Iteration, MyDay];

    public void Dispose() => _temp.Dispose();

    /// <summary>
    /// One stamped settings file behind a common face: two values a person can set — the
    /// first, and a second unlike it — the document a copy of each travels as, and the
    /// keys a new file is written with, in the order they are written.
    /// </summary>
    private sealed class Subject
    {
        public required string Path { get; init; }

        public required Func<string?> SetFirst { get; init; }

        public required Func<string?> SetSecond { get; init; }

        public required Func<object?> Value { get; init; }

        public required Func<object?> ReopenedValue { get; init; }

        public required object First { get; init; }

        public required object Second { get; init; }

        /// <summary>A copy of the first (false) or the second (true) value, at a stamp,
        /// with any extra JSON members.</summary>
        public required Func<bool, DateTimeOffset, string, string> Json { get; init; }

        public required Func<Task<(string Content, DateTimeOffset UpdatedAt)?>> Read { get; init; }

        public required Func<string, DateTimeOffset, Task<ReplicaOutcome>> Apply { get; init; }

        public required Action<Action> OnSyncChange { get; init; }

        public required string[] NewFileKeys { get; init; }
    }

    private Subject Of(string kind, string device = "a", TimeProvider? time = null)
    {
        time ??= new FakeTimeProvider(Morning);

        switch (kind)
        {
            case Iteration:
            {
                var path = _temp.Combine("data", device, IterationSettingsStore.FileName);
                var store = new IterationSettingsStore(path, time);
                IIterationSettingsReplication replica = store;

                return new Subject
                {
                    Path = path,
                    SetFirst = () => store.Set("two-weeks", new DateOnly(2026, 10, 19)),
                    SetSecond = () => store.Set("month", null),
                    Value = () => store.Choice,
                    ReopenedValue = () => new IterationSettingsStore(path, time).Choice,
                    First = new IterationChoice("two-weeks", new DateOnly(2026, 10, 19)),
                    Second = new IterationChoice("month", null),
                    Json = (second, at, extra) => second
                        ? $$"""{"updatedAt":"{{at:O}}","length":"month"{{extra}}}"""
                        : $$"""{"updatedAt":"{{at:O}}","length":"two-weeks","twoWeekStart":"2026-10-19"{{extra}}}""",
                    Read = async () => await replica.ReadAsync(Cancellation) is { } copy ? (copy.Content, copy.UpdatedAt) : null,
                    Apply = (content, at) => replica.ApplyAsync(new IterationSettingsCopyDto(content, at), Cancellation),
                    OnSyncChange = handler => replica.Changed += handler,
                    NewFileKeys = ["updatedAt", "length", "twoWeekStart"],
                };
            }

            case MyDay:
            {
                var path = _temp.Combine("data", device, MyDayPreferencesStore.FileName);
                var store = new MyDayPreferencesStore(path, time);
                IMyDayPreferencesReplication replica = store;

                return new Subject
                {
                    Path = path,
                    SetFirst = () => store.SetClearsOvernight(false),
                    SetSecond = () => store.SetClearsOvernight(true),
                    Value = () => store.ClearsOvernight,
                    ReopenedValue = () => new MyDayPreferencesStore(path, time).ClearsOvernight,
                    First = false,
                    Second = true,
                    Json = (second, at, extra) =>
                        $$"""{"updatedAt":"{{at:O}}","clearsOvernight":{{(second ? "true" : "false")}}{{extra}}}""",
                    Read = async () => await replica.ReadAsync(Cancellation) is { } copy ? (copy.Content, copy.UpdatedAt) : null,
                    Apply = (content, at) => replica.ApplyAsync(new MyDayPreferencesCopyDto(content, at), Cancellation),
                    OnSyncChange = handler => replica.Changed += handler,
                    NewFileKeys = ["updatedAt", "clearsOvernight"],
                };
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    // --- The file ------------------------------------------------------------------

    /// <summary>ADR 0036 §4 and ADR 0032 §1: a device that never set the file has no
    /// document to send, and nothing is written for it.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task With_no_file_there_is_nothing_to_send(string kind)
    {
        var subject = Of(kind);

        Assert.Null(await subject.Read());
        Assert.False(File.Exists(subject.Path));
    }

    /// <summary>The stamp is written first and the values after it — the shape every
    /// earlier build wrote, so a file written now reads the same to them.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_new_file_is_written_stamp_first_in_the_shape_it_always_had(string kind)
    {
        var subject = Of(kind);

        subject.SetFirst();

        var written = JsonNode.Parse(File.ReadAllText(subject.Path))!.AsObject();
        Assert.Equal(subject.NewFileKeys, written.Select(pair => pair.Key));
        Assert.Equal(Morning, (DateTimeOffset)written["updatedAt"]!);
        Assert.Equal(subject.First, subject.ReopenedValue());
    }

    /// <summary>A file an earlier build wrote — its own spelling, a key this build does not
    /// know — reads back as the same value and travels as the same text, at its stamp.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_file_written_before_reads_back_the_same(string kind)
    {
        var path = Of(kind).Path;
        var stored = Of(kind).Json(false, Morning, ""","fromANewerBuild":1""").Replace(",", ",\n  ", StringComparison.Ordinal);
        File.WriteAllText(path, stored);

        var subject = Of(kind);

        Assert.Equal(subject.First, subject.Value());
        Assert.Equal((stored, Morning), await subject.Read());
    }

    /// <summary>Two changes under a clock that has not moved are still two versions, the
    /// later one stamped past the earlier.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Every_change_is_stamped_past_the_one_before_under_a_frozen_clock(string kind)
    {
        var subject = Of(kind);

        subject.SetFirst();
        var first = (await subject.Read())!.Value.UpdatedAt;

        subject.SetSecond();
        var second = (await subject.Read())!.Value.UpdatedAt;

        Assert.Equal(Morning, first);
        Assert.True(second > first);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_file_that_cannot_be_written_keeps_the_change_for_now_and_says_so(string kind)
    {
        // A folder where the file should be: every write is refused.
        Directory.CreateDirectory(Of(kind).Path);
        var subject = Of(kind);

        var message = subject.SetFirst();

        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.Equal(subject.First, subject.Value());
    }

    // --- The document that travels -------------------------------------------------

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task The_document_is_the_file_as_stored_with_its_stamp(string kind)
    {
        var subject = Of(kind);
        subject.SetFirst();

        var copy = (await subject.Read())!.Value;

        Assert.Equal(File.ReadAllText(subject.Path), copy.Content);
        Assert.Equal(Morning, copy.UpdatedAt);
    }

    /// <summary>ADR 0036 and ADR 0032 Verification 2: a later stamp is taken whole, at the
    /// inbound stamp, keys this build does not know included — and the sync does not hear
    /// of it, so no push follows a pull.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_later_copy_is_taken_whole_and_not_announced_to_the_sync(string kind)
    {
        var subject = Of(kind);
        subject.SetFirst();
        var sync = 0;
        subject.OnSyncChange(() => sync++);
        var later = Morning.AddMinutes(10);

        var outcome = await subject.Apply(subject.Json(true, later, ""","fromANewerBuild":"kept" """), later);

        Assert.Equal(ReplicaOutcome.Taken, outcome);
        Assert.Equal(subject.Second, subject.Value());
        Assert.Equal(0, sync);

        var written = JsonNode.Parse(File.ReadAllText(subject.Path))!;
        Assert.Equal("kept", (string)written["fromANewerBuild"]!);
        Assert.Equal(later, (DateTimeOffset)written["updatedAt"]!);
        Assert.Equal(subject.Second, subject.ReopenedValue());
    }

    /// <summary>A newly paired device that never set the file takes the other device's
    /// value.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_device_with_no_file_takes_any_copy(string kind)
    {
        var subject = Of(kind);

        var outcome = await subject.Apply(subject.Json(false, Morning, ""), Morning);

        Assert.Equal(ReplicaOutcome.Taken, outcome);
        Assert.Equal(subject.First, subject.Value());
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_copy_at_the_stamp_already_held_is_an_echo_and_writes_nothing(string kind)
    {
        var subject = Of(kind);
        subject.SetFirst();
        var before = File.ReadAllText(subject.Path);

        var outcome = await subject.Apply(subject.Json(true, Morning, ""), Morning);

        Assert.Equal(ReplicaOutcome.Echo, outcome);
        Assert.Equal(before, File.ReadAllText(subject.Path));
        Assert.Equal(subject.First, subject.Value());
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task An_older_copy_is_refused(string kind)
    {
        var subject = Of(kind);
        subject.SetFirst();
        var before = File.ReadAllText(subject.Path);
        var earlier = Morning.AddMinutes(-1);

        var outcome = await subject.Apply(subject.Json(true, earlier, ""), earlier);

        Assert.Equal(ReplicaOutcome.Refused, outcome);
        Assert.Equal(before, File.ReadAllText(subject.Path));
        Assert.Equal(subject.First, subject.Value());
    }

    /// <summary>A change made after taking a copy from a PC whose clock runs ahead stamps
    /// past it, so it is the later version rather than one the replica refuses.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_change_after_taking_a_copy_from_the_future_stamps_past_it(string kind)
    {
        var subject = Of(kind);
        var ahead = Morning.AddHours(3);
        await subject.Apply(subject.Json(false, ahead, ""), ahead);

        subject.SetSecond();

        Assert.True((await subject.Read())!.Value.UpdatedAt > ahead);
    }
}
