namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The act, and the two things about it that are structural rather than
/// cosmetic: that it cannot be made inert without a cause, and that the cause
/// reaches both a pointer and a screen reader.
///
/// <para>Everything else in this file follows from the same idea — the host owns
/// the lifecycle and hands it in, so each state is asserted as a rendering of a
/// value rather than as the outcome of a sequence of clicks.</para>
/// </summary>
public sealed class IntegrationActionTests
{
    private static readonly IntegrationActionSpec CreateIssue =
        new("create-issue", "Create GitHub issue", IntegrationProvider.GitHub);

    [Fact]
    public void An_act_that_is_not_available_renders_no_enabled_button()
    {
        // There is no Disabled parameter to set, so this is the only route to an
        // inert integration act — and it is a route that cannot be taken without
        // saying why.
        using var context = new BunitContext();

        var action = context.Render<IntegrationAction>(parameters => parameters
            .Add(a => a.Action, CreateIssue with { Readiness = IntegrationReadiness.NotAuthorized("GitHub") })
            .Add(a => a.ButtonTestId, "act"));

        var button = action.Find("[data-testid='act']");

        Assert.True(button.HasAttribute("disabled"));
        Assert.Empty(action.FindAll("button:not([disabled])"));
    }

    [Fact]
    public void The_button_is_never_removed_by_being_unavailable()
    {
        // A reader who cannot find the control concludes the product cannot do
        // the thing at all, which is a different and worse belief than "not right
        // now, and here is why".
        using var context = new BunitContext();

        var action = context.Render<IntegrationAction>(parameters => parameters
            .Add(a => a.Action, CreateIssue with { Readiness = IntegrationReadiness.Offline() })
            .Add(a => a.ButtonTestId, "act"));

        Assert.Single(action.FindAll("[data-testid='act']"));
    }

    [Fact]
    public void The_reason_reaches_both_the_title_and_aria_describedby()
    {
        // Neither route alone reaches everybody: a title is not reliably
        // announced, and a described-by is invisible.
        using var context = new BunitContext();

        var action = context.Render<IntegrationAction>(parameters => parameters
            .Add(a => a.Action, CreateIssue with { Readiness = IntegrationReadiness.NotAuthorized("GitHub") })
            .Add(a => a.ButtonTestId, "act")
            .Add(a => a.ReasonTestId, "reason"));

        var button = action.Find("[data-testid='act']");
        var reason = action.Find("[data-testid='reason']");

        Assert.Equal("GitHub is not connected.", reason.TextContent);
        Assert.Equal("GitHub is not connected.", button.GetAttribute("title"));
        Assert.Equal(reason.GetAttribute("id"), button.GetAttribute("aria-describedby"));
        Assert.False(string.IsNullOrWhiteSpace(reason.GetAttribute("id")));
    }

    [Fact]
    public void A_cause_with_no_sentence_still_gets_one()
    {
        // The whole argument against an optional DisabledReason: optional means a
        // host can skip it, and every host eventually will.
        using var context = new BunitContext();

        var action = context.Render<IntegrationAction>(parameters => parameters
            .Add(a => a.Action, CreateIssue with { Readiness = new IntegrationReadiness(IntegrationAvailability.NotInstalled, "VS Code") })
            .Add(a => a.ReasonTestId, "reason"));

        Assert.Equal("VS Code is not installed on this machine.", action.Find("[data-testid='reason']").TextContent);
    }

    [Fact]
    public void A_remedy_renders_only_where_there_is_something_behind_it()
    {
        using var context = new BunitContext();
        var readiness = IntegrationReadiness.NotAuthorized("GitHub");

        var without = context.Render<IntegrationAction>(parameters => parameters
            .Add(a => a.Action, CreateIssue with { Readiness = readiness })
            .Add(a => a.RemedyTestId, "remedy"));

        Assert.Empty(without.FindAll("[data-testid='remedy']"));

        IntegrationReadiness? remedied = null;

        var with = context.Render<IntegrationAction>(parameters => parameters
            .Add(a => a.Action, CreateIssue with { Readiness = readiness })
            .Add(a => a.OnRemedy, r => remedied = r)
            .Add(a => a.RemedyTestId, "remedy"));

        var remedy = with.Find("[data-testid='remedy']");
        Assert.Equal("Connect GitHub", remedy.TextContent.Trim());

        remedy.Click();

        // The readiness rather than the act: four acts blocked by one expired
        // token have one thing to fix between them.
        Assert.Equal(readiness, remedied);
    }

    [Fact]
    public void Running_is_AppButtons_own_busy_state_and_keeps_the_button()
    {
        // Not a second loading button. AppButton already embeds the spinner, sets
        // aria-busy and disables without removing the control from the tab order.
        using var context = new BunitContext();

        var action = context.Render<IntegrationAction>(parameters => parameters
            .Add(a => a.Action, CreateIssue with { State = IntegrationActionState.Running })
            .Add(a => a.ButtonTestId, "act"));

        var button = action.Find("[data-testid='act']");

        Assert.Equal("true", button.GetAttribute("aria-busy"));
        Assert.True(button.HasAttribute("disabled"));
        Assert.Single(action.FindAll(".spinner"));
    }

