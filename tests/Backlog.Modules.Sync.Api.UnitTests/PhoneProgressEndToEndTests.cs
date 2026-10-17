using System.Net.Http.Json;

using Backlog.Infrastructure.Sqlite;
using Backlog.Infrastructure.Sync;
using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.Progress;
using Backlog.Mobile.UI.Services;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.Extensions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// Goals, impediments and the stand-up reaching the phone, and the phone's impediment edits
/// reaching the desktop, end to end through this service (local ADR 0035 §8, §9,
/// <c>.devbook/domain/progress/features.md#goals-on-the-phone</c>).
/// <para>
/// Every piece is the real one: the desktop's Progress module over its own
/// <c>backlog.db</c>, its merge and session; the phone's Progress view over its own SQLite
/// file, and its outbox. Only GitHub is a stand-in: the desktop reads pull requests through
/// a fake reader, which is the point — the phone reads them without asking GitHub at all.
/// </para>
/// </summary>
public sealed class PhoneProgressEndToEndTests : IDisposable
{
    private static readonly DateOnly Due = new(2026, 10, 31);

    private readonly SyncServiceFactory _service = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "phone-progress-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _service.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>The service passes every Progress kind through untouched, beside the tasks, and
    /// the inbox listing offers none of them.</summary>
    [Fact]
    public async Task Goal_impediment_context_and_edit_documents_come_back_from_the_task_feed_whole()
    {
        var device = await _service.CreateClient().RegisteredDevice("Study desktop");
        var at = new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
        TaskChange[] documents =
        [
            Document(Guid.NewGuid(), "goal", """{"title":"Customers get paid faster","dueDate":"2026-10-31"}""", at),
            Document(Guid.NewGuid(), "impediment", """{"title":"Bank sample files missing","history":[{"state":"New","on":"2026-10-14"}]}""", at),
            Document(StandUpContextReplicaDocuments.ContextId, "stand-up-context", """{"readAt":"2026-10-19T09:00:00+00:00","pullRequests":[]}""", at),
            Document(Guid.NewGuid(), "impediment-edit", """{"impedimentId":"55555555-5555-5555-5555-555555555555","kind":"Note","note":"x","askedAt":"2026-10-19T09:00:00+00:00"}""", at),
        ];

        await device.PushChanges(documents);

        var pulled = (await device.PullTasks()).Tasks.Select(record => record.Change).ToList();
        Assert.Equal(documents.Length, pulled.Count);
        foreach (var sent in documents)
        {
            var back = Assert.Single(pulled, change => change.Id == sent.Id);
            Assert.Equal(sent.Task.Type, back.Task.Type);
            Assert.Equal(sent.Task.ContentMd, back.Task.ContentMd);
            Assert.Equal(sent.UpdatedAt, back.UpdatedAt);
        }

        var inbox = (await device.GetFromJsonAsync<List<InboxItem>>(SyncRoutes.Absolute(SyncRoutes.Inbox), Cancellation))!;
        Assert.Empty(inbox);
    }

    /// <summary>The phone duplicates the desktop's literals rather than seeing its sync client;
    /// this is where the two are held equal.</summary>
    [Fact]
    public void The_phone_and_the_desktop_agree_on_every_token_and_the_context_id()
    {
        Assert.Equal(StandUpContextReplicaDocuments.ContextId, ProgressViewProjection.StandUpContextId);
        Assert.Equal(StandUpContextReplicaDocuments.ContextType, ProgressFold.StandUpContextType);
        Assert.Equal(ImpedimentEditReplicaDocuments.EditType, ProgressFold.ImpedimentEditType);
        Assert.Equal(ImpedimentEditReplicaDocuments.EditType, ImpedimentEditOutboxKind.Token);
        Assert.Equal(GoalReplicaDocuments.GoalType, ProgressFold.GoalType);
        Assert.Equal(ImpedimentReplicaDocuments.ImpedimentType, ProgressFold.ImpedimentType);
    }

    [Fact]
    public async Task The_phone_reads_goals_impediments_pull_request_status_and_the_stand_up_without_github()
    {
        await using var devices = await PairedAsync();
        var goal = await devices.Desktop.Goals(goals => goals.CreateAsync(new SetGoalRequest("Merge without opening GitHub", null, Due), Cancellation));
        await devices.Desktop.Goals(goals => goals.AttachAsync(goal.Id, GoalItemDto.ForPullRequest("JSdotNet/Backlog", 1364), cancellationToken: Cancellation));
        var impediment = await devices.Desktop.Impediments(impediments => impediments.RaiseAsync(new RaiseImpedimentRequest("Bank sample files missing"), Cancellation));
        Assert.True((await devices.Desktop.SyncAsync()).IsSuccess);

        await devices.Phone.PullAsync(Cancellation);

        var view = Assert.Single(devices.Phone.Goals());
        Assert.Equal("Merge without opening GitHub", view.Goal.Title);
        var pull = Assert.Single(view.PullRequests);
        Assert.Equal(LinkedPullRequestState.Open, pull.State);
        Assert.Equal(12, pull.Checks!.Passed);
        Assert.Equal(14, pull.Checks.Total);
        Assert.Equal(new GoalPullRequestApprovalsDto(1, 2), pull.Approvals);
        Assert.Equal(impediment.Id, Assert.Single(devices.Phone.Impediments()).Id);

        var standUp = devices.StandUp.Read();
        var counted = Assert.Single(standUp.Goals);
        Assert.Equal(new GoalProgressDto(Done: 0, Total: 1, Doing: 1, Blocked: 0, ToDo: 0, DoneInWindow: 0), counted.Progress);
        Assert.Equal("Bank sample files missing", Assert.Single(standUp.Impediments).Title);
    }

