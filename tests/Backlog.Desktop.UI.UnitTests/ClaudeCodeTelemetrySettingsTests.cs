using System.Text.Json;

using Backlog.Modules.Sessions.UI;
using Backlog.Modules.Sessions.UI.Extensions;

using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Claude Code settings page (local ADR 0024): the endpoint URL to paste into
/// Claude Code, and the settings block that sends Claude Code's logs there.
/// </summary>
public sealed class ClaudeCodeTelemetrySettingsTests
{
    private sealed class FixedEndpoint(Uri endpoint, string token) : IClaudeCodeTelemetryEndpoint
    {
        public Uri LogsEndpoint { get; } = endpoint;

        public string EnsureToken() => token;
    }

    private static IRenderedComponent<ClaudeCodeTelemetrySettings> Render(BunitContext context)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context.Render<ClaudeCodeTelemetrySettings>();
    }

    [Fact]
    public void The_desktop_endpoint_is_shown_with_the_token_the_listener_checks()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IClaudeCodeTelemetryEndpoint>(
            new FixedEndpoint(new Uri("http://127.0.0.1:5757/v1/logs"), "secret-token"));

        var page = Render(context);

        Assert.Equal("http://127.0.0.1:5757/v1/logs", page.Find("[data-testid='claude-code-telemetry-endpoint']").TextContent.Trim());

        // On screen the token is hidden; the copied block carries it.
        var shown = page.Find("[data-testid='claude-code-telemetry-settings']").TextContent;
        Assert.Contains("OTEL_EXPORTER_OTLP_LOGS_ENDPOINT", shown, StringComparison.Ordinal);
        Assert.Contains("Authorization=Bearer " + ClaudeCodeTelemetrySnippet.HiddenToken, shown, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", page.Markup, StringComparison.Ordinal);

        var copy = page.FindComponents<Backlog.UI.Components.Buttons.CopyButton>()
            .Single(button => button.Instance.TestId == "claude-code-telemetry-settings-copy");
        Assert.Contains("Authorization=Bearer secret-token", copy.Instance.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_host_with_no_endpoint_of_its_own_serves_it_on_the_page_s_origin_without_a_token()
    {
        using var context = new BunitContext();

        var page = Render(context);

        Assert.Equal("http://localhost/v1/logs", page.Find("[data-testid='claude-code-telemetry-endpoint']").TextContent.Trim());
        Assert.DoesNotContain("Authorization", page.Find("[data-testid='claude-code-telemetry-settings']").TextContent, StringComparison.Ordinal);
        Assert.Contains("no token", page.Find("[data-testid='claude-code-telemetry-note']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_settings_block_is_valid_json_with_the_logs_signal_s_own_variables()
    {
        var json = ClaudeCodeTelemetrySnippet.Settings(new Uri("http://127.0.0.1:5757/v1/logs"), "t");

        using var document = JsonDocument.Parse(json);
        var env = document.RootElement.GetProperty("env");

        Assert.Equal("1", env.GetProperty("CLAUDE_CODE_ENABLE_TELEMETRY").GetString());
        Assert.Equal("otlp", env.GetProperty("OTEL_LOGS_EXPORTER").GetString());
        Assert.Equal("http/protobuf", env.GetProperty("OTEL_EXPORTER_OTLP_LOGS_PROTOCOL").GetString());
        Assert.Equal("http://127.0.0.1:5757/v1/logs", env.GetProperty("OTEL_EXPORTER_OTLP_LOGS_ENDPOINT").GetString());
        Assert.Equal("Authorization=Bearer t", env.GetProperty("OTEL_EXPORTER_OTLP_LOGS_HEADERS").GetString());
        Assert.False(env.TryGetProperty("OTEL_EXPORTER_OTLP_HEADERS", out _));
    }

    [Fact]
    public void The_page_is_offered_behind_the_sessions_switch()
    {
        var services = new ServiceCollection();
        services.AddSessionsSettings();

        var section = Assert.Single(services.Select(d => d.ImplementationInstance).OfType<SettingsSection>());

        Assert.Equal("claude-code", section.Id);
        Assert.Equal(typeof(ClaudeCodeTelemetrySettings), section.Component);
        Assert.Equal(SessionFeatures.Sessions, section.FeatureKey);
    }
}
