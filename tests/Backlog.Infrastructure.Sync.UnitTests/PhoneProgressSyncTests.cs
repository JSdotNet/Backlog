using System.Net;
using System.Text.Json;

using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>Progress's edit intake, answering whatever the test said and keeping what it was offered.</summary>
internal sealed class RecordingImpedimentEdits : IImpedimentEditIntake
{
    public List<ImpedimentEditDto> Offered { get; } = [];

    public ImpedimentEditOutcome Answer { get; set; } = ImpedimentEditOutcome.Applied;

    public Task<ImpedimentEditOutcome> ApplyAsync(ImpedimentEditDto edit, CancellationToken cancellationToken = default)
    {
        Offered.Add(edit);
        return Task.FromResult(Answer);
    }
}

/// <summary>Progress's edit intake, noting how many impediment copies the merge had already
/// handed over each time it is offered an edit.</summary>
internal sealed class OrderedImpedimentEdits(RecordingImpedimentReplication impediments) : IImpedimentEditIntake
{
    public List<int> CopiesTakenBefore { get; } = [];

    public ImpedimentEditOutcome Answer { get; set; } = ImpedimentEditOutcome.Applied;

    public Task<ImpedimentEditOutcome> ApplyAsync(ImpedimentEditDto edit, CancellationToken cancellationToken = default)
    {
        CopiesTakenBefore.Add(impediments.Offered.Count);
        return Task.FromResult(Answer);
    }
}

/// <summary>Progress's stand-up context, as the test set it.</summary>
internal sealed class FixedStandUpContext : IStandUpContextSource
{
    public StandUpContextCopyDto? Copy { get; set; }

    public Task<StandUpContextCopyDto?> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Copy);
}

/// <summary>
/// The two documents the desktop exchanges with the phone on the task feed (local ADR 0035
/// §8): the <c>stand-up-context</c> it writes for the phone, and the <c>impediment-edit</c>
/// the phone writes for it — applied through Progress, then retired with a tombstone. Neither
/// is ever a task.
/// </summary>
public sealed class PhoneProgressSyncTests
{
    private static readonly Guid Phone = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ImpedimentId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset Morning = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static ImpedimentEditDto Edit(ImpedimentEditKind kind = ImpedimentEditKind.Note) =>
        new(ImpedimentId, kind, kind == ImpedimentEditKind.State ? ImpedimentState.Reported : null, null, null, "Asked twice", Morning);

