using Backlog.Modules.Tasks.Abstractions.Connectors;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using static Backlog.Desktop.UI.UnitTests.LinkedTaskPaneTests;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Connectors settings page (ADR 0020 §9): connect a repository or a product,
/// edit how it syncs, disconnect it. Drawn from the connectors' descriptors and the
/// connected targets alone, so it is rendered here with a fake connector.
/// </summary>
public sealed class TaskConnectorSettingsTests
{
    private static (BunitContext Context, InMemoryTargets Targets, RecordingSync Sync) Compose(bool withConnector = true, params ConnectedTarget[] targets)
    {
        var context = new BunitContext();
        var store = new InMemoryTargets(targets);
        var sync = new RecordingSync();
        context.Services.AddSingleton(new LinkedTaskSources(withConnector ? [new FakeConnector()] : [], store));
        context.Services.AddSingleton<ILinkedTaskSync>(sync);
        return (context, store, sync);
    }

    [Fact]
    public void The_page_is_registered_as_a_settings_section()
    {
        var services = new ServiceCollection().AddTaskConnectorSettings();

        var section = Assert.Single(services.BuildServiceProvider().GetServices<SettingsSection>());
        Assert.Equal("Connectors", section.Title);
        Assert.Equal(typeof(TaskConnectorSettings), section.Component);
    }

    [Fact]
    public void With_no_connector_installed_it_says_so_and_offers_nothing_to_connect()
    {
        var (context, _, _) = Compose(withConnector: false);
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        Assert.NotNull(page.Find("[data-testid='task-connectors-empty']"));
        Assert.Empty(page.FindAll("[data-testid='task-connectors-connect']"));
    }

    [Fact]
    public async Task Connecting_a_repository_stores_a_target_and_draws_its_card()
    {
        var (context, targets, _) = Compose();
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        Assert.NotNull(page.Find("[data-testid='task-connectors-none']"));

        await page.Find("input#task-connector-target").InputAsync(new() { Value = " owner/repo " });
        await page.Find("[data-testid='task-connector-connect']").ClickAsync(new());

        var stored = Assert.Single(targets.List());
        Assert.Equal(new ConnectedTarget(FakeConnector.Id, "owner/repo"), stored);
        Assert.Equal("owner/repo", page.Find("[data-testid='task-connector-target-card-name']").TextContent);
    }

