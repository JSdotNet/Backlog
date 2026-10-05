using Backlog.Desktop.UI.Inbox;
using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Tasks.Abstractions.Connectors;
using Backlog.SharedKernel;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Connectors page on the settings screen, drawn only from what the host
/// registered: one card per connector by its descriptor, Sign in and Sign out for a
/// connector that has them, and the connected targets kept in the store.
/// <para>
/// Asserted on what the page shows and on what reached the store, never on a
/// handler having run: bUnit swallows an event handler's exception, so a handler
/// that threw would otherwise pass for one that did nothing wrong.
/// </para>
/// </summary>
public sealed class ConnectorSettingsTests
{
    [Fact]
    public void Each_registered_connector_is_a_card_drawn_from_its_descriptor()
    {
        using var page = new ConnectorPage(new PlainConnector("issues", "Issues", "●", "color-info"), new SigningConnector());

        var cards = page.Render().FindAll(".connector-card");

        Assert.Equal(["Issues", "Tracker"], cards.Select(card => card.QuerySelector(".connector-card__name")!.TextContent));
        Assert.Equal("color: var(--color-info)", cards[0].QuerySelector(".connector-card__mark")!.GetAttribute("style"));
        Assert.Empty(cards[0].QuerySelectorAll("[data-testid='connector-account']"));
        Assert.NotNull(cards[1].QuerySelector("[data-testid='connector-account']"));
    }

    [Fact]
    public void With_no_connector_the_page_says_so()
    {
        using var page = new ConnectorPage();

        var view = page.Render();

        Assert.Contains("No connectors", view.Find("[data-testid='connectors-empty']").TextContent, StringComparison.Ordinal);
        Assert.Empty(view.FindAll(".connector-card"));
    }

    [Fact]
    public void Sign_in_is_busy_while_it_runs_and_then_names_the_account()
    {
        var connector = new SigningConnector();
        using var page = new ConnectorPage(connector);
        var view = page.Render();

        view.Find("[data-testid='connector-sign-in']").Click();

        Assert.Equal("true", view.Find("[data-testid='connector-sign-in']").GetAttribute("aria-busy"));

        connector.CompleteSignIn(new TaskConnectorAccount("Sam", DateTimeOffset.UnixEpoch), error: null);

        view.WaitForAssertion(() =>
            Assert.Equal("Signed in as Sam", view.Find("[data-testid='connector-account']").TextContent.Trim()));
        Assert.NotNull(view.Find("[data-testid='connector-sign-out']"));
    }

    [Fact]
    public void A_sign_in_that_did_not_work_shows_the_connector_s_own_words()
    {
        var connector = new SigningConnector();
        using var page = new ConnectorPage(connector);
        var view = page.Render();

        view.Find("[data-testid='connector-sign-in']").Click();
        connector.CompleteSignIn(account: null, error: "The window was closed before it finished.");

        view.WaitForAssertion(() =>
            Assert.Equal("The window was closed before it finished.", view.Find("[data-testid='connector-sign-in-error']").TextContent));
        Assert.Null(view.Find("[data-testid='connector-sign-in']").GetAttribute("aria-busy"));
    }

    [Fact]
    public async Task Leaving_the_page_lets_a_sign_in_under_way_finish_and_coming_back_shows_it()
    {
        // The browser is still open on the sign-in, waiting to redirect to the
        // connector's loopback listener: cancelling here would send it to a dead port.
        var connector = new SigningConnector();
        using var page = new ConnectorPage(connector);
        var view = page.Render();
        view.Find("[data-testid='connector-sign-in']").Click();

        await page.Context.DisposeComponentsAsync();

        Assert.False(connector.SignInToken.CanBeCanceled);

        connector.CompleteSignIn(new TaskConnectorAccount("Sam", DateTimeOffset.UnixEpoch), error: null);
        await connector.SignInFinished;

        var back = page.Render();
        Assert.Equal("Signed in as Sam", back.Find("[data-testid='connector-account']").TextContent.Trim());
    }

    [Fact]
    public void Sign_out_forgets_the_account()
    {
        var connector = new SigningConnector { Account = new TaskConnectorAccount("Sam", DateTimeOffset.UnixEpoch) };
        using var page = new ConnectorPage(connector);
        var view = page.Render();

        view.Find("[data-testid='connector-sign-out']").Click();

        view.WaitForAssertion(() =>
            Assert.Equal("Not signed in", view.Find("[data-testid='connector-account']").TextContent.Trim()));
        Assert.Equal(1, connector.SignOuts);
    }

