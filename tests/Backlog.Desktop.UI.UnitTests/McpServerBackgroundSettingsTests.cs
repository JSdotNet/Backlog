using Backlog.Infrastructure.FileSystem;
using Backlog.UI.Components.Inputs;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The MCP server page's third card (local ADR 0043 §5): keep running in the tray
/// when the main window closes, and start Backlog at sign-in. The startup task is a
/// stand-in at <see cref="IStartupRegistration"/>; the tray switch writes a real
/// <see cref="WorkspaceSettingsStore"/> over a folder of the test's own.
/// </summary>
public sealed class McpServerBackgroundSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcp-background-settings-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void The_tray_switch_is_on_by_default_and_says_the_server_keeps_answering()
    {
        using var context = Context(new FakeStartup(StartupRegistrationState.Disabled), out _);

        var page = context.Render<McpServerSettingsPage>();

        var tray = Switch(page, "mcp-server-keep-in-tray");
        Assert.Equal("Keep running in the tray when closed", tray.Instance.Label);
        Assert.Equal("The MCP server keeps answering while the window is closed.", tray.Instance.Description);
        Assert.True(tray.Instance.Checked);
        Assert.False(tray.Instance.Disabled);
    }

    [Fact]
    public void Turning_the_tray_switch_off_is_stored_for_the_next_start()
    {
        using var context = Context(new FakeStartup(StartupRegistrationState.Disabled), out var settings);
        var page = context.Render<McpServerSettingsPage>();

        page.Find("[data-testid='mcp-server-keep-in-tray'] button").Click();

        Assert.False(settings.KeepRunningInTray);
        Assert.False(new WorkspaceSettingsStore(_root, Path.Combine(_root, "settings.json"), _ => null).KeepRunningInTray);
        Assert.False(Switch(page, "mcp-server-keep-in-tray").Instance.Checked);
    }

    [Fact]
    public void A_tray_choice_made_in_another_window_shows_here()
    {
        using var context = Context(new FakeStartup(StartupRegistrationState.Disabled), out var settings);
        var page = context.Render<McpServerSettingsPage>();

        settings.SetKeepRunningInTray(false);

        page.WaitForAssertion(() => Assert.False(Switch(page, "mcp-server-keep-in-tray").Instance.Checked));
    }

    [Fact]
    public void Start_at_sign_in_is_off_by_default_and_turning_it_on_asks_windows()
    {
        var startup = new FakeStartup(StartupRegistrationState.Disabled);
        using var context = Context(startup, out _);
        var page = context.Render<McpServerSettingsPage>();

        var signIn = Switch(page, "mcp-server-start-at-sign-in");
        Assert.Equal("Start Backlog when I sign in", signIn.Instance.Label);
        Assert.False(signIn.Instance.Checked);
        Assert.False(signIn.Instance.Disabled);
        Assert.Equal(McpServerSettingsPage.StartupNote, signIn.Instance.Description);

        page.Find("[data-testid='mcp-server-start-at-sign-in'] button").Click();

        Assert.Equal([true], startup.Requests);
        page.WaitForAssertion(() => Assert.True(Switch(page, "mcp-server-start-at-sign-in").Instance.Checked));
    }

    [Fact]
    public void An_enabled_task_shows_on_and_turning_it_off_asks_windows()
    {
        var startup = new FakeStartup(StartupRegistrationState.Enabled);
        using var context = Context(startup, out _);
        var page = context.Render<McpServerSettingsPage>();

        Assert.True(Switch(page, "mcp-server-start-at-sign-in").Instance.Checked);

        page.Find("[data-testid='mcp-server-start-at-sign-in'] button").Click();

        Assert.Equal([false], startup.Requests);
        page.WaitForAssertion(() => Assert.False(Switch(page, "mcp-server-start-at-sign-in").Instance.Checked));
    }

    [Fact]
    public void Without_a_package_the_switch_is_disabled_and_points_at_the_installed_app()
    {
        using var context = Context(new FakeStartup(StartupRegistrationState.Unavailable), out _);

        var signIn = Switch(context.Render<McpServerSettingsPage>(), "mcp-server-start-at-sign-in");

        Assert.True(signIn.Instance.Disabled);
        Assert.False(signIn.Instance.Checked);
        Assert.Equal("Available in the installed app", signIn.Instance.Description);
    }

    [Theory]
    [InlineData(StartupRegistrationState.DisabledByUser)]
    [InlineData(StartupRegistrationState.DisabledByPolicy)]
    public void Turned_off_outside_the_app_the_switch_is_disabled_and_points_at_task_manager(StartupRegistrationState state)
    {
        var startup = new FakeStartup(state);
        using var context = Context(startup, out _);
        var page = context.Render<McpServerSettingsPage>();

        var signIn = Switch(page, "mcp-server-start-at-sign-in");
        Assert.True(signIn.Instance.Disabled);
        Assert.False(signIn.Instance.Checked);
        Assert.Equal("Turned off in Task Manager's Startup apps", signIn.Instance.Description);
        Assert.True(page.Find("[data-testid='mcp-server-start-at-sign-in'] button").HasAttribute("disabled"));
        Assert.Empty(startup.Requests);
    }

    [Fact]
    public void A_host_without_a_startup_registration_draws_the_card_with_a_note_and_no_switches()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var page = context.Render<McpServerSettingsPage>();

        Assert.Single(page.FindAll("[data-testid='mcp-server-background-note']"));
        Assert.Empty(page.FindComponents<Toggle>());
    }

    private BunitContext Context(FakeStartup startup, out WorkspaceSettingsStore settings)
    {
        Directory.CreateDirectory(_root);
        settings = new WorkspaceSettingsStore(_root, Path.Combine(_root, "settings.json"), _ => null);

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<IStartupRegistration>(startup);
        context.Services.AddSingleton(settings);
        return context;
    }

    private static IRenderedComponent<Toggle> Switch(IRenderedComponent<McpServerSettingsPage> page, string testId) =>
        page.FindComponents<Toggle>().Single(toggle => toggle.Instance.TestId == testId);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>The startup task as the page sees it: Windows takes an enable or a
    /// disable while the task is the app's to change, and answers the state it ends in.</summary>
    private sealed class FakeStartup(StartupRegistrationState state) : IStartupRegistration
    {
        private StartupRegistrationState _state = state;

        public List<bool> Requests { get; } = [];

        public Task<StartupRegistrationState> GetStateAsync() => Task.FromResult(_state);

        public Task<StartupRegistrationState> SetEnabledAsync(bool enabled)
        {
            Requests.Add(enabled);
            if (_state is StartupRegistrationState.Enabled or StartupRegistrationState.Disabled)
                _state = enabled ? StartupRegistrationState.Enabled : StartupRegistrationState.Disabled;

            return Task.FromResult(_state);
        }
    }
}
