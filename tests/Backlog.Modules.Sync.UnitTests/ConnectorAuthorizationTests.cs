using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Features.Connector;
using Backlog.Modules.Sync.Features.Enrolment;
using Backlog.SharedKernel.Mcp;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// The connector's authorize request through to the code (local ADR 0058 §8): one client,
/// one exact redirect, S256 only, <c>inbox:capture</c> only, the configured owner only, and
/// every step usable once.
/// </summary>
public sealed class ConnectorAuthorizationTests
{
    private readonly ConnectorFixture _connector = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_valid_request_goes_to_GitHub_without_the_clients_state()
    {
        await _connector.Secret();

        var started = Assert.IsType<Ok<Uri>>((await _connector.Start.Handle(ConnectorFixture.Authorize(EnrolmentPkce.CreateVerifier()), Cancellation)).Value).Value;

        Assert.Equal("github.test", started.Host);
        Assert.NotNull(ConnectorFixture.Query(started, "state"));
        Assert.DoesNotContain(ConnectorFixture.ClientState, started.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_client_is_refused_in_place()
    {
        await _connector.Secret();

        var started = await _connector.Start.Handle(
            ConnectorFixture.Authorize(EnrolmentPkce.CreateVerifier()) with { ClientId = "someone-else" }, Cancellation);

        Assert.IsType<Error>(started.Value);
    }

    [Theory]
    [InlineData("https://claude.com/api/mcp/auth_callback")]
    [InlineData("https://claude.ai/api/mcp/auth_callback/")]
    [InlineData("http://claude.ai/api/mcp/auth_callback")]
    [InlineData("https://evil.example/api/mcp/auth_callback")]
    [InlineData(null)]
    public async Task A_redirect_other_than_the_registered_one_is_refused_in_place(string? redirect)
    {
        await _connector.Secret();

        var started = await _connector.Start.Handle(
            ConnectorFixture.Authorize(EnrolmentPkce.CreateVerifier()) with { RedirectUri = redirect }, Cancellation);

        Assert.IsType<Error>(started.Value);
    }

    [Fact]
    public async Task A_plain_challenge_is_redirected_back_as_invalid_request()
    {
        await _connector.Secret();
        var verifier = EnrolmentPkce.CreateVerifier();

        var answer = await RedirectedError(ConnectorFixture.Authorize(verifier) with { CodeChallenge = verifier, CodeChallengeMethod = "plain" });

        Assert.Equal(OAuthErrors.InvalidRequest, answer);
    }

    [Fact]
    public async Task A_missing_challenge_is_redirected_back_as_invalid_request()
    {
        await _connector.Secret();

        var answer = await RedirectedError(ConnectorFixture.Authorize(EnrolmentPkce.CreateVerifier()) with { CodeChallenge = null });

        Assert.Equal(OAuthErrors.InvalidRequest, answer);
    }

    [Fact]
    public async Task A_response_type_other_than_code_is_redirected_back()
    {
        await _connector.Secret();

        var answer = await RedirectedError(ConnectorFixture.Authorize(EnrolmentPkce.CreateVerifier()) with { ResponseType = "token" });

        Assert.Equal(OAuthErrors.UnsupportedResponseType, answer);
    }

    [Theory]
    [InlineData(McpScopes.TasksAdmin)]
    [InlineData(McpScopes.DevbookDecide)]
    [InlineData("inbox:capture tasks:read")]
    public async Task A_scope_outside_inbox_capture_is_redirected_back(string scope)
    {
        await _connector.Secret();

        var answer = await RedirectedError(ConnectorFixture.Authorize(EnrolmentPkce.CreateVerifier()) with { Scope = scope });

        Assert.Equal(OAuthErrors.InvalidScope, answer);
    }

    [Fact]
    public async Task A_resource_other_than_mcp_is_redirected_back()
    {
        await _connector.Secret();

        var answer = await RedirectedError(
            ConnectorFixture.Authorize(EnrolmentPkce.CreateVerifier()) with { Resource = "https://sync.test/api/sync" });

        Assert.Equal(OAuthErrors.InvalidTarget, answer);
    }

    [Fact]
    public async Task With_no_secret_the_client_is_unauthorized()
    {
        var answer = await RedirectedError(ConnectorFixture.Authorize(EnrolmentPkce.CreateVerifier()));

        Assert.Equal(OAuthErrors.UnauthorizedClient, answer);
    }

    [Fact]
    public async Task A_service_with_no_owner_authorizes_nobody()
    {
        var open = new ConnectorFixture(OwnerEnrolment.Open);

        var started = Assert.IsType<Ok<Uri>>((await open.Start.Handle(ConnectorFixture.Authorize(EnrolmentPkce.CreateVerifier()), Cancellation)).Value).Value;

        Assert.Equal(OAuthErrors.TemporarilyUnavailable, ConnectorFixture.Query(started, "error"));
    }

    [Fact]
    public async Task A_state_the_connector_did_not_start_is_left_to_enrolment()
    {
        var outcome = Assert.IsType<Error>((await _connector.SignIn.Handle(new CompleteConnectorSignInCommand("an-enrolment-state", ConnectorFixture.OwnerCode), Cancellation)).Value);

        Assert.Equal(SyncErrorCodes.ConnectorSignInUnknown, outcome.Code);
    }

    [Theory]
    [InlineData(ConnectorFixture.StrangerCode)]
    [InlineData("a-code-github-rejects")]
    [InlineData("")]
    public async Task Anyone_but_the_owner_is_sent_back_denied_without_a_code(string gitHubCode)
    {
        await _connector.Secret();
        var state = await _connector.StartedSignIn(EnrolmentPkce.CreateVerifier());

        var outcome = Assert.IsType<Ok<ConnectorSignInOutcome>>((await _connector.SignIn.Handle(new CompleteConnectorSignInCommand(state, gitHubCode), Cancellation)).Value).Value;

        var back = outcome.Redirect!;
        Assert.StartsWith(ConnectorSettings.DefaultRedirectUri + "?", back.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(OAuthErrors.AccessDenied, ConnectorFixture.Query(back, "error"));
        Assert.Equal(ConnectorFixture.ClientState, ConnectorFixture.Query(back, "state"));
        Assert.Null(ConnectorFixture.Query(back, "code"));
        Assert.Null(outcome.ConsentTicket);
    }

    [Fact]
    public async Task The_owners_sign_in_leads_to_consent_once()
    {
        await _connector.Secret();
        var state = await _connector.StartedSignIn(EnrolmentPkce.CreateVerifier());

        var first = Assert.IsType<Ok<ConnectorSignInOutcome>>((await _connector.SignIn.Handle(new CompleteConnectorSignInCommand(state, ConnectorFixture.OwnerCode), Cancellation)).Value).Value;
        var replay = await _connector.SignIn.Handle(new CompleteConnectorSignInCommand(state, ConnectorFixture.OwnerCode), Cancellation);

        Assert.NotNull(first.ConsentTicket);
        Assert.IsType<Error>(replay.Value);
    }

    [Fact]
    public async Task The_consent_prompt_names_the_client_and_each_scope()
    {
        await _connector.Secret();
        var ticket = await _connector.ConsentTicket(EnrolmentPkce.CreateVerifier());

        var prompt = Assert.IsType<Ok<ConnectorConsentPrompt>>((await _connector.DescribeConsent.Handle(new DescribeConnectorConsentQuery(ticket), Cancellation)).Value).Value;

        Assert.Equal("Claude", prompt.ClientName);
        Assert.Equal("Add items to your Backlog Inbox.", Assert.Single(prompt.Scopes).Description);
    }

    [Fact]
    public async Task Allow_sends_a_code_and_the_state_back_once()
    {
        await _connector.Secret();
        var ticket = await _connector.ConsentTicket(EnrolmentPkce.CreateVerifier());

        var allowed = Assert.IsType<Ok<Uri>>((await _connector.Decide.Handle(new DecideConnectorConsentCommand(ticket, Allow: true), Cancellation)).Value).Value;
        var again = await _connector.Decide.Handle(new DecideConnectorConsentCommand(ticket, Allow: true), Cancellation);

        Assert.StartsWith(ConnectorSettings.DefaultRedirectUri + "?", allowed.AbsoluteUri, StringComparison.Ordinal);
        Assert.NotNull(ConnectorFixture.Query(allowed, "code"));
        Assert.Equal(ConnectorFixture.ClientState, ConnectorFixture.Query(allowed, "state"));
        Assert.IsType<Error>(again.Value);
    }

    [Fact]
    public async Task Deny_sends_access_denied_back_and_no_code()
    {
        await _connector.Secret();
        var ticket = await _connector.ConsentTicket(EnrolmentPkce.CreateVerifier());

        var denied = Assert.IsType<Ok<Uri>>((await _connector.Decide.Handle(new DecideConnectorConsentCommand(ticket, Allow: false), Cancellation)).Value).Value;

        Assert.Equal(OAuthErrors.AccessDenied, ConnectorFixture.Query(denied, "error"));
        Assert.Null(ConnectorFixture.Query(denied, "code"));
    }

    private async Task<string?> RedirectedError(StartConnectorAuthorizationCommand command)
    {
        var started = Assert.IsType<Ok<Uri>>((await _connector.Start.Handle(command, Cancellation)).Value).Value;

        Assert.StartsWith(ConnectorSettings.DefaultRedirectUri + "?", started.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(ConnectorFixture.ClientState, ConnectorFixture.Query(started, "state"));
        return ConnectorFixture.Query(started, "error");
    }
}