    /// <summary>The outbox round trip: the phone's three edits leave through its outbox, the
    /// desktop applies them through its impediment features and sends the impediment back
    /// with each edit retired, and the phone reads the change. A desktop replaying the whole
    /// feed afterwards applies none of them again.</summary>
    [Fact]
    public async Task An_impediment_edited_on_the_phone_changes_on_the_desktop_and_comes_back_to_the_phone()
    {
        await using var devices = await PairedAsync();
        var impediment = await devices.Desktop.Impediments(impediments => impediments.RaiseAsync(new RaiseImpedimentRequest("Bank sample files missing"), Cancellation));
        Assert.True((await devices.Desktop.SyncAsync()).IsSuccess);
        await devices.Phone.PullAsync(Cancellation);
        await Task.Delay(20, Cancellation);

        var checkAgain = DateOnly.FromDateTime(DateTime.Today).AddDays(2);
        Assert.NotNull(await devices.Phone.MoveImpedimentAsync(impediment.Id, ImpedimentState.Reported, "bank support", Cancellation));
        Assert.NotNull(await devices.Phone.SetCheckAgainAsync(impediment.Id, checkAgain, Cancellation));
        Assert.NotNull(await devices.Phone.EditNoteAsync(impediment.Id, "Asked twice", Cancellation));
        await devices.Outbox.FlushAsync(Cancellation);
        Assert.Empty(devices.Outbox.Entries);

        Assert.True((await devices.Desktop.SyncAsync()).IsSuccess);

        var onDesktop = await devices.Desktop.Impediments(impediments => impediments.GetAsync(impediment.Id, Cancellation));
        Assert.Equal(ImpedimentState.Reported, onDesktop.State);
        Assert.Equal("bank support", onDesktop.ReportedTo);
        Assert.Equal(checkAgain, onDesktop.CheckAgain);
        Assert.Equal("Asked twice", onDesktop.Note);

        await devices.Phone.PullAsync(Cancellation);
        var onPhone = devices.Phone.FindImpediment(impediment.Id)!;
        Assert.Equal(ImpedimentState.Reported, onPhone.State);
        Assert.Equal(checkAgain, onPhone.CheckAgain);
        Assert.Equal("Asked twice", onPhone.Note);

        // Every edit is a tombstone on the replica now: a desktop reading the feed from the
        // start meets none of them live.
        var feed = (await devices.Clients[0].PullTasks()).Tasks.Select(record => record.Change).ToList();
        var edits = feed.Where(change => change.Task.Type == "impediment-edit").ToList();
        Assert.Equal(3, edits.Count);
        Assert.All(edits, edit => Assert.NotNull(edit.DeletedAt));

        var history = onDesktop.History.Count;
        Assert.True((await devices.Desktop.ReplayAsync()).IsSuccess);
        Assert.Equal(history, (await devices.Desktop.Impediments(impediments => impediments.GetAsync(impediment.Id, Cancellation))).History.Count);
    }

    private static TaskChange Document(Guid id, string type, string content, DateTimeOffset at) =>
        new(id, at, DeletedAt: null, TaskSyncClientExtensions.Payload("Progress document") with { Type = type, ContentMd = content, InMyDayOn = null });

    private async Task<Devices> PairedAsync()
    {
        var desktopHttp = _service.CreateClient();
        var registration = await desktopHttp.RegisterDevice("Study desktop");
        desktopHttp.Bearing(await desktopHttp.DeviceToken(registration));

        var code = await desktopHttp.MintPairingCode();
        var phoneHttp = _service.CreateClient();
        var paired = await phoneHttp.PostAsJsonAsync(
            SyncRoutes.Absolute(SyncRoutes.RedeemPairingCode),
            new RedeemPairingCodeRequest(code.Code, "Phone"),
            Cancellation);
        phoneHttp.Bearing(await phoneHttp.DeviceToken((await paired.Content.ReadFromJsonAsync<DeviceRegistrationResponse>(Cancellation))!));

        var desktop = new Desktop(
            Path.Combine(_root, "desktop"),
            desktopHttp,
            new DeviceCredential(registration.OwnerId, registration.DeviceId, "Study desktop", registration.Credential));

        var phoneDatabase = Path.Combine(_root, "phone", "device.db");
        var sync = new CloudSyncClient(phoneHttp);
        var outbox = new DeviceOutbox(new SqliteDeviceStore(phoneDatabase), [new ImpedimentEditOutboxKind(sync)], TimeProvider.System);
        var progress = new ProgressViewProjection(new SqliteProgressViewStore(phoneDatabase), outbox, sync, TimeProvider.System);
        var tasks = new Mobile.UI.Tasks.TaskViewProjection(new Mobile.UI.Tasks.SqliteTaskViewStore(phoneDatabase), outbox, sync, TimeProvider.System);

        return new Devices(desktop, progress, new PhoneStandUp(progress, tasks), tasks, outbox, [desktopHttp, phoneHttp]);
    }

