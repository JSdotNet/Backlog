using System.Net;

using Backlog.Desktop.UI.Tasks;
using Backlog.Infrastructure.Sync;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.UI;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Sessions.UI;
using Backlog.Modules.Sessions.UI.Extensions;
using Backlog.Modules.Sync.UI;
using Backlog.Modules.Tasks.Abstractions.Connectors;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

using static Backlog.Desktop.UI.UnitTests.LinkedTaskPaneTests;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The badge sources the module Settings pages name: Inbox counts its routing
/// rules, Connectors its connected task sources, Model prices its prices, and
/// Devices says whether this device is paired. Each follows the setting behind it
/// and is registered, as a singleton, by the call that registers its page.
/// </summary>
public sealed class ModuleSettingsBadgeSourceTests
{
    [Fact]
    public void Inbox_counts_its_routing_rules_and_follows_them()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-badge-tests", Guid.NewGuid().ToString("n"));

        try
        {
            var rules = new InboxRoutingRulesStore(Path.Combine(root, "inbox-routing-rules.json"));
            var source = new InboxRoutingRulesBadgeSource(rules);
            var raised = 0;
            source.Changed += () => raised++;

            Assert.Equal("0", source.Badge);

            Assert.Null(rules.SetRules("#bug => JSdotNet/Bugs\n#docs => JSdotNet/Docs"));

            Assert.Equal("2", source.Badge);
            Assert.True(raised > 0);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Connectors_counts_the_connected_task_sources_and_follows_them()
    {
        var targets = new InMemoryTargets(new ConnectedTarget("github", "owner/one"));
        var source = new ConnectedTaskSourcesBadgeSource(new LinkedTaskSources([], targets));
        var raised = 0;
        source.Changed += () => raised++;

        Assert.Equal("1", source.Badge);

        Assert.Null(targets.Save(new ConnectedTarget("github", "owner/two")));
        Assert.Equal("2", source.Badge);

        Assert.Null(targets.Remove("github", "owner/one"));
        Assert.Null(targets.Remove("github", "owner/two"));
        Assert.Equal("0", source.Badge);
        Assert.Equal(3, raised);
    }

    [Fact]
    public async Task Model_prices_counts_the_prices_once_read_and_follows_each_save()
    {
        var store = new StubPriceStore(
            new ModelPriceTable([new ModelPrice("opus", 5m, 25m, 0.5m, 6.25m), new ModelPrice("sonnet", 3m, 15m, null, null)]));
        var source = new ModelPriceBadgeSource(store);
        var changed = new TaskCompletionSource();
        source.Changed += () => changed.TrySetResult();

        // The store is read asynchronously, so the first look starts the read.
        _ = source.Badge;
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("2", source.Badge);

        var raised = 0;
        source.Changed += () => raised++;
        source.Report(0);

        Assert.Equal("0", source.Badge);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Model_prices_moves_when_the_page_saves_a_price()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IModelPriceStore>(new StubPriceStore(ModelPriceTable.Empty));
        context.Services.AddSessionsSettings();
        var source = context.Services.GetRequiredService<ModelPriceBadgeSource>();

        var page = context.Render<ModelPriceSettings>();
        page.WaitForAssertion(() => Assert.Equal("0", source.Badge));

        page.Find("[data-testid='model-price-add']").Click();
        page.Find("input[data-testid='model-price-model']").Change("claude-opus-5-5");

        Assert.Equal("1", source.Badge);
    }

    [Fact]
    public void Devices_says_whether_this_device_is_paired_and_follows_the_credential()
    {
        var credentials = new InMemoryDeviceCredentialStore();
        var source = new DevicePairingBadgeSource(credentials);
        var raised = 0;
        source.Changed += () => raised++;

        Assert.Equal(DevicePairingBadgeSource.NotPaired, source.Badge);

        credentials.Save(new DeviceCredential(Guid.NewGuid(), Guid.NewGuid(), "Workshop PC", "a-credential"));
        Assert.Equal(DevicePairingBadgeSource.Paired, source.Badge);

        credentials.Clear();
        Assert.Equal(DevicePairingBadgeSource.NotPaired, source.Badge);
        Assert.Equal(2, raised);
    }

    [Fact]
    public async Task Devices_reads_a_credential_the_service_refused_as_not_paired()
    {
        var credentials = new InMemoryDeviceCredentialStore(
            new DeviceCredential(Guid.NewGuid(), Guid.NewGuid(), "Workshop PC", "a-revoked-credential"));
        var services = new ServiceCollection();
        services.AddHttpClient(SyncTokenProvider.HttpClientName, client => client.BaseAddress = new Uri("https://sync.test"))
            .ConfigurePrimaryHttpMessageHandler(() => new Refusing());
        using var provider = services.BuildServiceProvider();
        using var tokens = new SyncTokenProvider(provider.GetRequiredService<IHttpClientFactory>(), credentials, TimeProvider.System);
        var source = new DevicePairingBadgeSource(credentials, tokens);
        var raised = 0;
        source.Changed += () => raised++;

        Assert.Equal(DevicePairingBadgeSource.Paired, source.Badge);

        Assert.Null(await tokens.GetAccessTokenAsync(TestContext.Current.CancellationToken));

        Assert.Equal(DevicePairingBadgeSource.NotPaired, source.Badge);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void A_host_that_keeps_none_of_the_settings_draws_no_badge()
    {
        Assert.Null(new InboxRoutingRulesBadgeSource().Badge);
        Assert.Null(new ConnectedTaskSourcesBadgeSource().Badge);
        Assert.Null(new ConnectedTaskSourcesBadgeSource(LinkedTaskSources.None).Badge);
        Assert.Null(new ModelPriceBadgeSource().Badge);
        Assert.Null(new DevicePairingBadgeSource().Badge);
    }

    [Fact]
    public void Each_page_names_its_badge_source_and_registers_it_as_a_singleton()
    {
        var services = new ServiceCollection();
        services.AddInboxSettings();
        services.AddTaskConnectorSettings();
        services.AddSessionsSettings();
        services.AddSyncSettings();

        var badges = services.Select(descriptor => descriptor.ImplementationInstance).OfType<SettingsSection>()
            .ToDictionary(section => section.Id, section => section.BadgeSource);

        Assert.Equal(typeof(InboxRoutingRulesBadgeSource), badges["inbox"]);
        Assert.Equal(typeof(ConnectedTaskSourcesBadgeSource), badges["connectors"]);
        Assert.Equal(typeof(ModelPriceBadgeSource), badges["model-prices"]);
        Assert.Equal(typeof(DevicePairingBadgeSource), badges["devices"]);
        Assert.Null(badges["claude-code"]);

        foreach (var type in badges.Values.OfType<Type>())
        {
            var descriptor = Assert.Single(services, candidate => candidate.ServiceType == type);
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        }

        // Resolved in a host that keeps none of the settings, each still resolves
        // and draws nothing, rather than failing the settings screen.
        using var provider = services.BuildServiceProvider();
        foreach (var type in badges.Values.OfType<Type>())
        {
            Assert.Null(Assert.IsAssignableFrom<ISettingsBadgeSource>(provider.GetRequiredService(type)).Badge);
        }
    }

    /// <summary>A token endpoint that refuses every credential.</summary>
    private sealed class Refusing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
    }
}