    [Fact]
    public async Task An_edit_from_the_phone_goes_to_progress_and_its_tombstone_waits_for_the_push()
    {
        var tasks = new InMemoryTaskStore();
        var edits = new RecordingImpedimentEdits();
        var retirements = new ImpedimentEditRetirements();
        var activity = new SyncActivityLog();
        var change = ImpedimentEditReplicaDocuments.ToChange(Guid.NewGuid(), Edit(), "Bank sample files missing");

        var merged = await new TaskReplicaMerge(
                tasks, activity: activity, impedimentEdits: edits, retirements: retirements, time: new FakeTimeProvider(Morning.AddMinutes(10)))
            .ApplyAsync([TaskChanges.Record(change, Phone, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(1, 0), merged);
        Assert.Empty(tasks.Writes);
        Assert.Equal(Edit(), Assert.Single(edits.Offered));
        var tombstone = Assert.Single(retirements.Waiting());
        Assert.Equal(change.Id, tombstone.Id);
        Assert.Equal(Morning.AddMinutes(10), tombstone.DeletedAt);
        Assert.Equal(Morning.AddMinutes(10), tombstone.UpdatedAt);
        Assert.Equal(SyncItemKind.Impediment, Assert.Single(activity.Snapshot()).Kind);
    }

    [Theory]
    [InlineData(ImpedimentEditOutcome.Stale)]
    [InlineData(ImpedimentEditOutcome.Unchanged)]
    [InlineData(ImpedimentEditOutcome.Refused)]
    public async Task An_edit_progress_did_not_apply_is_retired_all_the_same(ImpedimentEditOutcome answer)
    {
        var retirements = new ImpedimentEditRetirements();

        var merged = await new TaskReplicaMerge(new InMemoryTaskStore(), impedimentEdits: new RecordingImpedimentEdits { Answer = answer }, retirements: retirements)
            .ApplyAsync([TaskChanges.Record(ImpedimentEditReplicaDocuments.ToChange(Guid.NewGuid(), Edit()), Phone, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 0), merged);
        Assert.Single(retirements.Waiting());
    }

    [Fact]
    public async Task An_edit_for_an_impediment_this_desktop_lacks_a_retired_edit_or_one_without_progress_stays_on_the_replica()
    {
        var retirements = new ImpedimentEditRetirements();
        var unknown = new RecordingImpedimentEdits { Answer = ImpedimentEditOutcome.Unknown };
        var change = ImpedimentEditReplicaDocuments.ToChange(Guid.NewGuid(), Edit());
        var tasks = new InMemoryTaskStore();

        await new TaskReplicaMerge(tasks, impedimentEdits: unknown, retirements: retirements)
            .ApplyAsync([TaskChanges.Record(change, Phone, 100)], Cancellation);
        await new TaskReplicaMerge(tasks, impedimentEdits: unknown, retirements: retirements)
            .ApplyAsync([TaskChanges.Record(ImpedimentEditReplicaDocuments.Retire(change, Morning.AddHours(1)), Phone, 101)], Cancellation);
        await new TaskReplicaMerge(tasks, retirements: retirements)
            .ApplyAsync([TaskChanges.Record(ImpedimentEditReplicaDocuments.ToChange(Guid.NewGuid(), Edit()), Phone, 102)], Cancellation);

        Assert.Empty(retirements.Waiting());
        Assert.Single(unknown.Offered);
        Assert.Empty(tasks.Writes);
    }

    [Fact]
    public async Task An_edit_met_before_its_impediment_is_offered_again_once_the_pull_is_done()
    {
        var retirements = new ImpedimentEditRetirements();
        var edits = new RecordingImpedimentEdits { Answer = ImpedimentEditOutcome.Unknown };
        var merge = new TaskReplicaMerge(new InMemoryTaskStore(), impedimentEdits: edits, retirements: retirements);
        var change = ImpedimentEditReplicaDocuments.ToChange(Guid.NewGuid(), Edit());

        await merge.ApplyAsync([TaskChanges.Record(change, Phone, 100)], Cancellation);
        Assert.Equal(0, await merge.RetryDeferredEditsAsync(Cancellation));
        Assert.Empty(retirements.Waiting());

        edits.Answer = ImpedimentEditOutcome.Applied;
        Assert.Equal(1, await merge.RetryDeferredEditsAsync(Cancellation));

        Assert.Equal(3, edits.Offered.Count);
        Assert.Equal(change.Id, Assert.Single(retirements.Waiting()).Id);
        Assert.Equal(0, await merge.RetryDeferredEditsAsync(Cancellation));
    }

    /// <summary>
    /// A desktop closed between applying an edit and pushing its tombstone leaves the edit
    /// live on the replica, behind the impediment it changed and any change made to it since.
    /// The desktop that meets it next meets the two in one page: the impediment's latest copy
    /// is taken first, so the edit is offered against that copy — stale or already so — and
    /// can never stamp the older value over the change made since.
    /// </summary>
    [Fact]
    public async Task An_edit_a_closed_desktop_never_retired_meets_the_latest_copy_of_its_impediment_first()
    {
        var impediments = new RecordingImpedimentReplication();
        var edits = new OrderedImpedimentEdits(impediments) { Answer = ImpedimentEditOutcome.Stale };
        var retirements = new ImpedimentEditRetirements();
        var edit = ImpedimentEditReplicaDocuments.ToChange(Guid.NewGuid(), Edit(ImpedimentEditKind.State));
        var changedSince = ImpedimentReplicaDocuments.ToChange(
            new ImpedimentReplicaCopyDto(ImpedimentId, "Bank sample files missing", """{"version":1,"title":"Bank sample files missing","history":[]}""", Morning.AddHours(2)));

        await new TaskReplicaMerge(new InMemoryTaskStore(), impediments: impediments, impedimentEdits: edits, retirements: retirements)
            .ApplyAsync([TaskChanges.Record(edit, Phone, 100), TaskChanges.Record(changedSince, Phone, 103)], Cancellation);

        Assert.Equal(1, Assert.Single(edits.CopiesTakenBefore));
        Assert.Equal(edit.Id, Assert.Single(retirements.Waiting()).Id);
    }

    /// <summary>
    /// The phone restarted with an edit sent but not applied: its next edit of the field names
    /// what the first asked (the phone keeps its sent edits, local ADR 0035 §8), so the desktop
    /// is offered the two in the order they were asked, in one page, and retires both.
    /// </summary>
    [Fact]
    public async Task Two_edits_of_one_field_from_a_restarted_phone_are_offered_in_the_order_asked()
    {
        var edits = new RecordingImpedimentEdits();
        var retirements = new ImpedimentEditRetirements();
        var first = Edit(ImpedimentEditKind.State) with { WasState = ImpedimentState.New };
        var second = first with { State = ImpedimentState.Escalated, WasState = ImpedimentState.Reported, AskedAt = Morning.AddMinutes(1) };
        var firstChange = ImpedimentEditReplicaDocuments.ToChange(Guid.NewGuid(), first);
        var secondChange = ImpedimentEditReplicaDocuments.ToChange(Guid.NewGuid(), second);

        await new TaskReplicaMerge(new InMemoryTaskStore(), impedimentEdits: edits, retirements: retirements)
            .ApplyAsync([TaskChanges.Record(firstChange, Phone, 100), TaskChanges.Record(secondChange, Phone, 101)], Cancellation);

        Assert.Equal([first, second], edits.Offered);
        Assert.Equal(2, retirements.Waiting().Count);
    }

    [Fact]
    public async Task The_stand_up_context_is_never_a_task_and_a_desktop_takes_nothing_from_it()
    {
        var tasks = new InMemoryTaskStore();
        var change = StandUpContextReplicaDocuments.ToChange(new StandUpContextCopyDto("{}", Morning));

        var merged = await new TaskReplicaMerge(tasks).ApplyAsync([TaskChanges.Record(change, Phone, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 0), merged);
        Assert.Empty(tasks.Writes);
        Assert.Equal("stand-up-context", change.Task.Type);
        Assert.Equal(StandUpContextReplicaDocuments.ContextId, change.Id);
    }

    [Fact]
    public async Task The_push_sends_the_context_once_per_change_and_every_waiting_tombstone_once()
    {
        var context = new FixedStandUpContext
        {
            Copy = new StandUpContextCopyDto(
                ProgressDocuments.WriteContext(new StandUpContextDto(Morning, [], 18m, null, IterationLength.Week, null)),
                Morning)
        };
        var retirements = new ImpedimentEditRetirements();
        var tombstone = ImpedimentEditReplicaDocuments.Retire(ImpedimentEditReplicaDocuments.ToChange(Guid.NewGuid(), Edit()), Morning.AddMinutes(1));
        retirements.Add(tombstone);
        var bodies = new List<string>();
        var (session, state) = Session(bodies, context, retirements);

        var first = await session.PushAsync(Cancellation);

        Assert.True(first.IsSuccess);
        var sent = bodies.SelectMany(body => JsonSerializer.Deserialize<PushTasksRequest>(body, Web)!.Tasks).ToList();
        var contextSent = Assert.Single(sent, change => change.Task.Type == "stand-up-context");
        Assert.Equal(context.Copy.Content, contextSent.Task.ContentMd);
        var retired = Assert.Single(sent, change => change.Task.Type == "impediment-edit");
        Assert.Equal(tombstone.Id, retired.Id);
        Assert.NotNull(retired.DeletedAt);
        Assert.Empty(retirements.Waiting());
        Assert.Equal(Morning, state.Current.DocumentWatermark("stand-up-context"));

        bodies.Clear();
        await session.PushAsync(Cancellation);
        Assert.Empty(bodies);

        context.Copy = context.Copy with { UpdatedAt = Morning.AddMinutes(5) };
        await session.PushAsync(Cancellation);
        Assert.Single(bodies);
    }

    [Fact]
    public void An_edit_travels_under_its_own_id_stamped_when_it_was_asked_and_reads_back_whole()
    {
        var id = Guid.NewGuid();
        var change = ImpedimentEditReplicaDocuments.ToChange(id, Edit(ImpedimentEditKind.State));

        Assert.Equal(id, change.Id);
        Assert.Equal("impediment-edit", change.Task.Type);
        Assert.Equal(Morning, change.UpdatedAt);
        Assert.Equal(Edit(ImpedimentEditKind.State), ImpedimentEditReplicaDocuments.ToEdit(change));

        // A desktop clock behind the phone's still retires it as the later write.
        var behind = ImpedimentEditReplicaDocuments.Retire(change, Morning.AddMinutes(-3));
        Assert.True(behind.UpdatedAt > change.UpdatedAt);
    }

    private static (TaskSyncSession Session, InMemoryTaskSyncStateStore State) Session(
        List<string> bodies,
        IStandUpContextSource context,
        ImpedimentEditRetirements retirements)
    {
        var state = new InMemoryTaskSyncStateStore();
        var tasks = new InMemoryTaskStore();
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Post && request.Content is not null)
            {
                bodies.Add(request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult());
            }

            return StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":1}""");
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };

        var session = new TaskSyncSession(
            new TaskSyncClient(http),
            new TaskReplicaMerge(tasks, retirements: retirements),
            tasks,
            state,
            new InMemoryDeviceCredentialStore(new DeviceCredential(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "Workshop PC",
                "a-registration-credential")),
            new FakeTimeProvider(Morning.AddHours(6)),
            standUpContext: context,
            retirements: retirements);

        return (session, state);
    }
}
