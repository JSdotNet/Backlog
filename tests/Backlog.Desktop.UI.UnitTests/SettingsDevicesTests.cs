using System.Net;
using System.Text;

using Bunit;

using Backlog.Infrastructure.AzureFoundry;
using Backlog.Infrastructure.Claude;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sync;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Devices panel: what a device is, and the two ways it gets to be one.
///
/// <para>The panel drives the real <see cref="DevicePairingClient"/> against a
/// scripted service rather than a stand-in for it, because the interesting
/// behaviour is the client's as much as the screen's — that a credential is
/// stored, that a ProblemDetails code becomes a sentence somebody can act on.
/// A seam between the two would have tested neither.</para>
/// </summary>
public sealed class SettingsDevicesTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Device = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void The_devices_tab_is_not_offered_until_the_feature_is_on()
    {
        using var context = RenderSettings(devicePairingEnabled: false);

        Assert.DoesNotContain("Devices", SettingsTabs(context.Component));
    }

    [Fact]
    public void An_unpaired_device_is_offered_registering_and_pairing_but_not_a_code_to_hand_out()
    {
        using var context = RenderSettings(devicePairingEnabled: true);

        Assert.Contains("Devices", SettingsTabs(context.Component));
        OpenDevicesTab(context.Component);

        context.Component.WaitForAssertion(() =>
        {
            Assert.Single(context.Component.FindAll("[data-testid='devices-register']"));
            Assert.Single(context.Component.FindAll("[data-testid='devices-pair']"));
        });

        // Nothing to hand out: only a device that is already in may invite
        // another one, and this one has no token to ask with.
        Assert.Empty(context.Component.FindAll("[data-testid='devices-generate-code']"));
        Assert.Single(context.Component.FindAll("[data-testid='devices-empty']"));
    }

    [Fact]
    public void The_status_card_says_where_the_credential_would_be_kept_before_there_is_one()
    {
        using var context = RenderSettings(devicePairingEnabled: true);

        OpenDevicesTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-status']")));

        var status = context.Component.Find("[data-testid='devices-status']").TextContent;
        Assert.Contains(context.Credentials.StorePath, status, StringComparison.Ordinal);
    }

    [Fact]
    public void Registering_stores_the_credential_and_the_status_card_says_who_this_device_is()
    {
        using var context = RenderSettings(devicePairingEnabled: true);

        OpenDevicesTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-register']")));

        var name = context.Component.Find("[data-testid='devices-name']");
        name.Input("Workshop PC");

        context.Component.Find("[data-testid='devices-register-button']").Click();

        context.Component.WaitForAssertion(() =>
        {
            Assert.Contains("Workshop PC", context.Component.Find("[data-testid='devices-paired']").TextContent, StringComparison.Ordinal);
            Assert.Contains(Owner.ToString(), context.Component.Find("[data-testid='devices-identity']").TextContent, StringComparison.OrdinalIgnoreCase);
        });

        Assert.Equal("Workshop PC", context.Credentials.Current?.DeviceName);

        // And the panel has swapped states: a registered device offers a code
        // rather than the two ways of getting in.
        Assert.Single(context.Component.FindAll("[data-testid='devices-generate-code']"));
        Assert.Empty(context.Component.FindAll("[data-testid='devices-register']"));
        Assert.Contains("Registered", context.Component.Find("[data-testid='devices-message']").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The code is shown in the format a person reads out — two groups of four —
    /// while what the clipboard gets is the code itself, because the hyphen is
    /// not part of it.
    /// </summary>
    [Fact]
    public void Generating_a_code_shows_it_grouped_with_the_time_it_has_left()
    {
        using var context = RenderSettings(devicePairingEnabled: true, paired: true);

        OpenDevicesTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-generate-code']")));

        context.Component.Find("[data-testid='devices-generate']").Click();

        context.Component.WaitForAssertion(() =>
            Assert.Equal("K7MN-9PQR", context.Component.Find("[data-testid='devices-code-display'] code").TextContent.Trim()));

        Assert.Contains("Expires in", context.Component.Find("[data-testid='devices-code-expiry']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Pairing_sends_the_code_normalized_and_stores_what_comes_back()
    {
        using var context = RenderSettings(devicePairingEnabled: true);

        OpenDevicesTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-pair']")));

        var code = context.Component.Find("[data-testid='devices-code']");
        code.Input("k7mn-9pqr");

        context.Component.Find("[data-testid='devices-pair-button']").Click();

        context.Component.WaitForAssertion(() =>
            Assert.Contains("Paired", context.Component.Find("[data-testid='devices-message']").TextContent, StringComparison.OrdinalIgnoreCase));

        Assert.Contains("/api/sync/devices/pair", context.Service.Paths);
        Assert.Contains("\"K7MN9PQR\"", context.Service.LastBody, StringComparison.Ordinal);
        Assert.NotNull(context.Credentials.Current);
    }

    /// <summary>
    /// A code that has already let a device in comes back as a 409 with a code of
    /// its own. bUnit swallows what an event handler throws, so this asserts on
    /// the sentence the failure has to produce rather than on the absence of an
    /// exception.
    /// </summary>
    [Fact]
    public void A_code_that_was_already_used_says_so_and_leaves_the_device_unpaired()
    {
        using var context = RenderSettings(
            devicePairingEnabled: true,
            respond: (_, _) => Problem(HttpStatusCode.Conflict, SyncErrorCodes.PairingCodeUsed, "That code has already paired a device."));

        OpenDevicesTab(context.Component);
        context.Component.WaitForAssertion(() =>
            Assert.Single(context.Component.FindAll("[data-testid='devices-pair']")));

        var code = context.Component.Find("[data-testid='devices-code']");
        code.Input("K7MN9PQR");

        context.Component.Find("[data-testid='devices-pair-button']").Click();

        context.Component.WaitForAssertion(() =>
        {
            var message = context.Component.Find("[data-testid='devices-message']");
            Assert.Contains("already paired", message.TextContent, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("alert", message.GetAttribute("role"));
        });

        Assert.Null(context.Credentials.Current);
        Assert.Single(context.Component.FindAll("[data-testid='devices-pair']"));
    }

    private static string[] SettingsTabs(IRenderedComponent<Settings> component) =>
        component.FindAll(".settings-tabs button").Select(button => button.TextContent.Trim()).ToArray();

    private static void OpenDevicesTab(IRenderedComponent<Settings> component) =>
        component.FindAll(".settings-tabs button").Single(button => button.TextContent.Trim() == "Devices").Click();

    private static HttpResponseMessage Problem(HttpStatusCode status, string code, string detail) =>
        new(status)
        {
            Content = new StringContent(
                $$"""
                {"type":"https://backlog.jsdotnet.dev/problems/{{code}}","title":"Pairing failed","status":{{(int)status}},"detail":"{{detail}}","code":"{{code}}"}
                """,
                Encoding.UTF8,
                "application/problem+json")
        };

    private static SettingsRenderContext RenderSettings(
        bool devicePairingEnabled,
        bool paired = false,
        Func<HttpRequestMessage, int, HttpResponseMessage>? respond = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-settings-devices-tests", Guid.NewGuid().ToString("n"));

        var store = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(TasksFeatures.GitHubIntegration, false);
        _ = features.SetEnabled(AppFeatures.AiAssistant, false);
        _ = features.SetEnabled(AppFeatures.UsageMetrics, false);
        _ = features.SetEnabled(SyncFeatures.DevicePairing, devicePairingEnabled);

        var githubSettings = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var (repositories, _) = GitHubSettings.ParseText("JSdotNet/Backlog");
        _ = githubSettings.SetRepositories(repositories);
        var github = new GitHubIntegration(githubSettings, new StubGitHubClient(), new StubProbe());

        var credentials = paired
            ? new InMemoryDeviceCredentialStore(new DeviceCredential(Owner, Device, "Workshop PC", "a-registration-credential"))
            : new InMemoryDeviceCredentialStore();

        var service = new ScriptedSyncService(respond);
        var http = new HttpClient(service) { BaseAddress = new Uri("https://sync.test") };

        var testContext = new BunitContext();

        // CopyButton beside the pairing code reaches for the clipboard through JS.
        // Nothing here presses it, but a strict interop would fail the render.
        testContext.JSInterop.Mode = JSRuntimeMode.Loose;

        testContext.Services.AddSingleton(store);
        testContext.Services.AddSingleton<IAppFeatureSettings>(features);
        testContext.Services.AddSingleton<ITasksRefreshSettings>(
            new TasksRefreshSettingsStore(Path.Combine(root, "refresh", "refresh.json")));
        testContext.Services.AddSingleton(new AzureFoundrySettingsStore(Path.Combine(root, "azure", "azure-foundry.json")));
        testContext.Services.AddSingleton(new ClaudeSettingsStore(Path.Combine(root, "claude", "claude.json")));
        testContext.Services.AddSingleton(github);
        testContext.Services.AddSingleton<FeedbackReporter>();
        testContext.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        testContext.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(githubSettings, store));
        testContext.Services.AddSingleton<IDeviceCredentialStore>(credentials);
        testContext.Services.AddSingleton(new DevicePairingClient(http, credentials));

        var component = testContext.Render<Settings>();
        return new SettingsRenderContext(root, testContext, component, credentials, service, http);
    }

    private sealed record SettingsRenderContext(
        string Root,
        BunitContext TestContext,
        IRenderedComponent<Settings> Component,
        InMemoryDeviceCredentialStore Credentials,
        ScriptedSyncService Service,
        HttpClient Http) : IDisposable
    {
        public void Dispose()
        {
            TestContext.Dispose();
            Http.Dispose();

            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>The sync service the panel talks to: registration and pairing
    /// hand back a credential, codes hand back a code, and everything else is the
    /// device's own status.</summary>
    internal sealed class ScriptedSyncService(Func<HttpRequestMessage, int, HttpResponseMessage>? respond) : HttpMessageHandler
    {
        private int _count;

        public List<string> Paths { get; } = [];

        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            Paths.Add(path);

            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            if (respond is not null) return respond(request, _count++);

            _count++;

            if (path.EndsWith("/devices/codes", StringComparison.Ordinal))
            {
                return Json(
                    HttpStatusCode.Created,
                    $$"""{"code":"K7MN9PQR","expiresAt":"{{DateTimeOffset.UtcNow.AddMinutes(10):O}}"}""");
            }

            if (path.EndsWith("/devices/me", StringComparison.Ordinal))
            {
                return Json(
                    HttpStatusCode.OK,
                    $$"""{"ownerId":"{{Owner}}","deviceId":"{{Device}}","deviceName":"Workshop PC","pairedDeviceCount":2}""");
            }

            if (path.EndsWith("/devices/token", StringComparison.Ordinal))
            {
                return Json(
                    HttpStatusCode.OK,
                    $$"""{"accessToken":"a-token","expiresAt":"{{DateTimeOffset.UtcNow.AddMinutes(30):O}}","tokenType":"Bearer"}""");
            }

            return Json(
                HttpStatusCode.Created,
                $$"""{"ownerId":"{{Owner}}","deviceId":"{{Device}}","credential":"a-registration-credential"}""");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class StubGitHubClient : IGitHubClient
    {
        public Task<GitHubIssue> CreateIssueAsync(
            GitHubRepositoryRef repository,
            string title,
            string? body,
            IEnumerable<string>? labels = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(
            GitHubRepositoryRef repository,
            int number,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubUploadedFile> UploadFileAsync(
            GitHubRepositoryRef repository,
            string path,
            string branch,
            byte[] content,
            string commitMessage,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(false, "Not connected."));

        public void Invalidate()
        {
        }
    }
}