    private sealed record Devices(
        Desktop Desktop,
        ProgressViewProjection Phone,
        PhoneStandUp StandUp,
        Mobile.UI.Tasks.TaskViewProjection PhoneTasks,
        DeviceOutbox Outbox,
        IReadOnlyList<HttpClient> Clients) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            Phone.Dispose();
            PhoneTasks.Dispose();
            Outbox.Dispose();
            Desktop.Dispose();
            foreach (var client in Clients) client.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>One desktop: its own <c>backlog.db</c> and sync state, the Progress module and
    /// its SQLite stores composed through the registrations a host calls, and a session built
    /// the way <c>AddTaskSyncClient</c> composes one.</summary>
    private sealed class Desktop : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly HttpClient _http;
        private readonly DeviceCredential _credential;
        private readonly string _root;
        private readonly SqliteTaskRepository _tasks;

        public Desktop(string root, HttpClient http, DeviceCredential credential)
        {
            Directory.CreateDirectory(root);
            _root = root;
            _http = http;
            _credential = credential;

            var services = new ServiceCollection();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IProgressTaskSource, NoTasks>();
            services.AddSingleton<IGoalPullRequestReader, OneOpenPullRequest>();
            services.AddSqlite(_ => root);
            services.AddProgressModule();
            services.AddSingleton<ImpedimentEditRetirements>();

            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            _tasks = new SqliteTaskRepository(root);
            Session = SessionAt(Path.Combine(root, "sync-state.json"));
        }

        private TaskSyncSession Session { get; }

        public async Task<T> Goals<T>(Func<IGoals, Task<Result<T>>> use)
        {
            using var scope = _provider.CreateScope();
            var result = await use(scope.ServiceProvider.GetRequiredService<IGoals>());
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
            return result.Value;
        }

        public async Task<T> Impediments<T>(Func<IImpediments, Task<Result<T>>> use)
        {
            using var scope = _provider.CreateScope();
            var result = await use(scope.ServiceProvider.GetRequiredService<IImpediments>());
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
            return result.Value;
        }

        public Task<Result<TaskSyncSummary>> SyncAsync() => Session.SyncAsync(Cancellation);

        /// <summary>A pull from the start of the feed, on a state of its own.</summary>
        public Task<Result<TaskSyncSummary>> ReplayAsync() =>
            SessionAt(Path.Combine(_root, "replay-" + Guid.NewGuid().ToString("N") + ".json")).PullAsync(Cancellation);

        public void Dispose()
        {
            _provider.Dispose();
            _http.Dispose();
        }

        private TaskSyncSession SessionAt(string statePath)
        {
            var goals = _provider.GetRequiredService<IGoalReplication>();
            var impediments = _provider.GetRequiredService<IImpedimentReplication>();
            var retirements = _provider.GetRequiredService<ImpedimentEditRetirements>();

            return new TaskSyncSession(
                new TaskSyncClient(_http),
                new TaskReplicaMerge(
                    _tasks,
                    goals: goals,
                    impediments: impediments,
                    impedimentEdits: _provider.GetRequiredService<IImpedimentEditIntake>(),
                    retirements: retirements),
                _tasks,
                new FileTaskSyncStateStore(statePath),
                new InMemoryDeviceCredentialStore(_credential),
                TimeProvider.System,
                goals: goals,
                impediments: impediments,
                standUpContext: _provider.GetRequiredService<IStandUpContextSource>(),
                retirements: retirements);
        }
    }

    private sealed class NoTasks : IProgressTaskSource
    {
        public Task<IReadOnlyList<ProgressTaskDto>> ReadAsync(ProgressTaskQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProgressTaskDto>>([]);
    }

    /// <summary>GitHub's answer, as the desktop's reader would give it: every pull request
    /// asked about is open, 12 of 14 checks passed, with one of two approvals.</summary>
    private sealed class OneOpenPullRequest : IGoalPullRequestReader
    {
        public Task<IReadOnlyList<GoalPullRequestDto>> ReadAsync(IReadOnlyList<PullRequestRefDto> pullRequests, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GoalPullRequestDto>>(
            [
                .. pullRequests.Select(pull => new GoalPullRequestDto(
                    pull.Repository, pull.Number, LinkedPullRequestState.Open, null,
                    new GoalPullRequestChecksDto(12, 0, 2, []), new GoalPullRequestApprovalsDto(1, 2)))
            ]);
    }
}
