using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Adapters;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features.DescribeOwner;
using Backlog.Modules.Sync.Features.Enrolment;
using Backlog.Modules.Sync.Features.IssueDeviceToken;
using Backlog.Modules.Sync.Features.IssuePairingCode;
using Backlog.Modules.Sync.Features.RedeemPairingCode;
using Backlog.Modules.Sync.Features.RegisterFirstDevice;
using Backlog.Modules.Sync.Services;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// One owner's worth of module, wired the way <c>AddSyncModule</c> wires it but
/// with a clock a test can move and a token issuer that records what it was
/// asked for. Built per test: the in-memory adapters are the store, so sharing
/// one would leak devices between cases.
/// </summary>
internal sealed class SyncModuleFixture
{
    internal SyncModuleFixture()
    {
        Clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        Devices = new InMemoryDeviceRegistry();
        Codes = new InMemoryPairingCodeStore();
        Hasher = new Sha256CredentialHasher();
        Tokens = new RecordingDeviceTokenIssuer(Clock);

        Tickets = new InMemoryEnrolmentTicketStore(Clock);
        StartEnrolment = new StartEnrolmentCommandHandler(
            Tickets, new NoGitHubOwnerSignIn(), OwnerEnrolment.Open, Hasher, Clock);
        Register = new RegisterFirstDeviceCommandHandler(
            Devices,
            Tickets,
            OwnerEnrolment.Open,
            Hasher,
            new RandomRegistrationCredentialGenerator(),
            new NoNotificationPushChannel(),
            Clock,
            NullLogger<RegisterFirstDeviceCommandHandler>.Instance);
        IssueCode = new IssuePairingCodeCommandHandler(
            Codes, new RandomPairingCodeGenerator(), Hasher, Clock);
        Redeem = new RedeemPairingCodeCommandHandler(
            Codes, Devices, Hasher, new RandomRegistrationCredentialGenerator(), Clock);
        Replays = new InMemoryAssertionReplayCache(Clock);
        IssueToken = new IssueDeviceTokenCommandHandler(
            Devices,
            Hasher,
            Tokens,
            new EcdsaDeviceAssertionVerifier(Clock),
            Replays,
            NullLogger<IssueDeviceTokenCommandHandler>.Instance);
        Describe = new DescribeOwnerQueryHandler(Devices);
    }

    internal FakeTimeProvider Clock { get; }

    internal InMemoryDeviceRegistry Devices { get; }

    internal InMemoryPairingCodeStore Codes { get; }

    internal Sha256CredentialHasher Hasher { get; }

    internal RecordingDeviceTokenIssuer Tokens { get; }

    internal InMemoryEnrolmentTicketStore Tickets { get; }

    internal InMemoryAssertionReplayCache Replays { get; }

    internal StartEnrolmentCommandHandler StartEnrolment { get; }

    internal RegisterFirstDeviceCommandHandler Register { get; }

    /// <summary>A registration carrying a one-time code from an enrolment that, with no
    /// owner configured, signs nobody in (local ADR 0058 §1).</summary>
    internal async Task<RegisterFirstDeviceCommand> Enrolled(string name)
    {
        var verifier = EnrolmentPkce.CreateVerifier();
        var started = Assert.IsType<Ok<EnrolmentStartResponse>>((await StartEnrolment.Handle(
            new StartEnrolmentCommand(new Uri("http://127.0.0.1:49152/"), EnrolmentPkce.Challenge(verifier), EnrolmentPkce.Method, "state"),
            TestContext.Current.CancellationToken)).Value).Value;

        var code = started.SignInAddress.Query.TrimStart('?').Split('&')
            .Select(pair => pair.Split('=', 2))
            .Single(pair => pair[0] == "code")[1];

        return new RegisterFirstDeviceCommand(name, Uri.UnescapeDataString(code), verifier);
    }

    internal IssuePairingCodeCommandHandler IssueCode { get; }

    internal RedeemPairingCodeCommandHandler Redeem { get; }

    internal IssueDeviceTokenCommandHandler IssueToken { get; }

    internal DescribeOwnerQueryHandler Describe { get; }

    /// <summary>Registers a first device and hands back the owner scope its
    /// token would carry.</summary>
    internal async Task<(OwnerScope Scope, string Credential)> RegisterFirstDevice(string name = "Study desktop")
    {
        var result = Assert.IsType<Ok<DeviceRegistrationResponse>>((await Register.Handle(await Enrolled(name), TestContext.Current.CancellationToken)).Value).Value;

        var scope = new OwnerScope(new OwnerId(result.OwnerId), new DeviceId(result.DeviceId));
        return (scope, result.Credential!);
    }
}

/// <summary>
/// Stands in for the JWT issuer the sync service registers. It records the
/// scope it was handed, which is the only thing these tests care about: whether
/// the token names the right owner is a module concern, how it is signed is
/// not.
/// </summary>
internal sealed class RecordingDeviceTokenIssuer(TimeProvider clock) : IDeviceTokenIssuer
{
    private readonly List<OwnerScope> _issued = [];

    internal IReadOnlyList<OwnerScope> Issued => _issued;

    public DeviceToken Issue(OwnerScope scope)
    {
        _issued.Add(scope);
        return new DeviceToken($"token-for-{scope.OwnerId}-{scope.DeviceId}", clock.GetUtcNow().AddMinutes(30));
    }
}
