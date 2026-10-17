using System.Net;
using System.Text.Json;

using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>Progress's impediment replication port, holding what the test set and
/// answering whatever the test said — the shape of <see cref="RecordingGoalReplication"/>.</summary>
internal sealed class RecordingImpedimentReplication : IImpedimentReplication
{
    public event Action? Changed;

    public List<ImpedimentReplicaCopyDto> Held { get; } = [];

    public List<ImpedimentReplicaCopyDto> Offered { get; } = [];

    public ImpedimentReplicaOutcome Answer { get; set; } = ImpedimentReplicaOutcome.Taken;

    public void RaiseChanged() => Changed?.Invoke();

    public Task<IReadOnlyList<ImpedimentReplicaCopyDto>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ImpedimentReplicaCopyDto>>([.. Held]);

    public Task<ImpedimentReplicaOutcome> ApplyAsync(ImpedimentReplicaCopyDto inbound, CancellationToken cancellationToken = default)
    {
        Offered.Add(inbound);
        return Task.FromResult(Answer);
    }
}

/// <summary>
/// An <c>impediment</c> document is not a task (local ADR 0035 §9): it goes to Progress's
/// impediment port whole on the pull, and leaves the machine whole on the push with a mark
/// per impediment
/// (<c>.devbook/domain/progress/requirements.md#requirement-goals-and-impediments-travel-as-one-document-each</c>).
/// </summary>
public sealed class ImpedimentSyncTests
{
    private static readonly Guid OtherPc = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Morning = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string ImpedimentJson = """{"version":1,"title":"Bank sample files missing","history":[{"state":"New","on":"2026-10-14"}]}""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static ImpedimentReplicaCopyDto Copy(string title = "Bank sample files missing", DateTimeOffset? at = null) =>
        new(Guid.NewGuid(), title, ImpedimentJson, at ?? Morning);

    [Fact]
    public async Task An_impediment_goes_to_progress_whole_and_never_to_the_task_store()
    {
        var tasks = new InMemoryTaskStore();
        var impediments = new RecordingImpedimentReplication();
        var activity = new SyncActivityLog();
        var sent = Copy();

        var merged = await new TaskReplicaMerge(tasks, activity: activity, impediments: impediments)
            .ApplyAsync([TaskChanges.Record(ImpedimentReplicaDocuments.ToChange(sent), OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(1, 0), merged);
        Assert.Empty(tasks.Writes);
        Assert.Equal(sent, Assert.Single(impediments.Offered));
        var entry = Assert.Single(activity.Snapshot());
        Assert.Equal(SyncItemKind.Impediment, entry.Kind);
        Assert.Equal("Bank sample files missing", entry.Title);
    }

    [Theory]
    [InlineData(ImpedimentReplicaOutcome.Echo, 0, 0)]
    [InlineData(ImpedimentReplicaOutcome.Refused, 0, 0)]
    [InlineData(ImpedimentReplicaOutcome.Unreadable, 0, 1)]
    public async Task An_echo_an_older_copy_or_an_unreadable_one_counts_as_the_port_said(ImpedimentReplicaOutcome answer, int applied, int skipped)
    {
        var impediments = new RecordingImpedimentReplication { Answer = answer };

        var merged = await new TaskReplicaMerge(new InMemoryTaskStore(), impediments: impediments)
            .ApplyAsync([TaskChanges.Record(ImpedimentReplicaDocuments.ToChange(Copy()), OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(applied, skipped), merged);
    }

    [Fact]
    public async Task Without_the_impediment_port_an_impediment_is_skipped_and_never_a_task()
    {
        var tasks = new InMemoryTaskStore();

        var merged = await new TaskReplicaMerge(tasks)
            .ApplyAsync([TaskChanges.Record(ImpedimentReplicaDocuments.ToChange(Copy()), OtherPc, 100)], Cancellation);

        Assert.Equal(new TaskMergeOutcome(0, 1), merged);
        Assert.Empty(tasks.Writes);
    }

    [Fact]
    public void The_kind_token_is_impediment_and_the_mark_is_keyed_per_impediment()
    {
        var copy = Copy();
        var change = ImpedimentReplicaDocuments.ToChange(copy);

        Assert.Equal("impediment", change.Task.Type);
        Assert.Equal(ImpedimentJson, change.Task.ContentMd);
        Assert.Equal($"impediment/{copy.Id:D}", ImpedimentReplicaDocuments.MarkOf(copy.Id));
        Assert.Equal(copy, ImpedimentReplicaDocuments.ToCopy(change));
    }

    [Fact]
    public async Task Each_impediment_is_pushed_whole_and_only_again_once_it_changed()
    {
        var impediments = new RecordingImpedimentReplication();
        var bank = Copy();
        impediments.Held.Add(bank);
        var state = new InMemoryTaskSyncStateStore();
        var bodies = new List<string>();
        var tasks = new InMemoryTaskStore();
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Post && request.Content is not null)
            {
                bodies.Add(request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult());
            }

            return StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"accepted":1}""");
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };
        var session = new TaskSyncSession(
            new TaskSyncClient(http),
            new TaskReplicaMerge(tasks, impediments: impediments),
            tasks,
            state,
            new InMemoryDeviceCredentialStore(new DeviceCredential(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "Workshop PC",
                "a-registration-credential")),
            new FakeTimeProvider(Morning.AddHours(6)),
            impediments: impediments);

        Assert.Equal(1, (await session.PushAsync(Cancellation)).Value.Pushed);
        var sent = Assert.Single(JsonSerializer.Deserialize<PushTasksRequest>(bodies[0], Web)!.Tasks);
        Assert.Equal(bank.Id, sent.Id);
        Assert.Equal("impediment", sent.Task.Type);
        Assert.Equal(Morning, state.Current.DocumentWatermark(ImpedimentReplicaDocuments.MarkOf(bank.Id)));

        await session.PushAsync(Cancellation);
        Assert.Single(bodies);

        impediments.Held[0] = bank with { UpdatedAt = Morning.AddMinutes(5) };
        await session.PushAsync(Cancellation);
        Assert.Equal(2, bodies.Count);
    }
}
