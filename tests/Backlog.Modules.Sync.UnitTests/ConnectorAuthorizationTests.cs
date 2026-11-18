using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Features.Connector;
using Backlog.Modules.Sync.Features.Enrolment;
using Backlog.SharedKernel.Mcp;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// The connector's authorize request through to the code (local ADR 0052 §8): one client,
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

        var started = await _connector.Start.Handle(ConnectorFixture.Authorize(EnrolmentPkce.CreateVerifier()), Cancellation);

        Assert.True(started.IsSuccess);
        Assert.Equal("github.test", started.Value.Host);
        Assert.NotNull(ConnectorFixture.Query(started.Value, "state"));
        Assert.DoesNotContain(ConnectorFixture.ClientState, started.Value.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_client_is_refused_in_place()
    {
        await _connector.Secret();

        var started = await _connector.Start.Handle(
            ConnectorFixture.Authorize(EnrolmentPkce.CreateVerifier()) with { ClientId = "someone-else" }, Cancellation);

        Assert.True(started.IsFailure);
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

        Assert.True(started.IsFailure);
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

        var started = await open.Start.Handle(ConnectorFixture.Authorize(EnrolmentPkce.CreateVerifier()), Cancellation);

        Assert.True(started.IsSuccess);
        Assert.Equal(OAuthErrors.TemporarilyUnavailable, ConnectorFixture.Query(started.Value, "error"));
    }

    [Fact]
    public async Task A_state_the_connector_did_not_start_is_left_to_enrolment()
    {
        var outcome = await _connector.SignIn.Handle(new CompleteConnectorSignInCommand("an-enrolment-state", ConnectorFixture.OwnerCode), Cancellation);

        Assert.True(outcome.IsFailure);
        Assert.Equal(SyncErrorCodes.ConnectorSignInUnknown, outcome.Error.Code);
    }

    [Theory]
    [InlineData(ConnectorFixture.StrangerCode)]
    [InlineData("a-code-github-rejects")]
    [InlineData("")]
    public async Task Anyone_but_the_owner_is_sent_back_denied_without_a_code(string gitHubCode)
    {
        await _connector.Secret();
        var state = await _connector.StartedSignIn(EnrolmentPkce.CreateVerifier());

        var outcome = await _connector.SignIn.Handle(new CompleteConnectorSignInCommand(state, gitHubCode), Cancellation);

        var back = outcome.Value.Redirect!;
        Assert.StartsWith(ConnectorSettings.DefaultRedirectUri + "?", back.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(OAuthErrors.AccessDenied, ConnectorFixture.Query(back, "error"));
        Assert.Equal(ConnectorFixture.ClientState, ConnectorFixture.Query(back, "state"));
        Assert.Null(ConnectorFixture.Query(back, "code"));
        Assert.Null(outcome.Value.ConsentTicket);
    }

    [Fact]
    public async Task The_owners_sign_in_leads_to_consent_once()
    {
        await _connector.Secret();
        var state = await _connector.StartedSignIn(EnrolmentPkce.CreateVerifier());

        var first = await _connector.SignIn.Handle(new CompleteConnectorSignInCommand(state, ConnectorFixture.OwnerCode), Cancellation);
        var replay = await _connector.SignIn.Handle(new CompleteConnectorSignInCommand(state, ConnectorFixture.OwnerCode), Cancellation);

        Assert.NotNull(first.Value.ConsentTicket);
        Assert.True(replay.IsFailure);
    }

    [Fact]
    public async Task The_consent_prompt_names_the_client_and_each_scope()
    {
        await _connector.Secret();
        var ticket = await _connector.ConsentTicket(EnrolmentPkce.CreateVerifier());

        var prompt = await _connector.DescribeConsent.Handle(new DescribeConnectorConsentQuery(ticket), Cancellation);

        Assert.Equal("Claude", prompt.Value.ClientName);
        Assert.Equal("Add items to your Backlog Inbox.", Assert.Single(prompt.Value.Scopes).Description);
    }

    [Fact]
    public async Task Allow_sends_a_code_and_the_state_back_once()
    {
        await _connector.Secret();
        var ticket = await _connector.ConsentTicket(EnrolmentPkce.CreateVerifier());

        var allowed = await _connector.Decide.Handle(new DecideConnectorConsentCommand(ticket, Allow: true), Cancellation);
        var again = await _connector.Decide.Handle(new DecideConnectorConsentCommand(ticket, Allow: true), Cancellation);

        Assert.StartsWith(ConnectorSettings.DefaultRedirectUri + "?", allowed.Value.AbsoluteUri, StringComparison.Ordinal);
        Assert.NotNull(ConnectorFixture.Query(allowed.Value, "code"));
        Assert.Equal(ConnectorFixture.ClientState, ConnectorFixture.Query(allowed.Value, "state"));
        Assert.True(again.IsFailure);
    }

    [Fact]
    public async Task Deny_sends_access_denied_back_and_no_code()
    {
        await _connector.Secret();
        var ticket = await _connector.ConsentTicket(EnrolmentPkce.CreateVerifier());

        var denied = await _connector.Decide.Handle(new DecideConnectorConsentCommand(ticket, Allow: false), Cancellation);

        Assert.Equal(OAuthErrors.AccessDenied, ConnectorFixture.Query(denied.Value, "error"));
        Assert.Null(ConnectorFixture.Query(denied.Value, "code"));
    }

    private async Task<string?> RedirectedError(StartConnectorAuthorizationCommand command)
    {
        var started = await _connector.Start.Handle(command, Cancellation);

        Assert.True(started.IsSuccess);
        Assert.StartsWith(ConnectorSettings.DefaultRedirectUri + "?", started.Value.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(ConnectorFixture.ClientState, ConnectorFixture.Query(started.Value, "state"));
        return ConnectorFixture.Query(started.Value, "error");
    }
}