    [Fact]
    public void An_account_change_the_connector_raises_on_its_own_re_renders_the_card()
    {
        var connector = new SigningConnector();
        using var page = new ConnectorPage(connector);
        var view = page.Render();

        connector.Account = new TaskConnectorAccount("Sam", DateTimeOffset.UnixEpoch);
        connector.RaiseAccountChanged();

        view.WaitForAssertion(() =>
            Assert.Equal("Signed in as Sam", view.Find("[data-testid='connector-account']").TextContent.Trim()));
    }

    [Fact]
    public void A_card_lists_only_its_own_connector_s_targets()
    {
        using var page = new ConnectorPage(new SigningConnector(), new PlainConnector("issues", "Issues"));
        page.Targets.Save(new ConnectedTarget(SigningConnector.Id, "planning") { LastSyncedAt = DateTimeOffset.UnixEpoch });
        page.Targets.Save(new ConnectedTarget("issues", "acme/web"));
        page.Targets.Save(new ConnectedTarget("not-registered", "elsewhere"));

        var view = page.Render();

        Assert.Equal(["acme/web"], TargetsOf(view, "issues"));
        Assert.Equal(["planning"], TargetsOf(view, SigningConnector.Id));
        Assert.StartsWith("Synced ", view.Find($"[data-testid='connector-{SigningConnector.Id}'] [data-testid='connector-target-synced']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_saves_the_trimmed_name_and_clears_the_field()
    {
        using var page = new ConnectorPage(new SigningConnector());
        var view = page.Render();

        view.Find("[data-testid='connector-add'] input").Input("  planning  ");
        view.Find("form.connector-card__add").Submit();

        Assert.Equal(new ConnectedTarget(SigningConnector.Id, "planning"), Assert.Single(page.Targets.List()));
        Assert.Equal(["planning"], TargetsOf(view, SigningConnector.Id));
        Assert.Equal(string.Empty, view.Find("[data-testid='connector-add'] input").GetAttribute("value"));
    }

    [Fact]
    public void A_blank_name_is_refused_and_nothing_is_saved()
    {
        using var page = new ConnectorPage(new SigningConnector());
        var view = page.Render();

        view.Find("[data-testid='connector-add'] input").Input("   ");
        view.Find("form.connector-card__add").Submit();

        Assert.Empty(page.Targets.List());
        Assert.Equal("Type what to connect first.", view.Find("[data-testid='connector-add'] .field__error").TextContent);
    }

    [Fact]
    public void A_name_already_connected_is_refused_without_regard_to_case()
    {
        using var page = new ConnectorPage(new SigningConnector());
        page.Targets.Save(new ConnectedTarget(SigningConnector.Id, "planning", Enabled: false));
        var view = page.Render();

        view.Find("[data-testid='connector-add'] input").Input("Planning");
        view.Find("form.connector-card__add").Submit();

        var kept = Assert.Single(page.Targets.List());
        Assert.False(kept.Enabled);
        Assert.Equal("Planning is already connected.", view.Find("[data-testid='connector-add'] .field__error").TextContent);
        Assert.Equal("Planning", view.Find("[data-testid='connector-add'] input").GetAttribute("value"));
    }

    [Fact]
    public void A_name_the_store_could_not_keep_shows_the_store_s_words()
    {
        using var page = new ConnectorPage(new SigningConnector());
        page.Targets.FailWith = "The settings file could not be written.";
        var view = page.Render();

        view.Find("[data-testid='connector-add'] input").Input("planning");
        view.Find("form.connector-card__add").Submit();

        Assert.Equal("The settings file could not be written.", view.Find("[data-testid='connector-add'] .field__error").TextContent);
        Assert.Equal("planning", view.Find("[data-testid='connector-add'] input").GetAttribute("value"));
    }

    [Fact]
    public void The_switch_pauses_a_target_and_keeps_its_other_settings()
    {
        using var page = new ConnectorPage(new SigningConnector());
        page.Targets.Save(new ConnectedTarget(SigningConnector.Id, "planning") { SyncInterval = TimeSpan.FromHours(1) });
        var view = page.Render();

        view.Find("[data-testid='connector-target'] [role='switch']").Click();

        var kept = Assert.Single(page.Targets.List());
        Assert.False(kept.Enabled);
        Assert.Equal(TimeSpan.FromHours(1), kept.SyncInterval);
        Assert.Equal("false", view.Find("[data-testid='connector-target'] [role='switch']").GetAttribute("aria-checked"));
    }

    [Fact]
    public void Remove_forgets_the_target()
    {
        using var page = new ConnectorPage(new SigningConnector());
        page.Targets.Save(new ConnectedTarget(SigningConnector.Id, "planning"));
        var view = page.Render();

        view.Find("[data-testid='connector-target-remove']").Click();

        Assert.Empty(page.Targets.List());
        Assert.Equal("Nothing connected yet.", view.Find("[data-testid='connector-no-targets']").TextContent);
    }

    [Fact]
    public void A_target_saved_elsewhere_appears_without_reopening_the_page()
    {
        using var page = new ConnectorPage(new SigningConnector());
        var view = page.Render();

        page.Targets.Save(new ConnectedTarget(SigningConnector.Id, "planning"));

        view.WaitForAssertion(() => Assert.Equal(["planning"], TargetsOf(view, SigningConnector.Id)));
    }

    [Fact]
    public void Sync_now_asks_the_trigger_and_is_busy_until_the_run_ends()
    {
        using var page = new ConnectorPage(new SigningConnector());
        page.Targets.Save(new ConnectedTarget(SigningConnector.Id, "planning"));
        var view = page.Render();

        view.Find("[data-testid='connectors-sync-now']").Click();

        Assert.Equal(1, page.Trigger.Requests);
        Assert.Equal("true", view.Find("[data-testid='connectors-sync-now']").GetAttribute("aria-busy"));

        page.Trigger.Finish();

        view.WaitForAssertion(() => Assert.Null(view.Find("[data-testid='connectors-sync-now']").GetAttribute("aria-busy")));
    }

    [Fact]
    public void Sync_now_is_off_while_nothing_enabled_is_connected()
    {
        using var page = new ConnectorPage(new SigningConnector());
        page.Targets.Save(new ConnectedTarget(SigningConnector.Id, "planning", Enabled: false));
        var view = page.Render();

        Assert.True(view.Find("[data-testid='connectors-sync-now']").HasAttribute("disabled"));
    }

    [Fact]
    public void Without_a_sync_there_is_no_sync_now()
    {
        using var page = new ConnectorPage(withTrigger: false, connectors: new SigningConnector());
        page.Targets.Save(new ConnectedTarget(SigningConnector.Id, "planning"));

        var view = page.Render();

        Assert.Empty(view.FindAll("[data-testid='connectors-sync-now']"));
    }

    [Fact]
    public void The_page_is_registered_as_its_own_settings_section_after_the_shell_s_pages()
    {
        var services = new ServiceCollection();

        services.AddTasksAdapters();

        var section = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(SettingsSection)).ImplementationInstance as SettingsSection;
        Assert.Equal(new SettingsSection("connectors", "Connectors", ConnectorSettingsRegistration.Order, typeof(ConnectorSettings)), section);
        Assert.True(ConnectorSettingsRegistration.Order < InboxSettingsRegistration.Order);
    }

    private static IEnumerable<string?> TargetsOf(IRenderedComponent<ConnectorSettings> view, string connectorId) =>
        view.FindAll($"[data-testid='connector-{connectorId}'] [data-testid='connector-target']").Select(row => row.GetAttribute("data-target"));

    /// <summary>One render of the page over the doubles a test arranges first.</summary>
    private sealed class ConnectorPage : IDisposable
    {
        public ConnectorPage(params ITaskConnector[] connectors)
            : this(withTrigger: true, connectors)
        {
        }

        public ConnectorPage(bool withTrigger, params ITaskConnector[] connectors)
        {
            foreach (var connector in connectors) Context.Services.AddSingleton<ITaskConnector>(connector);
            Context.Services.AddSingleton<IConnectedTargets>(Targets);
            if (withTrigger) Context.Services.AddSingleton<ILinkedTaskSyncTrigger>(Trigger);
        }

        public BunitContext Context { get; } = new();

        public RecordingTargets Targets { get; } = new();

        public HeldTrigger Trigger { get; } = new();

        public IRenderedComponent<ConnectorSettings> Render() => Context.Render<ConnectorSettings>();

        public void Dispose() => Context.Dispose();
    }

    private sealed class PlainConnector(string id, string name, string icon = "", string colorToken = "") : ITaskConnector
    {
        public TaskConnectorDescriptor Descriptor { get; } = new(id, name, icon, colorToken);

        public TaskConnectorCapabilities Capabilities { get; } = new();

        public Task<IReadOnlyList<SourceItem>> FetchAsync(string target, DateTimeOffset? since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SourceItem>>([]);

        public Task<string?> WhoAmIAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    /// <summary>A connector a person signs in to, whose sign-in waits for the test
    /// to finish it.</summary>
    private sealed class SigningConnector : ITaskConnector, ITaskConnectorSignIn
    {
        public const string Id = "tracker";

        private TaskCompletionSource<string?>? _signIn;
        private TaskConnectorAccount? _pending;

        public TaskConnectorDescriptor Descriptor { get; } = new(Id, "Tracker", "◆", "color-primary");

        public TaskConnectorCapabilities Capabilities { get; } = new();

        public TaskConnectorAccount? Account { get; set; }

        public event Action? AccountChanged;

        public CancellationToken SignInToken { get; private set; }

        public int SignOuts { get; private set; }

        public Task<IReadOnlyList<SourceItem>> FetchAsync(string target, DateTimeOffset? since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SourceItem>>([]);

        public Task<string?> WhoAmIAsync(CancellationToken cancellationToken) => Task.FromResult(Account?.DisplayName);

        /// <summary>The last sign-in, completed once it has applied its account and
        /// raised <see cref="AccountChanged"/>.</summary>
        public Task<string?> SignInFinished { get; private set; } = Task.FromResult<string?>(null);

        public Task<string?> SignInAsync(CancellationToken cancellationToken)
        {
            SignInToken = cancellationToken;
            _signIn = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            SignInFinished = RunSignInAsync(_signIn, cancellationToken);
            return SignInFinished;
        }

        private async Task<string?> RunSignInAsync(TaskCompletionSource<string?> signIn, CancellationToken cancellationToken)
        {
            await using var registration = cancellationToken.Register(() => signIn.TrySetCanceled(cancellationToken));

            var error = await signIn.Task;
            if (error is null)
            {
                Account = _pending;
                AccountChanged?.Invoke();
            }

            return error;
        }

        public void CompleteSignIn(TaskConnectorAccount? account, string? error)
        {
            _pending = account;
            _signIn!.SetResult(error);
        }

        public Task SignOutAsync(CancellationToken cancellationToken)
        {
            SignOuts++;
            Account = null;
            AccountChanged?.Invoke();
            return Task.CompletedTask;
        }

        public void RaiseAccountChanged() => AccountChanged?.Invoke();
    }

    /// <summary>The store, in memory, keyed the way the real one is — connector
    /// exactly, target without regard to case — and able to refuse a save.</summary>
    private sealed class RecordingTargets : IConnectedTargets
    {
        private readonly List<ConnectedTarget> _targets = [];

        public event Action? Changed;

        public string? FailWith { get; set; }

        public IReadOnlyList<ConnectedTarget> List() => [.. _targets];

        public ConnectedTarget? Get(string connectorId, string target) =>
            _targets.FirstOrDefault(candidate => candidate.Is(connectorId, target));

        public string? Save(ConnectedTarget target)
        {
            if (FailWith is not null) return FailWith;

            var index = _targets.FindIndex(candidate => candidate.Is(target.ConnectorId, target.Target));
            if (index >= 0) _targets[index] = target;
            else _targets.Add(target);

            Changed?.Invoke();
            return null;
        }

        public string? Update(string connectorId, string target, Func<ConnectedTarget, ConnectedTarget> change)
        {
            var index = _targets.FindIndex(candidate => candidate.Is(connectorId, target));
            if (index < 0) return null;

            _targets[index] = change(_targets[index]);
            Changed?.Invoke();
            return null;
        }

        public string? Remove(string connectorId, string target)
        {
            _targets.RemoveAll(candidate => candidate.Is(connectorId, target));
            Changed?.Invoke();
            return null;
        }
    }

    /// <summary>A sync that runs until the test lets it finish.</summary>
    private sealed class HeldTrigger : ILinkedTaskSyncTrigger
    {
        private readonly TaskCompletionSource _run = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Requests { get; private set; }

        public Task RequestSync()
        {
            Requests++;
            return _run.Task;
        }

        public void Finish() => _run.TrySetResult();
    }
}
