using Backlog.UI.Components.Settings;

using Bunit;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// <see cref="AgentAccessRequest"/>: an agent's request for access with Decline and Allow with
/// Hello the only answers — as a row whose Trim scopes opens the switches, and as the request
/// window whose switches are always open.
/// </summary>
public sealed class AgentAccessRequestTests
{
    private static readonly IReadOnlyList<AgentScopeOption> Asked =
    [
        new("tasks:read", "Find and read items"),
        new("agents:grant", "Grant other agents"),
    ];

    [Fact]
    public void A_row_shows_its_scopes_as_chips_a_trimmed_one_reading_off_and_Trim_opens_the_switches()
    {
        using var context = new BunitContext();
        var trimmed = 0;

        var row = context.Render<AgentAccessRequest>(parameters => parameters
            .Add(request => request.Name, "Copilot CLI")
            .Add(request => request.Asked, Asked)
            .Add(request => request.Selected, ["tasks:read"])
            .Add(request => request.Trimmable, true)
            .Add(request => request.OnTrim, () => trimmed++));

        Assert.Equal(["tasks:read", "agents:grant · off"], row.FindAll("[data-testid='agent-request-scope']").Select(chip => chip.TextContent.Trim()));
        Assert.Empty(row.FindAll("[data-testid='agent-request-switches']"));
        Assert.Equal("listitem", row.Find(".agent-request").GetAttribute("role"));

        row.Find("[data-testid='agent-request-trim']").Click();
        Assert.Equal(1, trimmed);

        row.Render(parameters => parameters.Add(request => request.Trimming, true));
        Assert.NotEmpty(row.FindAll("[data-testid='agent-request-switches']"));
        Assert.Equal("Done", row.Find("[data-testid='agent-request-trim']").TextContent.Trim());
    }

    [Fact]
    public void The_window_shape_has_its_switches_open_and_only_Decline_and_Allow()
    {
        using var context = new BunitContext();
        var allowed = 0;
        var declined = 0;

        var window = context.Render<AgentAccessRequest>(parameters => parameters
            .Add(request => request.Name, "Claude Code")
            .Add(request => request.Asked, Asked)
            .Add(request => request.Selected, ["tasks:read"])
            .Add(request => request.OnAllow, () => allowed++)
            .Add(request => request.OnDecline, () => declined++));

        Assert.NotEmpty(window.FindAll("[data-testid='agent-request-switches']"));
        Assert.Empty(window.FindAll("[data-testid='agent-request-trim']"));
        Assert.Equal(2, window.FindAll(".agent-row__actions button").Count);

        window.Find("[data-testid='agent-request-allow']").Click();
        window.Find("[data-testid='agent-request-decline']").Click();

        Assert.Equal((1, 1), (allowed, declined));
    }

    [Fact]
    public void Allow_is_disabled_while_no_scope_is_on_and_a_refusal_shows_as_a_status()
    {
        using var context = new BunitContext();

        var window = context.Render<AgentAccessRequest>(parameters => parameters
            .Add(request => request.Name, "Claude Code")
            .Add(request => request.Asked, Asked)
            .Add(request => request.Selected, Array.Empty<string>())
            .Add(request => request.Message, "Windows Hello is not available here.")
            .Add(request => request.MessageIsError, true));

        Assert.True(window.Find("[data-testid='agent-request-allow']").HasAttribute("disabled"));
        var status = window.Find("[data-testid='agent-request-status']");
        Assert.Contains("Windows Hello is not available", status.TextContent, StringComparison.Ordinal);
        Assert.Contains("agent-request__status--error", status.ClassName, StringComparison.Ordinal);
    }
}
