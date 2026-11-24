using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Adapters;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features.Connector;
using Backlog.Modules.Sync.Features.Enrolment;
using Backlog.Modules.Sync.Ports;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// The connector's authorization server, wired the way <c>AddSyncModule</c> wires it, with a
/// clock a test can move, a GitHub that answers a user id per code, and a token issuer that
/// records the claims it was asked to sign. Built per test.
/// </summary>
internal sealed class ConnectorFixture
{
    internal const long OwnerGitHubId = StubGitHubOwnerSignIn.OwnerGitHubId;
    internal const string OwnerCode = StubGitHubOwnerSignIn.OwnerCode;
    internal const string StrangerCode = StubGitHubOwnerSignIn.StrangerCode;
    internal const string Resource = "https://sync.test/mcp";
    internal const string ClientState = "claude-state";

    /// <param name="owner">The configured owner; the GitHub owner by default.</param>
    /// <param name="refreshTokens">Wraps the refresh-token store the token handlers use, so a
    /// test can interleave a concurrent caller with theirs.</param>
    internal ConnectorFixture(
        OwnerEnrolment? owner = null,
        Func<IConnectorRefreshTokenStore, IConnectorRefreshTokenStore>? refreshTokens = null)
    {
        Owner = owner ?? new OwnerEnrolment(OwnerGitHubId);
        Clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        Hasher = new Sha256CredentialHasher();
        Authorizations = new InMemoryConnectorAuthorizationStore(Clock);
        Clients = new InMemoryConnectorClientStore();
        RefreshTokens = new InMemoryConnectorRefreshTokenStore();
        Tokens = new RecordingConnectorTokenIssuer(Clock);
        Settings = new ConnectorSettings(
            ConnectorSettings.DefaultClientId, ConnectorSettings.DefaultRedirectUri, Resource, TimeSpan.FromDays(30));

        Start = new StartConnectorAuthorizationCommandHandler(Authorizations, Clients, new StubGitHubOwnerSignIn(), Owner, Settings, Hasher, Clock);
        SignIn = new CompleteConnectorSignInCommandHandler(
            Authorizations, new StubGitHubOwnerSignIn(), Owner, Hasher, Clock, NullLogger<CompleteConnectorSignInCommandHandler>.Instance);
        DescribeConsent = new DescribeConnectorConsentQueryHandler(Authorizations, Settings, Hasher, Clock);
        Decide = new DecideConnectorConsentCommandHandler(Authorizations, Hasher, Clock);
        var tokenStore = refreshTokens?.Invoke(RefreshTokens) ?? RefreshTokens;
        Exchange = new ExchangeAuthorizationCodeCommandHandler(
            Authorizations, Clients, tokenStore, Tokens, Settings, Hasher, Clock, NullLogger<ExchangeAuthorizationCodeCommandHandler>.Instance);
        Refresh = RefreshUnder(Owner, Settings, tokenStore);
        CreateSecret = new CreateConnectorSecretCommandHandler(
            Clients, Owner, Settings, Hasher, Clock, NullLogger<CreateConnectorSecretCommandHandler>.Instance);
        RevokeSecret = new RevokeConnectorSecretCommandHandler(
            Clients, Owner, Settings, NullLogger<RevokeConnectorSecretCommandHandler>.Instance);
        Describe = new DescribeConnectorQueryHandler(Clients, Owner, Settings);
    }

    internal OwnerEnrolment Owner { get; }

    internal FakeTimeProvider Clock { get; }

    internal Sha256CredentialHasher Hasher { get; }

    internal InMemoryConnectorAuthorizationStore Authorizations { get; }

    internal InMemoryConnectorClientStore Clients { get; }

    internal InMemoryConnectorRefreshTokenStore RefreshTokens { get; }

    internal RecordingConnectorTokenIssuer Tokens { get; }

    internal ConnectorSettings Settings { get; }

    internal StartConnectorAuthorizationCommandHandler Start { get; }

    internal CompleteConnectorSignInCommandHandler SignIn { get; }

    internal DescribeConnectorConsentQueryHandler DescribeConsent { get; }

    internal DecideConnectorConsentCommandHandler Decide { get; }

    internal ExchangeAuthorizationCodeCommandHandler Exchange { get; }

    internal RefreshConnectorTokenCommandHandler Refresh { get; }

    internal CreateConnectorSecretCommandHandler CreateSecret { get; }

    internal RevokeConnectorSecretCommandHandler RevokeSecret { get; }