    [Fact]
    public async Task Connecting_the_same_target_twice_is_refused_in_words()
    {
        var (context, targets, _) = Compose(true, new ConnectedTarget(FakeConnector.Id, "owner/repo"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        await page.Find("input#task-connector-target").InputAsync(new() { Value = "owner/repo" });
        await page.Find("[data-testid='task-connector-connect']").ClickAsync(new());

        Assert.Single(targets.List());
        Assert.Contains("already connected", page.Find("[data-testid='task-connector-target']").TextContent);
    }

    [Fact]
    public async Task An_option_changed_on_the_card_is_stored_on_the_target()
    {
        var (context, targets, _) = Compose(true, new ConnectedTarget(FakeConnector.Id, "owner/repo"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        await page.Find("[data-testid='task-connector-target-card-title-follows'] button[role='switch']").ClickAsync(new());
        await page.Find("select#task-connector-target-card-interval").ChangeAsync(new() { Value = "60" });

        var stored = Assert.Single(targets.List());
        Assert.False(stored.TitleFollowsSource);
        Assert.Equal(TimeSpan.FromHours(1), stored.SyncInterval);
    }

    [Fact]
    public async Task Sync_now_runs_the_sync_and_disconnect_removes_the_target()
    {
        var (context, targets, sync) = Compose(true, new ConnectedTarget(FakeConnector.Id, "owner/repo"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();
        await page.Find("[data-testid='task-connector-target-card-sync']").ClickAsync(new());
        Assert.Equal(1, sync.Runs);

        await page.Find("[data-testid='task-connector-target-card-remove']").ClickAsync(new());
        Assert.Empty(targets.List());
        Assert.Contains("Disconnected owner/repo", page.Find("[data-testid='task-connectors-message']").TextContent);
    }

    [Fact]
    public void A_target_whose_connector_is_not_installed_stays_listed_under_its_id()
    {
        var (context, _, _) = Compose(true, new ConnectedTarget("jira", "PROJ"));
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        Assert.Contains("jira", page.Find(".connected-target__connector").TextContent);
    }

    // --- Signing in ------------------------------------------------------------

    private static BunitContext ComposeSigning(SigningConnector connector, bool withPlain = false)
    {
        var context = new BunitContext();
        ITaskConnector[] connectors = withPlain ? [new FakeConnector(), connector] : [connector];
        context.Services.AddSingleton(new LinkedTaskSources(connectors, new InMemoryTargets()));
        context.Services.AddSingleton<ILinkedTaskSync>(new RecordingSync());
        return context;
    }

    [Fact]
    public void Only_a_connector_that_signs_in_gets_a_sign_in_line()
    {
        using var context = ComposeSigning(new SigningConnector(), withPlain: true);

        var page = context.Render<TaskConnectorSettings>();

        var line = Assert.Single(page.FindAll(".connector-sign-in"));
        Assert.Equal("Tracker", line.QuerySelector(".connector-sign-in__name")!.TextContent);
        Assert.Equal("Not signed in", page.Find(Part("account")).TextContent.Trim());
    }

    [Fact]
    public void With_no_connector_that_signs_in_there_is_no_sign_in_block()
    {
        var (context, _, _) = Compose();
        using var _context = context;

        var page = context.Render<TaskConnectorSettings>();

        Assert.Empty(page.FindAll("[data-testid='task-connectors-sign-ins']"));
    }

    [Fact]
    public void Sign_in_is_busy_while_it_runs_and_then_names_the_account()
    {
        var connector = new SigningConnector();
        using var context = ComposeSigning(connector);
        var page = context.Render<TaskConnectorSettings>();

        page.Find(Part("sign-in")).Click();

        Assert.Equal("true", page.Find(Part("sign-in")).GetAttribute("aria-busy"));

        connector.CompleteSignIn(new TaskConnectorAccount("Sam", DateTimeOffset.UnixEpoch), error: null);

        page.WaitForAssertion(() => Assert.Equal("Signed in as Sam", page.Find(Part("account")).TextContent.Trim()));
        Assert.NotNull(page.Find(Part("sign-out")));
    }

    [Fact]
    public void A_sign_in_that_did_not_work_shows_the_connector_s_own_words()
    {
        var connector = new SigningConnector();
        using var context = ComposeSigning(connector);
        var page = context.Render<TaskConnectorSettings>();

        page.Find(Part("sign-in")).Click();
        connector.CompleteSignIn(account: null, error: "The window was closed before it finished.");

        page.WaitForAssertion(() =>
            Assert.Equal("The window was closed before it finished.", page.Find(Part("error")).TextContent));
        Assert.Null(page.Find(Part("sign-in")).GetAttribute("aria-busy"));
    }

    [Fact]
    public async Task Leaving_the_page_lets_a_sign_in_under_way_finish_and_coming_back_shows_it()
    {
        // The browser is still open on the sign-in, waiting to redirect to the
        // connector's loopback listener: cancelling here would send it to a dead port.
        var connector = new SigningConnector();
        using var context = ComposeSigning(connector);
        var page = context.Render<TaskConnectorSettings>();
        page.Find(Part("sign-in")).Click();

        await context.DisposeComponentsAsync();

        Assert.False(connector.SignInToken.CanBeCanceled);

        connector.CompleteSignIn(new TaskConnectorAccount("Sam", DateTimeOffset.UnixEpoch), error: null);
        await connector.SignInFinished;

        var back = context.Render<TaskConnectorSettings>();
        Assert.Equal("Signed in as Sam", back.Find(Part("account")).TextContent.Trim());
    }

    [Fact]
    public void Sign_out_forgets_the_account()
    {
        var connector = new SigningConnector { Account = new TaskConnectorAccount("Sam", DateTimeOffset.UnixEpoch) };
        using var context = ComposeSigning(connector);
        var page = context.Render<TaskConnectorSettings>();

        page.Find(Part("sign-out")).Click();

        page.WaitForAssertion(() => Assert.Equal("Not signed in", page.Find(Part("account")).TextContent.Trim()));
        Assert.Equal(1, connector.SignOuts);
    }

    [Fact]
    public void An_account_change_the_connector_raises_on_its_own_re_renders_the_page()
    {
        var connector = new SigningConnector();
        using var context = ComposeSigning(connector);
        var page = context.Render<TaskConnectorSettings>();

        connector.Account = new TaskConnectorAccount("Sam", DateTimeOffset.UnixEpoch);
        connector.RaiseAccountChanged();

        page.WaitForAssertion(() => Assert.Equal("Signed in as Sam", page.Find(Part("account")).TextContent.Trim()));
    }

    private static string Part(string part) => $"[data-testid='task-connector-sign-in-{SigningConnector.Id}-{part}']";

    /// <summary>A connector a person signs in to, whose sign-in waits for the test
    /// to finish it.</summary>
    private sealed class SigningConnector : ITaskConnector, ITaskConnectorSignIn
    {
        public const string Id = "tracker";

        private TaskCompletionSource<string?>? _signIn;
        private TaskConnectorAccount? _pending;

        public TaskConnectorDescriptor Descriptor { get; } = new(Id, "Tracker", "tracker", "color-primary");

        public TaskConnectorCapabilities Capabilities { get; } = new();

        public TaskConnectorAccount? Account { get; set; }

        public event Action? AccountChanged;

        public CancellationToken SignInToken { get; private set; }

        public int SignOuts { get; private set; }

        /// <summary>The last sign-in, completed once it has applied its account and
        /// raised <see cref="AccountChanged"/>.</summary>
        public Task<string?> SignInFinished { get; private set; } = Task.FromResult<string?>(null);

        public Task<IReadOnlyList<SourceItem>> FetchAsync(string target, DateTimeOffset? since, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SourceItem>>([]);

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

    internal sealed class RecordingSync : ILinkedTaskSync
    {
        public int Runs { get; private set; }

        public Task RequestSync()
        {
            Runs++;
            return Task.CompletedTask;
        }
    }
}
