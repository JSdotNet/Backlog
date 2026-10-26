using Backlog.UI.Components.Buttons;
using Backlog.UI.Components.Settings;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Settings screen's MCP server page (local ADR 0012): whether this machine's
/// MCP server holds its port, the address a client registers, the worker's own
/// sentence when it could not bind and a Retry for it, the port, and the bearer
/// token - hidden on screen, whole on the clipboard, and never minted by drawing.
/// The server is a stand-in at <see cref="IMcpEndpointSource"/>, the port the page
/// reads it through; the real listener behind it is DesktopMcpEndpointSourceTests'.
/// </summary>
public sealed class McpServerSettingsPageTests
{
    private const string Collision =
        "Port 5757 is already in use, so the MCP server could not start. "
        + "Close whatever is using it, or choose another port — and change it in every registration too.";

    [Fact]
    public void A_listening_server_shows_its_state_and_an_address_to_copy()
    {
        var server = new FakeMcpServer { IsListening = true };
        using var context = Context(server);

        var page = context.Render<McpServerSettingsPage>();

        Assert.Equal("Listening", Text(page, "mcp-server-state"));
        Assert.Equal("http://127.0.0.1:5757/mcp", Text(page, "mcp-server-address"));
        Assert.Equal("http://127.0.0.1:5757/mcp", Copy(page, "mcp-server-address-copy").Text);
        Assert.Equal("5757", page.Find("[data-testid='mcp-server-port-input']").GetAttribute("value"));
        Assert.Empty(page.FindAll("[data-testid='mcp-server-error']"));
        Assert.Empty(page.FindAll("[data-testid='mcp-server-retry']"));
    }

    [Fact]
    public void A_failed_bind_shows_the_worker_s_sentence_the_configured_address_and_a_retry()
    {
        var server = new FakeMcpServer { Unavailable = Collision };
        using var context = Context(server);

        var page = context.Render<McpServerSettingsPage>();

        Assert.Equal("Not listening", Text(page, "mcp-server-state"));
        Assert.Equal(Collision, Text(page, "mcp-server-error"));
        Assert.Equal("http://127.0.0.1:5757/mcp", Text(page, "mcp-server-address"));
        Assert.Equal("5757", page.Find("[data-testid='mcp-server-port-input']").GetAttribute("value"));

        page.Find("[data-testid='mcp-server-retry']").Click();

        // The stand-in clears its sentence as the worker does when it queues the bind.
        Assert.Equal("Starting", Text(page, "mcp-server-state"));
        Assert.Empty(page.FindAll("[data-testid='mcp-server-retry']"));
        Assert.Empty(page.FindAll("[data-testid='mcp-server-error']"));
    }

    [Fact]
    public void The_token_is_hidden_on_screen_and_whole_on_the_clipboard()
    {
        var server = new FakeMcpServer { IsListening = true, CurrentToken = "secret-token" };
        using var context = Context(server);

        var page = context.Render<McpServerSettingsPage>();

        Assert.Equal(McpServerSettingsPage.HiddenToken, Text(page, "mcp-server-token"));
        Assert.DoesNotContain("secret-token", page.Markup, StringComparison.Ordinal);
        Assert.Equal("secret-token", Copy(page, "mcp-server-token-copy").Text);
        Assert.Equal(0, server.TokensMinted);
    }

    [Fact]
    public void Drawing_the_page_mints_no_token_and_creating_one_offers_it_to_copy()
    {
        var server = new FakeMcpServer { Unavailable = Collision };
        using var context = Context(server);

        var page = context.Render<McpServerSettingsPage>();

        Assert.Equal(0, server.TokensMinted);
        Assert.Null(server.CurrentToken);
        Assert.Empty(page.FindAll("[data-testid='mcp-server-token-copy']"));

        page.Find("[data-testid='mcp-server-token-create']").Click();

        Assert.Equal(server.CurrentToken, Copy(page, "mcp-server-token-copy").Text);
        Assert.Equal(McpServerSettingsPage.HiddenToken, Text(page, "mcp-server-token"));
    }

