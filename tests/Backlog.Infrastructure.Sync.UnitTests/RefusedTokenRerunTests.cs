using System.Net;

using Backlog.SharedKernel.Results;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The re-run rule on its own, for the two cases no loop's fixture can reach:
/// the loops' own tests cover a token refused once, twice, and not at all.
/// </summary>
public sealed class RefusedTokenRerunTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    private static readonly DeviceCredential Paired = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "Workshop PC",
        "a-registration-credential");

    private static readonly Error Unauthorized = new("sync.unauthorized", "Unauthorized");

    /// <summary>
    /// Inherited ADR 0015, at the token endpoint: once the service has said the
    /// credential is no good, running the exchange again would only ask it again.
    /// A token was refused during the exchange here too - the same exchange then
    /// went back for a new one and had the credential refused - and the
    /// credential's verdict wins.
    /// </summary>
    [Fact]
    public async Task An_exchange_is_not_run_again_once_the_credential_itself_was_refused()
    {
        using var tokens = TokenPipeline.Create(
            new InMemoryDeviceCredentialStore(Paired),
            new FakeTimeProvider(Noon),
            respond: (_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var exchanges = 0;

        var result = await RefusedTokenRerun.RunAsync(
            new Services(tokens.Provider),
            async () =>
            {
                exchanges++;
                tokens.Provider.Refused();
                _ = await tokens.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken);
                return Result.Failure<int>(Unauthorized);
            },
            NullLogger.Instance,
            "test");

        Assert.True(tokens.Provider.CredentialRejected);
        Assert.True(result.IsFailure);
        Assert.Equal(1, exchanges);
    }

    /// <summary>A host that registered no token provider has no token to
    /// refuse: the exchange runs once, whatever it answers.</summary>
    [Fact]
    public async Task With_no_token_provider_an_exchange_runs_once()
    {
        var exchanges = 0;

        var result = await RefusedTokenRerun.RunAsync(
            new Services(tokens: null),
            () =>
            {
                exchanges++;
                return Task.FromResult(Result.Failure<int>(Unauthorized));
            },
            NullLogger.Instance,
            "test");

        Assert.True(result.IsFailure);
        Assert.Equal(1, exchanges);
    }

    private sealed class Services(SyncTokenProvider? tokens) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(SyncTokenProvider) ? tokens : null;
    }
}