    [Fact]
    public void Succeeding_is_a_status_and_failing_is_an_alert()
    {
        // Toast already draws this line between Error/Warning and everything
        // else; this follows it rather than inventing a second rule.
        using var context = new BunitContext();

        var succeeded = context.Render<IntegrationAction>(parameters => parameters
            .Add(a => a.Action, CreateIssue with { State = IntegrationActionState.Succeeded })
            .Add(a => a.StatusTestId, "status"));

        Assert.Equal("status", succeeded.Find("[data-testid='status']").GetAttribute("role"));

        var failed = context.Render<IntegrationAction>(parameters => parameters
            .Add(a => a.Action, CreateIssue with { State = IntegrationActionState.Failed })
            .Add(a => a.StatusTestId, "status"));

        Assert.Equal("alert", failed.Find("[data-testid='status']").GetAttribute("role"));
    }

    [Fact]
    public void A_succeeded_act_leaves_the_consequence_where_a_reader_can_reach_it()
    {
        // A copy is finished the moment the clipboard returns; creating an issue
        // produces an issue, and a line that says "done" and vanishes takes the
        // link to it with it.
        using var context = new BunitContext();

        var action = context.Render<IntegrationAction>(parameters => parameters
            .Add(a => a.Action, CreateIssue with { State = IntegrationActionState.Succeeded })
            .Add(a => a.SuccessContent, builder =>
            {
                builder.OpenElement(0, "a");
                builder.AddAttribute(1, "href", "https://example.invalid/128");
                builder.AddContent(2, "Issue #128 created");
                builder.CloseElement();
            })
            .Add(a => a.StatusTestId, "status"));

        Assert.Equal("Issue #128 created", action.Find("[data-testid='status'] a").TextContent);
    }

    [Fact]
    public void Retry_raises_its_own_callback_rather_than_re_raising_invoke()
    {
        // So a host can tell a first attempt from a second in whatever it logs.
        using var context = new BunitContext();
        var invoked = 0;
        var retried = 0;

        var action = context.Render<IntegrationAction>(parameters => parameters
            .Add(a => a.Action, CreateIssue with { State = IntegrationActionState.Failed })
            .Add(a => a.OnInvoke, _ => invoked++)
            .Add(a => a.OnRetry, _ => retried++)
            .Add(a => a.RetryTestId, "retry"));

        action.Find("[data-testid='retry']").Click();

        Assert.Equal(0, invoked);
        Assert.Equal(1, retried);
    }

    [Fact]
    public void Confirming_is_a_button_with_a_cancel_beside_it()
    {
        using var context = new BunitContext();
        IntegrationActionSpec? cancelled = null;

        var action = context.Render<IntegrationAction>(parameters => parameters
            .Add(a => a.Action, CreateIssue with
            {
                State = IntegrationActionState.Confirming,
                ConfirmLabel = "Create the issue"
            })
            .Add(a => a.OnCancel, spec => cancelled = spec)
            .Add(a => a.ConfirmTestId, "confirm")
            .Add(a => a.CancelTestId, "cancel"));

        var confirm = action.Find("[data-testid='confirm']");

        Assert.Contains("Create the issue", confirm.TextContent, StringComparison.Ordinal);

        // The accessible name never collapses to the confirm word alone: on its
        // own, "Create the issue" says nothing about which issue to a reader who
        // arrived here by keyboard.
        Assert.Contains("Create GitHub issue", confirm.GetAttribute("aria-label") ?? string.Empty, StringComparison.Ordinal);

        action.Find("[data-testid='cancel']").Click();

        Assert.Equal("create-issue", cancelled?.Id);
    }

    [Fact]
    public void Compact_hides_the_label_and_keeps_the_name()
    {
        // Icon-only means visually icon-only. IconButton already sets the
        // convention that Title falls back to the accessible name, and this
        // copies the convention rather than the markup.
        using var context = new BunitContext();

        var action = context.Render<IntegrationAction>(parameters => parameters
            .Add(a => a.Action, CreateIssue)
            .Add(a => a.Density, IntegrationDensity.Compact)
            .Add(a => a.ButtonTestId, "act"));

        var button = action.Find("[data-testid='act']");

        Assert.DoesNotContain("Create GitHub issue", button.TextContent, StringComparison.Ordinal);
        Assert.Equal("Create GitHub issue", button.GetAttribute("aria-label"));
        Assert.Equal("Create GitHub issue", button.GetAttribute("title"));

        // The compact stem is IconButton's own, so the shape is unchanged rather
        // than similar.
        Assert.Contains("btn--icon", button.GetAttribute("class") ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void An_act_with_no_provider_still_has_an_icon_at_compact_density()
    {
        // An icon-only control with no icon is the one shape this component must
        // never produce.
        using var context = new BunitContext();

        var action = context.Render<IntegrationAction>(parameters => parameters
            .Add(a => a.Action, new IntegrationActionSpec("ask", "Ask AI"))
            .Add(a => a.Density, IntegrationDensity.Compact)
            .Add(a => a.ButtonTestId, "act"));

        Assert.Single(action.FindAll("[data-testid='act'] svg"));
    }

    [Fact]
    public void The_component_advances_nothing_on_its_own()
    {
        // The host holds the truth and hands it back — the same discipline
        // MarkdownView keeps with a comment.
        using var context = new BunitContext();

        var action = context.Render<IntegrationAction>(parameters => parameters
            .Add(a => a.Action, CreateIssue)
            .Add(a => a.OnInvoke, _ => { })
            .Add(a => a.ButtonTestId, "act")
            .Add(a => a.CancelTestId, "cancel"));

        action.Find("[data-testid='act']").Click();

        // Still idle: no Cancel appeared, because nothing here decided the act
        // had moved on.
        Assert.Empty(action.FindAll("[data-testid='cancel']"));
    }
}