    [Fact]
    public void A_port_typed_and_committed_is_stored_and_the_field_says_every_registration_must_follow()
    {
        var server = new FakeMcpServer { IsListening = true };
        using var context = Context(server);
        var page = context.Render<McpServerSettingsPage>();

        Commit(page, "6123");

        Assert.Equal(6123, server.Port);
        Assert.Equal("6123", page.Find("[data-testid='mcp-server-port-input']").GetAttribute("value"));
        Assert.Equal("Starting", Text(page, "mcp-server-state"));
        Assert.Equal("http://127.0.0.1:6123/mcp", Text(page, "mcp-server-address"));
        Assert.Contains("changing it in each of them too", Text(page, "mcp-server-port-status"), StringComparison.Ordinal);
        Assert.DoesNotContain("setting__input--error", page.Find("[data-testid='mcp-server-port-input']").ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void A_port_outside_the_range_is_refused_beside_the_field_and_not_stored()
    {
        var server = new FakeMcpServer { IsListening = true };
        using var context = Context(server);
        var page = context.Render<McpServerSettingsPage>();

        Commit(page, "80");

        Assert.Equal(5757, server.Port);
        Assert.Equal("Choose a port between 1024 and 65535.", Text(page, "mcp-server-port-status"));
        Assert.Contains("setting__input--error", page.Find("[data-testid='mcp-server-port-input']").ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void A_port_taken_but_not_saved_for_next_time_is_a_plain_status_not_a_refusal()
    {
        const string NotSaved = "Port changed, but the choice couldn't be saved for next time.";
        var server = new FakeMcpServer { IsListening = true, SaveFailure = NotSaved };
        using var context = Context(server);
        var page = context.Render<McpServerSettingsPage>();

        Commit(page, "6124");

        Assert.Equal(6124, server.Port);
        Assert.Equal(NotSaved, Text(page, "mcp-server-port-status"));
        Assert.DoesNotContain("setting__status--error", page.Find("[data-testid='mcp-server-port-status']").ClassName, StringComparison.Ordinal);
        Assert.DoesNotContain("setting__input--error", page.Find("[data-testid='mcp-server-port-input']").ClassName, StringComparison.Ordinal);
        Assert.Equal("6124", page.Find("[data-testid='mcp-server-port-input']").GetAttribute("value"));
    }

    [Fact]
    public void Enter_in_the_port_field_commits_the_port()
    {
        var server = new FakeMcpServer { IsListening = true };
        using var context = Context(server);
        var page = context.Render<McpServerSettingsPage>();

        page.Find("[data-testid='mcp-server-port-input']").Input("6200");
        page.Find("[data-testid='mcp-server-port-input']").KeyDown("Enter");

        Assert.Equal(6200, server.Port);
        Assert.Equal("6200", page.Find("[data-testid='mcp-server-port-input']").GetAttribute("value"));
        Assert.Equal("Starting", Text(page, "mcp-server-state"));
    }

    [Fact]
    public void Escape_in_the_port_field_puts_back_the_stored_port_and_clears_a_refusal()
    {
        var server = new FakeMcpServer { IsListening = true };
        using var context = Context(server);
        var page = context.Render<McpServerSettingsPage>();
        Commit(page, "80");

        page.Find("[data-testid='mcp-server-port-input']").Input("6300");
        page.Find("[data-testid='mcp-server-port-input']").KeyDown("Escape");

        Assert.Equal(5757, server.Port);
        Assert.Equal("5757", page.Find("[data-testid='mcp-server-port-input']").GetAttribute("value"));
        Assert.DoesNotContain("setting__input--error", page.Find("[data-testid='mcp-server-port-input']").ClassName, StringComparison.Ordinal);
        Assert.Contains("changing it in each of them too", Text(page, "mcp-server-port-status"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_page_redraws_when_the_listener_changes_on_another_thread()
    {
        var server = new FakeMcpServer();
        using var context = Context(server);
        var page = context.Render<McpServerSettingsPage>();
        Assert.Equal("Starting", Text(page, "mcp-server-state"));

        server.IsListening = true;
        await Task.Run(server.Raise, TestContext.Current.CancellationToken);

        page.WaitForAssertion(() => Assert.Equal("Listening", Text(page, "mcp-server-state")));
    }

    [Fact]
    public async Task Leaving_the_page_lets_go_of_the_server_s_event()
    {
        var server = new FakeMcpServer { IsListening = true };
        using var context = Context(server);
        context.Render<McpServerSettingsPage>();
        Assert.Equal(1, server.Subscribers);

        await context.DisposeComponentsAsync();

        Assert.Equal(0, server.Subscribers);
    }

    [Fact]
    public void A_host_with_no_server_of_its_own_answers_on_its_own_origin_with_no_token_and_nothing_to_edit()
    {
        using var context = Context(server: null);

        var page = context.Render<McpServerSettingsPage>();

        Assert.Equal("Listening", Text(page, "mcp-server-state"));
        Assert.Equal("http://localhost/mcp", Text(page, "mcp-server-address"));
        Assert.Contains("no token", Text(page, "mcp-server-token-note"), StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-testid='mcp-server-port-input']"));
        Assert.Empty(page.FindAll("[data-testid='mcp-server-retry']"));
        Assert.Empty(page.FindAll("[data-testid='mcp-server-token-create']"));
    }

    [Theory]
    [InlineData(true, null, "The MCP server is listening on port 5757.")]
    [InlineData(false, Collision, "The MCP server is not listening on port 5757.")]
    [InlineData(false, null, "The MCP server is starting on port 5757.")]
    public void The_page_supplies_its_headline_to_the_frame(bool listening, string? unavailable, string headline)
    {
        using var context = Context(new FakeMcpServer { IsListening = listening, Unavailable = unavailable });

        var frame = context.Render<SettingsFrame>(parameters => parameters
            .Add(f => f.Title, "MCP server")
            .AddChildContent<McpServerSettingsPage>());

        Assert.Equal(headline, frame.Find("[data-testid='settings-headline']").TextContent.Trim());
    }

    [Fact]
    public void The_cards_on_the_page_are_the_ones_its_section_declares()
    {
        var section = SettingsPages.BuiltIn.Single(page => page.Id == SettingsPages.McpServer);
        using var context = Context(new FakeMcpServer { IsListening = true, CurrentToken = "t" });

        var page = context.Render<McpServerSettingsPage>();

        Assert.Equal(["MCP endpoint", "Bearer token"], section.CardTitles!);
        Assert.Equal(section.CardTitles!, page.FindAll(".settings-card__title").Select(title => title.TextContent.Trim()));
        Assert.Equal(SettingsGroup.Workspace, section.Group);
        Assert.Equal("MCP server", section.Title);
        Assert.Equal("settings/mcp-server", SettingsPages.Href(section.Id));
    }

    /// <summary>Offered on a screen where every optional feature is off - System
    /// tools included, which is about installing tools and not about this server -
    /// and again with System tools on.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_page_is_offered_under_Workspace_whatever_the_features_say(bool systemTools)
    {
        using var settings = SettingsSectionRenderingTests.RenderSettings(services =>
            services.AddSingleton<IMcpEndpointSource>(new FakeMcpServer { IsListening = true }));
        var features = settings.TestContext.Services.GetRequiredService<IAppFeatureSettings>();
        _ = features.SetEnabled(DevPcFeatures.SystemTools, systemTools);

        Assert.Equal(SettingsSectionRenderingTests.AppWidePages, SettingsMenu.Pages(settings.Component));
        Assert.Equal("MCP server", SettingsSectionRenderingTests.AppWidePages[^1]);
        Assert.Equal("Listening", SettingsMenu.Badge(settings.Component, SettingsPages.McpServer));

        SettingsMenu.Open(settings.Component, "MCP server");

        Assert.Equal("Settings · Workspace", settings.Component.Find("[data-testid='settings-eyebrow']").TextContent.Trim());
        Assert.NotNull(settings.Component.Find("section[aria-label='MCP server settings'] [data-testid='mcp-server-endpoint-card']"));
    }

    [Fact]
    public async Task The_menu_badge_follows_the_listener()
    {
        var server = new FakeMcpServer { Unavailable = Collision };
        using var settings = SettingsSectionRenderingTests.RenderSettings(services =>
            services.AddSingleton<IMcpEndpointSource>(server));

        Assert.Equal("Not listening", SettingsMenu.Badge(settings.Component, SettingsPages.McpServer));

        server.Unavailable = null;
        server.IsListening = true;
        await Task.Run(server.Raise, TestContext.Current.CancellationToken);

        settings.Component.WaitForAssertion(() =>
            Assert.Equal("Listening", SettingsMenu.Badge(settings.Component, SettingsPages.McpServer)));
    }

    [Fact]
    public void A_search_for_MCP_finds_the_page()
    {
        using var settings = SettingsSectionRenderingTests.RenderSettings();

        settings.Component.Find("[data-testid='settings-search-input']").Input("MCP");

        Assert.Equal(["MCP server"], SettingsMenu.Pages(settings.Component));
    }

    private static BunitContext Context(FakeMcpServer? server)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        if (server is not null) context.Services.AddSingleton<IMcpEndpointSource>(server);
        return context;
    }

    /// <summary>Types <paramref name="value"/> into the port field and leaves it,
    /// the way a person commits a settings field.</summary>
    private static void Commit(IRenderedComponent<McpServerSettingsPage> page, string value)
    {
        page.Find("[data-testid='mcp-server-port-input']").Input(value);
        page.Find("[data-testid='mcp-server-port-input']").Change(value);
    }

    private static string Text(IRenderedComponent<McpServerSettingsPage> page, string testId) =>
        page.Find($"[data-testid='{testId}']").TextContent.Trim();

    private static CopyButton Copy(IRenderedComponent<McpServerSettingsPage> page, string testId) =>
        page.FindComponents<CopyButton>().Single(button => button.Instance.TestId == testId).Instance;

    /// <summary>The desktop head's MCP server as the page sees it. Retry clears
    /// the sentence the way the worker does when it queues the bind; minting is
    /// counted, because drawing a page must never be what writes a secret.</summary>
    private sealed class FakeMcpServer : IMcpEndpointSource
    {
        private Action? _changed;

        public int Port { get; private set; } = 5757;

        public string? Unavailable { get; set; }

        public bool IsListening { get; set; }

        public Uri? Endpoint => IsListening ? new Uri($"http://127.0.0.1:{Port}/mcp") : null;

        public string? CurrentToken { get; set; }

        public int TokensMinted { get; private set; }

        public int Subscribers => _changed?.GetInvocationList().Length ?? 0;

        /// <summary>The sentence the store answers with when it moved the port
        /// but could not write it down; null for a store that saves.</summary>
        public string? SaveFailure { get; set; }

        /// <summary>Like the store: refuses a port out of range without a word to
        /// anyone, and announces a port it took synchronously, on the caller's
        /// thread, before it answers.</summary>
        public string? SetPort(int port)
        {
            if (port is < 1024 or > 65535) return "Choose a port between 1024 and 65535.";
            if (port == Port) return null;

            Port = port;
            IsListening = false;
            Raise();
            return SaveFailure;
        }

        /// <summary>Like the worker: clears the sentence as it queues the bind and
        /// says so synchronously, before it returns.</summary>
        public void Retry()
        {
            if (IsListening || Unavailable is null) return;

            Unavailable = null;
            Raise();
        }

        public string EnsureToken()
        {
            if (CurrentToken is { } existing) return existing;

            TokensMinted++;
            return CurrentToken = "minted-token";
        }

        public event Action? Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }

        public void Raise() => _changed?.Invoke();
    }
}
