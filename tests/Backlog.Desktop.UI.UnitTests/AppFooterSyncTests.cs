using System.Net;
using System.Text;
using System.Text.Json;

using Backlog.Desktop.UI.AppUpdate;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sync;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;

using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The footer says what cloud sync last did, and opens the list of what it
/// moved. Driven through the real <see cref="TaskSyncWorker"/> and the real
/// <see cref="TaskSyncSession"/> over a scripted wire, the way the Settings
/// devices tests are: the phrase on the band is a reading of the worker, and a
/// seam between the two would test neither.
/// </summary>
public sealed class AppFooterSyncTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Device = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherDevice = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>Every installed build with sync off, every settings-only head,
    /// and every test that registers no worker: the band without a phrase.</summary>
    [Fact]
    public void A_host_that_does_not_replicate_shows_no_sync_phrase()
    {
        using var context = Context(withSync: false);

        var footer = context.Render();

        Assert.NotNull(footer.Find("[data-testid='app-footer']"));
        Assert.Empty(footer.FindAll("[data-testid='app-sync']"));
    }

    /// <summary>Quiet until the first cycle. A phrase before then would be a
    /// control opening an empty window.</summary>
    [Fact]
    public void Nothing_is_said_before_the_first_cycle()
    {
        using var context = Context(withSync: true);

        var footer = context.Render();

        Assert.Empty(footer.FindAll("[data-testid='app-sync']"));
    }

    [Fact]
    public void A_completed_cycle_is_read_out_with_both_directions()
    {
        using var context = Context(withSync: true);
        var footer = context.Render();

        context.Worker!.RequestSync();

        footer.WaitForAssertion(() =>
        {
            var phrase = footer.Find("[data-testid='app-sync']");
            Assert.Equal("synced", phrase.GetAttribute("data-sync-state"));
            Assert.StartsWith("Synced ↑1 ↓1 · ", phrase.TextContent.Trim(), StringComparison.Ordinal);
            Assert.Contains("1 sent, 1 received", phrase.GetAttribute("aria-label"), StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// The list behind the phrase: one line per document, with the direction
    /// as a word and the title as the person knows it. The task this machine
    /// pushed is sent; the task the other device wrote is received; the pull
    /// count of two — the echo came back too — is not two received lines.
    /// </summary>
    [Fact]
    public void The_window_lists_what_moved_and_which_way()
    {
        using var context = Context(withSync: true);
        var footer = context.Render();

        context.Worker!.RequestSync();
        footer.WaitForAssertion(() => Assert.Single(footer.FindAll("[data-testid='app-sync'][data-sync-state='synced']")));

        var dialog = OpenWindow(footer);
        Assert.Contains("Tasks: sent 1, received 1 of 2 pulled", dialog.QuerySelector("[data-testid='sync-activity-tasks']")!.TextContent, StringComparison.Ordinal);
        Assert.Empty(footer.FindAll("[data-testid='sync-activity-sessions']"));

        var entries = footer.FindAll("[data-testid='sync-activity-list'] li");
        Assert.Equal(2, entries.Count);

        var sent = Assert.Single(entries, entry => entry.GetAttribute("data-direction") == "sent");
        Assert.Equal("task", sent.GetAttribute("data-kind"));
        Assert.Contains("↑ Sent", sent.TextContent, StringComparison.Ordinal);
        Assert.Contains("Something to send", sent.TextContent, StringComparison.Ordinal);

        var received = Assert.Single(entries, entry => entry.GetAttribute("data-direction") == "received");
        Assert.Contains("↓ Received", received.TextContent, StringComparison.Ordinal);
        Assert.Contains("From the other machine", received.TextContent, StringComparison.Ordinal);

        // Newest first, and the pull comes after the push in a cycle.
        Assert.Equal("received", entries[0].GetAttribute("data-direction"));
    }

    [Fact]
    public void A_failed_cycle_says_so_without_shouting()
    {
        using var context = Context(
            withSync: true,
            respond: (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/tasks", StringComparison.Ordinal)
                ? Problem(HttpStatusCode.ServiceUnavailable, SyncErrorCodes.ReplicaUnavailable, "The replica is not reachable yet.")
                : Token());
        var footer = context.Render();

        context.Worker!.RequestSync();

        footer.WaitForAssertion(() =>
        {
            var phrase = footer.Find("[data-testid='app-sync']");
            Assert.Equal("failed", phrase.GetAttribute("data-sync-state"));
            Assert.Equal("Sync failed", phrase.TextContent.Trim());
            Assert.Contains("not reachable", phrase.GetAttribute("title"), StringComparison.Ordinal);
        });

        OpenWindow(footer);

        var error = footer.Find("[data-testid='sync-activity-error']");
        Assert.Contains("not reachable", error.TextContent, StringComparison.Ordinal);
        Assert.NotNull(footer.Find("[data-testid='sync-activity-none']"));
    }

    /// <summary>The button in the window is the Settings button by another
    /// door: one press, one exchange, and the phrase follows.</summary>
    [Fact]
    public void Sync_now_runs_a_cycle_from_the_window()
    {
        using var context = Context(withSync: true);
        var footer = context.Render();

        // The window is reachable only once there is a phrase, so the first
        // cycle is asked for directly; the second is the button's.
        context.Worker!.RequestSync();
        footer.WaitForAssertion(() => Assert.Single(footer.FindAll("[data-testid='app-sync'][data-sync-state='synced']")));

        var before = context.Service.Paths.Count;

        OpenWindow(footer);
        footer.Find("[data-testid='sync-activity-sync-now']").Click();

        footer.WaitForAssertion(() => Assert.True(context.Service.Paths.Count > before));
    }

    /// <summary>
    /// Clicks the phrase and waits for the window. Waits, because the render the
    /// click asks for can land a beat after the click returns: the cycle that
    /// put the phrase there raises <c>Changed</c> from the thread pool several
    /// times — once per document the log recorded, once when the worker
    /// finished — and each is a queued re-render the footer's own render has
    /// to take its turn behind.
    /// </summary>
    private static AngleSharp.Dom.IElement OpenWindow(IRenderedComponent<AppFooter> footer)
    {
        footer.Find("[data-testid='app-sync']").Click();
        footer.WaitForAssertion(() => footer.Find("[data-testid='sync-activity-dialog']"));

        return footer.Find("[data-testid='sync-activity-dialog']");
    }

    // --- Fixture ---------------------------------------------------------------

    private static FooterContext Context(
        bool withSync,
        Func<HttpRequestMessage, int, HttpResponseMessage>? respond = null)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var root = Path.Combine(Path.GetTempPath(), "backlog-footer-sync", Guid.NewGuid().ToString("n"));

        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features.json"));
        _ = features.SetEnabled(SyncFeatures.Sync, withSync);

        context.Services.AddSingleton<IAppUpdateService>(new UnsupportedAppUpdateService(currentVersion: "1.2.3"));
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton(new FeedbackReporter(
            new GitHubIntegration(
                new GitHubSettingsStore(Path.Combine(root, "github.json")),
                new SilentGitHubClient(),
                new SilentProbe())));
        context.Services.AddSingleton<FeedbackReportChannel>();

        // The one task this machine has to send, shared with the wire so the
        // page it hands back can carry that task's own echo — same id, same
        // stamp — beside another device's change.
        var mine = new TaskItem("Something to send", string.Empty, EntryType.Task);

        var service = new ScriptedSyncService(mine, respond);
        var http = new HttpClient(service) { BaseAddress = new Uri("https://sync.test") };

        if (!withSync) return new FooterContext(context, http, service, Worker: null);

        var credentials = new InMemoryDeviceCredentialStore(
            new DeviceCredential(Owner, Device, "Workshop PC", "a-registration-credential"));
        var tasks = new OneTaskRepository(mine);
        var state = new SettingsDevicesTests.ForgetfulTaskSyncStateStore(new TaskSyncState(DateTimeOffset.UnixEpoch, null));
        var activity = new SyncActivityLog();

        context.Services.AddSingleton(activity);
        context.Services.AddSingleton(_ => new TaskSyncSession(
            new TaskSyncClient(http),
            new TaskReplicaMerge(tasks, activity: activity),
            tasks,
            state,
            credentials,
            TimeProvider.System,
            activity: activity));

        var worker = new TaskSyncWorker(context.Services, features, credentials, state, new FakeTimeProvider());
        context.Services.AddSingleton(worker);

        return new FooterContext(context, http, service, worker);
    }

    private sealed record FooterContext(BunitContext Bunit, HttpClient Http, ScriptedSyncService Service, TaskSyncWorker? Worker) : IDisposable
    {
        public IRenderedComponent<AppFooter> Render() => Bunit.Render<AppFooter>();

        public void Dispose()
        {
            Worker?.Dispose();
            Bunit.Dispose();
            Http.Dispose();
        }
    }

    /// <summary>A replica that accepts whatever it is sent and hands back one
    /// page holding this device's own echo and one change from another device
    /// — the shape that tells "pulled" from "received".</summary>
    private sealed class ScriptedSyncService(TaskItem mine, Func<HttpRequestMessage, int, HttpResponseMessage>? respond) : HttpMessageHandler
    {
        private int _count;

        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            lock (Paths) Paths.Add(path);

            if (respond is not null) return Task.FromResult(respond(request, _count++));

            _count++;

            if (path.EndsWith("/devices/token", StringComparison.Ordinal)) return Task.FromResult(Token());

            if (path.EndsWith("/tasks", StringComparison.Ordinal))
            {
                return Task.FromResult(request.Method == HttpMethod.Post
                    ? Json(HttpStatusCode.OK, """{"accepted":1}""")
                    : Json(HttpStatusCode.OK, Page()));
            }

            return Task.FromResult(Json(HttpStatusCode.NotFound, "{}"));
        }

        private string Page()
        {
            var echo = TaskReplicaMerge.ToChange(mine);
            var theirs = TaskReplicaMerge.ToChange(new TaskItem("From the other machine", string.Empty, EntryType.Task));

            return JsonSerializer.Serialize(new
            {
                tasks = new[]
                {
                    new { change = echo, deviceId = Device, serverTimestamp = 100L },
                    new { change = theirs, deviceId = OtherDevice, serverTimestamp = 101L },
                },
                since = "cursor-1",
                hasMore = false,
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Token() =>
        Json(HttpStatusCode.OK, $$"""{"accessToken":"a-token","expiresAt":"{{DateTimeOffset.UtcNow.AddMinutes(30):O}}","tokenType":"Bearer"}""");

    private static HttpResponseMessage Problem(HttpStatusCode status, string code, string detail) =>
        new(status)
        {
            Content = new StringContent(
                $$"""{"type":"https://backlog.jsdotnet.dev/problems/{{code}}","title":"Sync failed","status":{{(int)status}},"detail":"{{detail}}","code":"{{code}}"}""",
                Encoding.UTF8,
                "application/problem+json")
        };

    /// <summary>One task, stamped now, so a push has exactly one thing to send —
    /// and so the echo of it on the pulled page is held rather than applied,
    /// because the merge finds the same document, same stamp, already here.</summary>
    private sealed class OneTaskRepository(TaskItem mine) : ITaskRepository
    {
        private readonly Dictionary<Guid, TaskItem> _tasks = new() { [mine.Id] = mine };

        public Task SaveAsync(TaskItem task, CancellationToken cancellationToken = default)
        {
            lock (_tasks) _tasks[task.Id] = task;
            return Task.CompletedTask;
        }

        public Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            lock (_tasks) return Task.FromResult(_tasks.TryGetValue(id, out var task) && task.DeletedAt is null ? task : null);
        }

        public Task<TaskItem?> GetIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default)
        {
            lock (_tasks) return Task.FromResult(_tasks.TryGetValue(id, out var task) ? task : null);
        }

        public Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken cancellationToken = default)
        {
            lock (_tasks) return Task.FromResult<IReadOnlyList<TaskItem>>([.. _tasks.Values.Where(task => task.DeletedAt is null)]);
        }

        public Task<IReadOnlyList<TaskItem>> ListChangedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
        {
            lock (_tasks)
            {
                return Task.FromResult<IReadOnlyList<TaskItem>>(
                    [.. _tasks.Values.Where(task => task.UpdatedAt > since).OrderBy(task => task.UpdatedAt)]);
            }
        }
    }

    private sealed class SilentGitHubClient : IGitHubClient
    {
        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssue> CreateIssueAsync(GitHubRepositoryRef repository, string title, string? body, IEnumerable<string>? labels = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(GitHubRepositoryRef repository, int number, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubUploadedFile> UploadFileAsync(GitHubRepositoryRef repository, string path, string branch, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class SilentProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(false, "Not configured."));

        public void Invalidate()
        {
        }
    }
}
