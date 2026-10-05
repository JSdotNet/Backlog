using System.Net;
using System.Text.Json;

using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.Services;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The repository registry and the GitHub accounts leave the machine on the push,
/// after the roadmap's documents, whenever a stamp is later than the one this device
/// last had accepted for it (local ADR 0021, Decision §4) — and a device that never
/// saved one sends nothing.
/// </summary>
public sealed class TaskSyncSessionGitHubTests
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string RegistryJson = """{"repositories":[]}""";
    private const string AccountsJson = """{"accounts":[]}""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static HttpResponseMessage Accepted(int count) =>
        StubHttpMessageHandler.Json(HttpStatusCode.OK, $$"""{"accepted":{{count}}}""");

    [Fact]
    public async Task Each_saved_document_is_pushed_whole_after_the_roadmap()
    {
        var roadmap = new RecordingRoadmapReplication();
        roadmap.Held[RoadmapReplicaDocument.Plan] = new RoadmapReplicaCopyDto("""{"version":1}""", Morning);
        var github = new RecordingGitHubSettingsReplication();
        github.Held[GitHubReplicaDocument.Registry] = new GitHubReplicaCopyDto(RegistryJson, Morning.AddMinutes(1));
        github.Held[GitHubReplicaDocument.Accounts] = new GitHubReplicaCopyDto(AccountsJson, Morning.AddMinutes(2));

        using var fixture = Fixture.Create(new InMemoryTaskSyncStateStore(), github, (_, _) => Accepted(1), roadmap: roadmap);

        var result = await fixture.Session.PushAsync(Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Pushed);
        Assert.Equal(3, fixture.Bodies.Count);
        Assert.Equal("roadmap-plan", Assert.Single(Pushed(fixture.Bodies[0])).Task.Type);

        var registry = Assert.Single(Pushed(fixture.Bodies[1]));
        Assert.Equal(GitHubReplicaDocuments.RegistryId, registry.Id);
        Assert.Equal("repository-registry", registry.Task.Type);
        Assert.Equal(RegistryJson, registry.Task.ContentMd);
        Assert.Equal(Morning.AddMinutes(1), registry.UpdatedAt);
        Assert.Null(registry.DeletedAt);

        var accounts = Assert.Single(Pushed(fixture.Bodies[2]));
        Assert.Equal(GitHubReplicaDocuments.AccountsId, accounts.Id);
        Assert.Equal("github-accounts", accounts.Task.Type);
        Assert.Equal(AccountsJson, accounts.Task.ContentMd);
    }

    [Fact]
    public async Task A_pushed_document_is_not_sent_again_until_it_changes()
    {
        var github = new RecordingGitHubSettingsReplication();
        github.Held[GitHubReplicaDocument.Accounts] = new GitHubReplicaCopyDto(AccountsJson, Morning);
        var state = new InMemoryTaskSyncStateStore();

        using var fixture = Fixture.Create(state, github, (_, _) => Accepted(1));

        await fixture.Session.PushAsync(Cancellation);
        Assert.Equal(Morning, state.Current.DocumentWatermark(GitHubReplicaDocuments.AccountsType));

        await fixture.Session.PushAsync(Cancellation);
        Assert.Single(fixture.Bodies);

        github.Held[GitHubReplicaDocument.Accounts] = new GitHubReplicaCopyDto(AccountsJson, Morning.AddMinutes(5));
        await fixture.Session.PushAsync(Cancellation);

        Assert.Equal(2, fixture.Bodies.Count);
        Assert.Equal(Morning.AddMinutes(5), state.Current.DocumentWatermark(GitHubReplicaDocuments.AccountsType));
    }

    /// <summary>ADR 0021 Verification 7: a device with no registry and no accounts
    /// pushes neither document — never an empty list over another device's.</summary>
    [Fact]
    public async Task A_device_that_never_saved_either_document_sends_nothing()
    {
        using var fixture = Fixture.Create(
            new InMemoryTaskSyncStateStore(), new RecordingGitHubSettingsReplication(), (_, _) => Accepted(0));

        var result = await fixture.Session.PushAsync(Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Pushed);
        Assert.Empty(fixture.Handler.Requests);
    }

    /// <summary>The older push is refused — a short accepted count — and said so in
    /// the summary. The mark still moves past it: the replica holds a later version,
    /// which the pull brings back.</summary>
    [Fact]
    public async Task A_refused_document_is_counted_refused_and_not_offered_again()
    {
        var github = new RecordingGitHubSettingsReplication();
        github.Held[GitHubReplicaDocument.Registry] = new GitHubReplicaCopyDto(RegistryJson, Morning);
        var state = new InMemoryTaskSyncStateStore();
        var activity = new SyncActivityLog();

        using var fixture = Fixture.Create(state, github, (_, _) => Accepted(0), activity);

        var result = await fixture.Session.PushAsync(Cancellation);

        Assert.Equal(0, result.Value.Pushed);
        Assert.Equal(1, result.Value.Refused);
        Assert.Empty(activity.Snapshot());
        Assert.Equal(Morning, state.Current.DocumentWatermark(GitHubReplicaDocuments.RegistryType));
    }

    [Fact]
    public async Task A_pushed_document_is_recorded_as_a_github_settings_document_sent()
    {
        var github = new RecordingGitHubSettingsReplication();
        github.Held[GitHubReplicaDocument.Registry] = new GitHubReplicaCopyDto(RegistryJson, Morning);
        var activity = new SyncActivityLog();

        using var fixture = Fixture.Create(new InMemoryTaskSyncStateStore(), github, (_, _) => Accepted(1), activity);

        await fixture.Session.PushAsync(Cancellation);

        var entry = Assert.Single(activity.Snapshot());
        Assert.Equal(SyncDirection.Sent, entry.Direction);
        Assert.Equal(SyncItemKind.GitHubSettings, entry.Kind);
        Assert.Equal("Repository registry", entry.Title);
    }

    [Fact]
    public async Task A_failed_push_leaves_the_mark_where_it_was()
    {
        var github = new RecordingGitHubSettingsReplication();
        github.Held[GitHubReplicaDocument.Registry] = new GitHubReplicaCopyDto(RegistryJson, Morning);
        var state = new InMemoryTaskSyncStateStore();

        using var fixture = Fixture.Create(state, github,
            (_, _) => StubHttpMessageHandler.Problem(HttpStatusCode.ServiceUnavailable, "sync.replica_unavailable", "later"));

        var result = await fixture.Session.PushAsync(Cancellation);

        Assert.True(result.IsFailure);
        Assert.Null(state.Current.DocumentWatermark(GitHubReplicaDocuments.RegistryType));
    }

    /// <summary>A session whose merge has no GitHub settings port — the older build —
    /// counts both kinds Skipped, writes no task, and still moves the cursor past the
    /// page.</summary>
    [Fact]
    public async Task Without_the_port_a_pull_skips_both_kinds_and_still_moves_the_cursor()
    {
        var tasks = new InMemoryTaskStore();
        var state = new InMemoryTaskSyncStateStore();
        var page = JsonSerializer.Serialize(
            new PullTasksResponse(
                [
                    new TaskChangeRecord(GitHubReplicaChanges.Registry(RegistryJson, Morning), Guid.NewGuid(), 10),
                    new TaskChangeRecord(GitHubReplicaChanges.Accounts(AccountsJson, Morning), Guid.NewGuid(), 11),
                ],
                "cursor-after-the-page",
                HasMore: false),
            Web);

        using var fixture = Fixture.Create(state, github: null,
            (_, _) => StubHttpMessageHandler.Json(HttpStatusCode.OK, page), tasks: tasks);

        var result = await fixture.Session.PullAsync(Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Pulled);
        Assert.Equal(0, result.Value.Applied);
        Assert.Equal(2, result.Value.Skipped);
        Assert.Empty(tasks.Writes);
        Assert.Equal("cursor-after-the-page", state.Current.PullCursor);
    }

    private static IReadOnlyList<TaskChange> Pushed(string body) =>
        JsonSerializer.Deserialize<PushTasksRequest>(body, Web)!.Tasks;

    /// <summary><c>TaskSyncSessionRoadmapTests.Fixture</c>, with the GitHub settings
    /// port threaded through the merge and the session alike.</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _http;

        private Fixture(HttpClient http, StubHttpMessageHandler handler, TaskSyncSession session, List<string> bodies)
        {
            _http = http;
            Handler = handler;
            Session = session;
            Bodies = bodies;
        }

        public StubHttpMessageHandler Handler { get; }

        public TaskSyncSession Session { get; }

        public List<string> Bodies { get; }

        public static Fixture Create(
            ITaskSyncStateStore state,
            IGitHubSettingsReplication? github,
            Func<HttpRequestMessage, int, HttpResponseMessage> respond,
            SyncActivityLog? activity = null,
            IRoadmapReplication? roadmap = null,
            InMemoryTaskStore? tasks = null)
        {
            tasks ??= new InMemoryTaskStore();
            var bodies = new List<string>();

            var handler = new StubHttpMessageHandler((request, index) =>
            {
                if (request.Method == HttpMethod.Post && request.Content is not null)
                {
                    bodies.Add(request.Content.ReadAsStringAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult());
                }

                return respond(request, index);
            });

            var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };

            var session = new TaskSyncSession(
                new TaskSyncClient(http),
                new TaskReplicaMerge(tasks, roadmap: roadmap, github: github),
                tasks,
                state,
                new InMemoryDeviceCredentialStore(new DeviceCredential(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    "Workshop PC",
                    "a-registration-credential")),
                new FakeTimeProvider(Morning.AddHours(6)),
                activity: activity,
                roadmap: roadmap,
                github: github);

            return new Fixture(http, handler, session, bodies);
        }

        public void Dispose() => _http.Dispose();
    }
}