    internal DescribeConnectorQueryHandler Describe { get; }

    /// <summary>A refresh handler over this fixture's stores, as a service configured with
    /// <paramref name="owner"/> and <paramref name="settings"/> would build it — a restart
    /// after the configuration changed.</summary>
    internal RefreshConnectorTokenCommandHandler RefreshUnder(
        OwnerEnrolment owner, ConnectorSettings settings, IConnectorRefreshTokenStore? store = null) =>
        new(Clients, store ?? RefreshTokens, Tokens, owner, settings, Hasher, Clock, NullLogger<RefreshConnectorTokenCommandHandler>.Instance);

    /// <summary>A device of the configured owner, as its token would scope it.</summary>
    internal OwnerScope OwnerDevice => new(Owner.Owner!.Value, new DeviceId(Guid.CreateVersion7()));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>The owner makes a secret and the test keeps its plaintext.</summary>
    internal async Task<string> Secret()
    {
        var created = Assert.IsType<Ok<ConnectorSecretResponse>>((await CreateSecret.Handle(new CreateConnectorSecretCommand(OwnerDevice), Cancellation)).Value).Value;
        return created.ClientSecret;
    }

    /// <summary>A well-formed authorize request for <paramref name="verifier"/>.</summary>
    internal static StartConnectorAuthorizationCommand Authorize(string verifier) => new(
        ResponseType: "code",
        ClientId: ConnectorSettings.DefaultClientId,
        RedirectUri: ConnectorSettings.DefaultRedirectUri,
        CodeChallenge: EnrolmentPkce.Challenge(verifier),
        CodeChallengeMethod: EnrolmentPkce.Method,
        Scope: "inbox:capture",
        State: ClientState,
        Resource: Resource);

    /// <summary>The authorize request through GitHub, answered with the service state GitHub carried.</summary>
    internal async Task<string> StartedSignIn(string verifier)
    {
        var started = Assert.IsType<Ok<Uri>>((await Start.Handle(Authorize(verifier), Cancellation)).Value).Value;
        Assert.Equal("github.test", started.Host);
        return Query(started, "state")!;
    }

    /// <summary>The owner's sign-in up to the consent page, answered with its ticket.</summary>
    internal async Task<string> ConsentTicket(string verifier)
    {
        var state = await StartedSignIn(verifier);
        var outcome = Assert.IsType<Ok<ConnectorSignInOutcome>>((await SignIn.Handle(new CompleteConnectorSignInCommand(state, OwnerCode), Cancellation)).Value).Value;
        return outcome.ConsentTicket!;
    }

    /// <summary>The whole authorization as the owner, ending in the code claude.ai receives.</summary>
    internal async Task<string> AuthorizationCode(string verifier)
    {
        var ticket = await ConsentTicket(verifier);
        var decided = Assert.IsType<Ok<Uri>>((await Decide.Handle(new DecideConnectorConsentCommand(ticket, Allow: true), Cancellation)).Value).Value;
        return Query(decided, "code")!;
    }

    /// <summary>A code exchanged for tokens under <paramref name="secret"/>.</summary>
    internal async Task<Result<Backlog.Modules.Sync.Abstractions.DataTransferObjects.ConnectorTokenResponse>> Exchanged(string? secret)
    {
        var verifier = EnrolmentPkce.CreateVerifier();
        var code = await AuthorizationCode(verifier);
        return await Exchange.Handle(
            new ExchangeAuthorizationCodeCommand(Credentials(secret), code, ConnectorSettings.DefaultRedirectUri, verifier),
            Cancellation);
    }

    internal static ConnectorClientCredentials Credentials(string? secret) => new(ConnectorSettings.DefaultClientId, secret);

    internal static string? Query(Uri address, string name) =>
        address.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .Where(pair => pair[0] == name)
            .Select(pair => Uri.UnescapeDataString(pair.Length > 1 ? pair[1] : string.Empty))
            .FirstOrDefault();
}

/// <summary>Records the claims of every access token it was asked to sign.</summary>
internal sealed class RecordingConnectorTokenIssuer(TimeProvider clock) : IConnectorTokenIssuer
{
    private readonly List<ConnectorTokenClaims> _issued = [];

    internal IReadOnlyList<ConnectorTokenClaims> Issued => _issued;

    public ConnectorAccessToken Issue(ConnectorTokenClaims claims)
    {
        _issued.Add(claims);
        return new ConnectorAccessToken($"access-{_issued.Count}", clock.GetUtcNow().AddMinutes(15));
    }
}
