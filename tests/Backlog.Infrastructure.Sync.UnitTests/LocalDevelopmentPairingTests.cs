using System.Net;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using Polly.Timeout;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The development harnesses pairing themselves: the desktop registers and
/// mints a pairing code with its own credential when the phone asks, and the
/// phone redeems the code it is handed. Only the wire, the handoff, the wait
/// between attempts and its jitter are faked; the credential stores are the
/// real ones.
/// </summary>
public sealed class LocalDevelopmentPairingTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DesktopDevice = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PhoneDevice = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private const string DesktopSecret = "the-desktop-registration-credential";
    private const string PhoneSecret = "the-phone-registration-credential";
    private const string AccessToken = "the-desktop-access-token";
    private const string PairingCode = "K7MN9PQR";

    private const string TokenPath = "/api/sync/devices/token";
    private const string RegisterPath = "/api/sync/devices/register";
    private const string CodesPath = "/api/sync/devices/codes";
    private const string PairPath = "/api/sync/devices/pair";

    private static readonly DeviceCredential Desktop = new(Owner, DesktopDevice, LocalDevelopmentPairing.DesktopDeviceName, DesktopSecret);

    /// <summary>A credential from before the emulator forgot its registry: an
    /// owner and devices the service no longer has.</summary>
    private const string StaleSecret = "a-credential-the-service-has-forgotten";
    private const string StalePhoneSecret = "a-phone-credential-the-service-has-forgotten";
    private static readonly Guid StaleOwner = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DeviceCredential StaleDesktop = new(StaleOwner, Guid.Parse("55555555-5555-5555-5555-555555555555"), LocalDevelopmentPairing.DesktopDeviceName, StaleSecret);
    private static readonly DeviceCredential StalePhone = new(StaleOwner, Guid.Parse("66666666-6666-6666-6666-666666666666"), LocalDevelopmentPairing.PhoneDeviceName, StalePhoneSecret);

    /// <summary>
    /// The desktop first waits for the device routes to answer — a token asked
    /// for a device nobody holds is refused once the registry is reachable —
    /// and then registers exactly once.
    /// </summary>
    [Fact]
    public async Task An_unregistered_desktop_registers_once_and_keeps_the_credential()
    {
        var store = new InMemoryDeviceCredentialStore();
        using var fixture = Fixture.Create(store, (request, _) => DesktopService(request));

        var outcome = await fixture.Pairing.RegisterDesktopAsync(fixture.Http, store, TestContext.Current.CancellationToken);

        Assert.Equal(LocalPairingOutcome.Registered, outcome);
        Assert.Equal([TokenPath, RegisterPath], fixture.Paths);
        Assert.Equal(new DeviceCredential(Owner, DesktopDevice, "Desktop harness", DesktopSecret), store.Current);
    }

    /// <summary>
    /// A desktop that already holds a credential asks only whether the service
    /// still knows it — one token exchange, a read — and registers nothing.
    /// </summary>
    [Fact]
    public async Task A_desktop_whose_credential_still_holds_registers_nothing()
    {
        var store = new InMemoryDeviceCredentialStore(Desktop);
        var changes = 0;
        store.Changed += () => changes++;
        using var fixture = Fixture.Create(store, (request, _) =>
            Is(request, "/devices/token") && Body(request).Contains(DesktopSecret, StringComparison.Ordinal)
                ? Token()
                : throw new InvalidOperationException("Only the held credential's token exchange was expected."));

        var outcome = await fixture.Pairing.RegisterDesktopAsync(fixture.Http, store, TestContext.Current.CancellationToken);

        Assert.Equal(LocalPairingOutcome.AlreadyPaired, outcome);
        Assert.Equal([TokenPath], fixture.Paths);
        Assert.Equal(Desktop, store.Current);
        Assert.Equal(0, changes);
    }

    /// <summary>
    /// Criterion 9: the Cosmos emulator keeps no volume, so its device registry
    /// can come back empty while the harness keeps its credential file. A
    /// credential the token route refuses as unknown is discarded through the
    /// harness's own store, and the desktop registers again as on a fresh run —
    /// once: the held credential is checked once per start, never in a loop.
    /// </summary>
    [Fact]
    public async Task A_desktop_whose_credential_the_service_forgot_discards_it_and_registers_again_once()
    {
        var store = new InMemoryDeviceCredentialStore(StaleDesktop);
        var changes = new List<DeviceCredential?>();
        store.Changed += () => changes.Add(store.Current);
        var staleChecks = 0;
        using var fixture = Fixture.Create(store, (request, _) =>
        {
            if (Body(request).Contains(StaleSecret, StringComparison.Ordinal)) staleChecks++;
            return DesktopService(request);
        });

        var outcome = await fixture.Pairing.RegisterDesktopAsync(fixture.Http, store, TestContext.Current.CancellationToken);

        Assert.Equal(LocalPairingOutcome.Registered, outcome);
        Assert.Equal([TokenPath, TokenPath, RegisterPath], fixture.Paths);
        Assert.Equal(1, staleChecks);
        Assert.Equal([null, Desktop], changes);
        Assert.Equal(Desktop, store.Current);
    }

    /// <summary>
    /// A held credential that cannot be checked yet — a replica still starting,
    /// a service not listening — is kept, and the check is asked again; only a
    /// definite "unknown device" discards it.
    /// </summary>
    [Theory]
    [InlineData("replica unavailable")]
    [InlineData("connection refused")]
    public async Task A_desktop_whose_credential_cannot_be_checked_yet_keeps_it(string ending)
    {
        var store = new InMemoryDeviceCredentialStore(Desktop);
        var changes = 0;
        store.Changed += () => changes++;
        var checks = 0;
        using var fixture = Fixture.Create(store, (request, _) =>
            Is(request, "/devices/token") && checks++ < 2 ? Unready(ending)
            : Is(request, "/devices/token") ? Token()
            : throw new InvalidOperationException("Only token exchanges were expected."));

        var outcome = await fixture.Pairing.RegisterDesktopAsync(fixture.Http, store, TestContext.Current.CancellationToken);

        Assert.Equal(LocalPairingOutcome.AlreadyPaired, outcome);
        Assert.Equal([TokenPath, TokenPath, TokenPath], fixture.Paths);
        Assert.Equal(2, fixture.Waits.Count);
        Assert.Equal(Desktop, store.Current);
        Assert.Equal(0, changes);
    }

    /// <summary>
    /// The Cosmos emulator takes minutes to come up and nothing in the app model
    /// waits for it, so the first answers are a 503 and then nothing at all. The
    /// readiness probe is a read, so it is asked again on both; the waits grow to
    /// a ceiling rather than for ever, and the register goes out once.
    /// </summary>
    [Fact]
    public async Task A_replica_still_starting_or_a_service_not_up_is_waited_out_with_a_capped_backoff()
    {
        var store = new InMemoryDeviceCredentialStore();
        var probes = 0;
        using var fixture = Fixture.Create(store, (request, _) =>
            !Is(request, "/devices/token") ? DesktopService(request)
            : probes++ switch
            {
                0 or 1 or 2 => ReplicaUnavailable(),
                3 or 4 or 5 => throw new HttpRequestException("An error occurred while sending the request."),
                _ => DesktopService(request)
            });

        var outcome = await fixture.Pairing.RegisterDesktopAsync(fixture.Http, store, TestContext.Current.CancellationToken);

        Assert.Equal(LocalPairingOutcome.Registered, outcome);
        Assert.Equal([2, 4, 8, 16, 30, 30], fixture.Waits.Select(wait => wait.TotalSeconds));
        Assert.Equal(1, fixture.Count(RegisterPath));
        Assert.Equal(Desktop, store.Current);
    }

    /// <summary>
    /// Two harnesses started together would otherwise knock in step. The wait
    /// lands up to a fifth either side of its base.
    /// </summary>
    [Theory]
    [InlineData(0.0, 1.6)]
    [InlineData(1.0, 2.4)]
    public async Task The_wait_between_attempts_is_jittered_around_its_base(double random, double seconds)
    {
        var store = new InMemoryDeviceCredentialStore();
        var probes = 0;
        using var fixture = Fixture.Create(
            store,
            (request, _) => Is(request, "/devices/token") && probes++ == 0 ? ReplicaUnavailable() : DesktopService(request),
            random: () => random);

        await fixture.Pairing.RegisterDesktopAsync(fixture.Http, store, TestContext.Current.CancellationToken);

        Assert.Equal(seconds, Assert.Single(fixture.Waits).TotalSeconds, precision: 6);
    }

    /// <summary>
    /// A refusal the service meant is not going to change by asking again, and a
    /// loop that registered on every pass would leave an owner behind each time.
    /// </summary>
    [Fact]
    public async Task A_refusal_that_is_not_transient_stops_without_retrying()
    {
        var store = new InMemoryDeviceCredentialStore();
        using var fixture = Fixture.Create(store, (request, _) => Is(request, "/devices/register")
            ? StubHttpMessageHandler.Problem(HttpStatusCode.BadRequest, "device.name_required", "Give this device a name first.")
            : DesktopService(request));

        var outcome = await fixture.Pairing.RegisterDesktopAsync(fixture.Http, store, TestContext.Current.CancellationToken);

        Assert.Equal(LocalPairingOutcome.Stopped, outcome);
        Assert.Equal(1, fixture.Count(RegisterPath));
        Assert.Empty(fixture.Waits);
        Assert.Null(store.Current);
        Assert.Contains(fixture.Log.Lines, line => line.Level == LogLevel.Warning && line.Message.Contains("device.name_required", StringComparison.Ordinal));
    }

    /// <summary>
    /// A register that timed out, whose response was dropped, that a proxy
    /// answered 500 for, or that the service answered "replica unavailable" for
    /// may have created an owner — the device registry files a Cosmos write that
    /// timed out after landing under that code. Sending it again would create a
    /// second and orphan the first (guideline ADR 0015: never retry a
    /// non-idempotent command), so the attempt ends there.
    /// </summary>
    [Theory]
    [InlineData("timed out")]
    [InlineData("pipeline timed out")]
    [InlineData("response dropped")]
    [InlineData("500 with no code")]
    [InlineData("replica unavailable")]
    public async Task A_register_that_may_have_landed_is_not_sent_again(string ending)
    {
        var store = new InMemoryDeviceCredentialStore();
        using var fixture = Fixture.Create(store, (request, _) => Is(request, "/devices/register")
            ? Ambiguous(ending)
            : DesktopService(request));

        var outcome = await fixture.Pairing.RegisterDesktopAsync(fixture.Http, store, TestContext.Current.CancellationToken);

        Assert.Equal(LocalPairingOutcome.Stopped, outcome);
        Assert.Equal(1, fixture.Count(RegisterPath));
        Assert.Empty(fixture.Waits);
        Assert.Null(store.Current);
    }

    /// <summary>
    /// A register that provably did not land — the connection was refused, or
    /// the replica turned it away as too busy, which Cosmos never applies — is
    /// safe to send again, and is.
    /// </summary>
    [Theory]
    [InlineData("connection refused")]
    [InlineData("replica busy")]
    public async Task A_register_that_provably_did_not_land_is_sent_again(string ending)
    {
        var store = new InMemoryDeviceCredentialStore();
        var registers = 0;
        using var fixture = Fixture.Create(store, (request, _) => Is(request, "/devices/register") && registers++ == 0
            ? NotLanded(ending)
            : DesktopService(request));

        var outcome = await fixture.Pairing.RegisterDesktopAsync(fixture.Http, store, TestContext.Current.CancellationToken);

        Assert.Equal(LocalPairingOutcome.Registered, outcome);
        Assert.Equal(2, fixture.Count(RegisterPath));
        Assert.Single(fixture.Waits);
        Assert.Equal(Desktop, store.Current);
    }

    /// <summary>
    /// The host stopping while the service is still down ends the attempt
    /// quietly: this runs in the background of a harness, and an exception out of
    /// it would be one nobody awaits.
    /// </summary>
    [Fact]
    public async Task A_host_that_stops_while_waiting_ends_the_attempt_without_throwing()
    {
        var store = new InMemoryDeviceCredentialStore();
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var fixture = Fixture.Create(
            store,
            (_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, "No connection could be made."),
            onWait: waits => { if (waits == 3) stopping.Cancel(); });

        var outcome = await fixture.Pairing.RegisterDesktopAsync(fixture.Http, store, stopping.Token);

        Assert.Equal(LocalPairingOutcome.Cancelled, outcome);
        Assert.Equal([TokenPath, TokenPath, TokenPath], fixture.Paths);
        Assert.Null(store.Current);
    }

    /// <summary>
    /// A Cosmos cold start takes minutes, not a quarter of an hour. A service
    /// still not ready after fifteen minutes is something a person has to look
    /// at, so the poll gives up and says how to pair by hand — once, and with no
    /// secret in it.
    /// </summary>
    [Fact]
    public async Task A_desktop_whose_service_never_becomes_ready_gives_up_after_fifteen_minutes()
    {
        var store = new InMemoryDeviceCredentialStore();
        using var fixture = Fixture.Create(store, (_, _) => ReplicaUnavailable());

        var outcome = await fixture.Pairing.RegisterDesktopAsync(fixture.Http, store, TestContext.Current.CancellationToken);

        Assert.Equal(LocalPairingOutcome.Stopped, outcome);
        AssertGaveUpAtTheDeadline(fixture);
        Assert.Equal(0, fixture.Count(RegisterPath));
        Assert.Null(store.Current);
    }

    /// <summary>Waited at least fifteen minutes and not a wait more, and said
    /// so in one warning naming where to pair by hand.</summary>
    private static void AssertGaveUpAtTheDeadline(Fixture fixture)
    {
        var waited = TimeSpan.FromTicks(fixture.Waits.Sum(wait => wait.Ticks));
        var beforeTheLast = waited - fixture.Waits[^1];

        Assert.True(waited >= TimeSpan.FromMinutes(15), $"Gave up after {waited}.");
        Assert.True(beforeTheLast < TimeSpan.FromMinutes(15), $"Kept going until {waited}.");

        var warning = Assert.Single(fixture.Log.Lines, line => line.Level >= LogLevel.Warning);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("Settings → Devices", warning.Message, StringComparison.Ordinal);
        Assert.Contains("15", warning.Message, StringComparison.Ordinal);
    }

    /// <summary>A clock that is missing is a composition mistake, and it is
    /// reported where it was made rather than at the first wait.</summary>
    [Fact]
    public void A_pairing_without_a_clock_is_refused_at_construction()
    {
        Assert.Throws<ArgumentNullException>(() => new LocalDevelopmentPairing(new CapturingLogger<LocalDevelopmentPairing>(), null!));
    }

    /// <summary>
    /// A phone that already holds a credential asks only whether the service
    /// still knows it, with its own credential, and pairs nothing — and its views
    /// are not forgotten: this is not a new pairing.
    /// </summary>
    [Fact]
    public async Task A_phone_whose_credential_still_holds_pairs_nothing()
    {
        var phone = new DeviceCredential(Owner, PhoneDevice, "Phone harness", PhoneSecret);
        var store = new InMemoryDeviceCredentialStore(phone);
        var changes = 0;
        store.Changed += () => changes++;
        var handoff = new ScriptedHandoff();
        using var fixture = Fixture.Create(store, (request, _) =>
            Is(request, "/devices/token") && Body(request).Contains(PhoneSecret, StringComparison.Ordinal)
                ? Token()
                : throw new InvalidOperationException("Only the held credential's token exchange was expected."));

        var outcome = await fixture.PairPhoneAsync(store, handoff, manualPairingRequested: false);

        Assert.Equal(LocalPairingOutcome.AlreadyPaired, outcome);
        Assert.Equal([TokenPath], fixture.Paths);
        Assert.Equal(phone, store.Current);
        Assert.Equal(0, changes);
        Assert.Equal(0, handoff.Requests);
        Assert.Equal(0, fixture.Forgotten);
    }

    /// <summary>
    /// Criterion 9 on the phone: its own credential refused as unknown is
    /// discarded through its own store, and it pairs again with a code the desktop
    /// hands over — checking its own credential once, not in a loop.
    /// </summary>
    [Fact]
    public async Task A_phone_whose_credential_the_service_forgot_discards_it_and_pairs_again_once()
    {
        var store = new InMemoryDeviceCredentialStore(StalePhone);
        var changes = new List<DeviceCredential?>();
        store.Changed += () => changes.Add(store.Current);
        var staleChecks = 0;
        using var fixture = Fixture.Create(store, (request, _) =>
        {
            if (!Body(request).Contains(StalePhoneSecret, StringComparison.Ordinal)) return Service(request);
            staleChecks++;
            return CredentialInvalid();
        });

        var outcome = await fixture.PairPhoneAsync(store, new ScriptedHandoff { Desktop = _ => PairingCode }, manualPairingRequested: false);

        Assert.Equal(LocalPairingOutcome.Paired, outcome);
        Assert.Equal([TokenPath, PairPath], fixture.Paths);
        Assert.Equal(1, staleChecks);
        Assert.Equal([null, new DeviceCredential(Owner, PhoneDevice, "Phone harness", PhoneSecret)], changes);
    }

    /// <summary>
    /// A phone's credential that cannot be checked yet is kept; only a definite
    /// "unknown device" discards it.
    /// </summary>
    [Fact]
    public async Task A_phone_whose_credential_cannot_be_checked_yet_keeps_it()
    {
        var phone = new DeviceCredential(Owner, PhoneDevice, "Phone harness", PhoneSecret);
        var store = new InMemoryDeviceCredentialStore(phone);
        var changes = 0;
        store.Changed += () => changes++;
        var checks = 0;
        using var fixture = Fixture.Create(store, (request, _) =>
            Is(request, "/devices/token") && checks++ == 0 ? ReplicaUnavailable()
            : Is(request, "/devices/token") ? Token()
            : throw new InvalidOperationException("Only token exchanges were expected."));

        var outcome = await fixture.PairPhoneAsync(store, new ScriptedHandoff(), manualPairingRequested: false);

        Assert.Equal(LocalPairingOutcome.AlreadyPaired, outcome);
        Assert.Equal([TokenPath, TokenPath], fixture.Paths);
        Assert.Equal(phone, store.Current);
        Assert.Equal(0, changes);
    }

    /// <summary>
    /// A phone marked for pairing by hand keeps whatever credential it holds,
    /// untouched and unchecked: the marker means a person, or the end-to-end
    /// suite, owns this phone's pairing.
    /// </summary>
    [Fact]
    public async Task A_phone_marked_for_pairing_by_hand_keeps_its_credential_untouched()
    {
        var store = new InMemoryDeviceCredentialStore(StalePhone);
        var changes = 0;
        store.Changed += () => changes++;
        var handoff = new ScriptedHandoff();
        using var fixture = Fixture.Create(store, (_, _) => throw new InvalidOperationException("No request was expected."));

        var outcome = await fixture.PairPhoneAsync(store, handoff, manualPairingRequested: true);

        Assert.Equal(LocalPairingOutcome.AlreadyPaired, outcome);
        Assert.Empty(fixture.Paths);
        Assert.Equal(StalePhone, store.Current);
        Assert.Equal(0, changes);
        Assert.Equal(0, handoff.Requests);
    }

    /// <summary>
    /// A phone made "never paired" on purpose — the end-to-end reset, or a person
    /// who wants to try the pairing screen — stays that way across a restart, and
    /// asks the desktop for nothing.
    /// </summary>
    [Fact]
    public async Task A_phone_marked_for_pairing_by_hand_is_left_unpaired()
    {
        var store = new InMemoryDeviceCredentialStore();
        var handoff = new ScriptedHandoff { Desktop = _ => PairingCode };
        using var fixture = Fixture.Create(store, (_, _) => throw new InvalidOperationException("No request was expected."));

        var outcome = await fixture.PairPhoneAsync(store, handoff, manualPairingRequested: true);

        Assert.Equal(LocalPairingOutcome.ManualPairingRequested, outcome);
        Assert.Empty(fixture.Paths);
        Assert.Equal(0, handoff.Requests);
        Assert.Null(store.Current);
    }

    /// <summary>
    /// Only a pairing code crosses between the harnesses, never a credential
    /// (<c>.devbook/domain/sync/features.md#pairing-a-device</c>): the phone asks
    /// the desktop for one, the desktop mints it with its own credential and hands
    /// it over, and the phone redeems it anonymously, as a person's phone does —
    /// ending up under the desktop's owner as a device of its own. Its cached views
    /// are forgotten before the credential is kept, so the first pull starts them
    /// afresh.
    /// </summary>
    [Fact]
    public async Task A_phone_pairs_under_the_desktops_owner_with_a_code_the_desktop_handed_over()
    {
        var store = new InMemoryDeviceCredentialStore();
        var changes = 0;
        store.Changed += () => changes++;
        var handoff = new ScriptedHandoff { Desktop = _ => PairingCode };
        string? redeemRequest = null;
        DeviceCredential? heldWhenForgotten = new(Owner, Guid.Empty, "not asked", "not asked");
        using var fixture = Fixture.Create(store, (request, _) =>
        {
            if (Is(request, "/devices/pair")) redeemRequest = Body(request);
            return Service(request);
        });

        var outcome = await fixture.PairPhoneAsync(store, handoff, manualPairingRequested: false, forget: () => heldWhenForgotten = store.Current);

        Assert.Equal(LocalPairingOutcome.Paired, outcome);
        Assert.Equal([PairPath], fixture.Paths);
        Assert.Equal(1, handoff.Requests);
        Assert.Contains(PairingCode, redeemRequest, StringComparison.Ordinal);
        Assert.Contains("Phone harness", redeemRequest, StringComparison.Ordinal);
        Assert.Null(fixture.Handler.Requests[0].AuthorizationScheme);
        Assert.Equal(new DeviceCredential(Owner, PhoneDevice, "Phone harness", PhoneSecret), store.Current);
        Assert.Equal(1, changes);
        Assert.Equal(1, fixture.Forgotten);
        Assert.Null(heldWhenForgotten);
    }

    /// <summary>
    /// The two harnesses start together, and the desktop mints only once it is
    /// registered, so the phone usually asks first. It waits for the code without
    /// asking the service anything — there is nothing to ask until it has one.
    /// </summary>
    [Fact]
    public async Task A_phone_waits_for_the_desktops_code_without_calling_the_service()
    {
        var store = new InMemoryDeviceCredentialStore();
        var pathsWhenItArrived = Array.Empty<string>();
        Fixture? fixture = null;
        ScriptedHandoff? handoff = null;
        handoff = new ScriptedHandoff
        {
            Desktop = _ =>
            {
                if (handoff!.Takes < 3) return null;
                pathsWhenItArrived = fixture!.Paths;
                return PairingCode;
            }
        };
        using var created = fixture = Fixture.Create(store, (request, _) => Service(request));

        var outcome = await fixture.PairPhoneAsync(store, handoff, manualPairingRequested: false);

        Assert.Equal(LocalPairingOutcome.Paired, outcome);
        Assert.Empty(pathsWhenItArrived);
        Assert.Equal([2, 4], fixture.Waits.Select(wait => wait.TotalSeconds));
        Assert.Equal(1, handoff.Requests);
        Assert.Equal(PhoneDevice, store.Current?.DeviceId);
    }

    /// <summary>
    /// A code that was handed over but could not be read is dropped, and the
    /// desktop has already withdrawn the request it answered. The phone asks
    /// again rather than waiting out its deadline with nothing standing.
    /// </summary>
    [Fact]
    public async Task A_phone_whose_handed_over_code_could_not_be_read_asks_again()
    {
        var store = new InMemoryDeviceCredentialStore();
        var handoff = new ScriptedHandoff { Desktop = _ => PairingCode, Unreadable = request => request == 1 };
        using var fixture = Fixture.Create(store, (request, _) => Service(request));

        var outcome = await fixture.PairPhoneAsync(store, handoff, manualPairingRequested: false);

        Assert.Equal(LocalPairingOutcome.Paired, outcome);
        Assert.Equal(2, handoff.Requests);
        Assert.Equal([PairPath], fixture.Paths);
        Assert.Equal(PhoneDevice, store.Current?.DeviceId);
    }

    /// <summary>
    /// The deadline runs from the first attempt of the start, the check of a held
    /// credential included: a refused credential on a cold emulator does not get
    /// a second fifteen minutes for the pairing that follows.
    /// </summary>
    [Fact]
    public async Task A_desktop_that_starts_from_a_held_credential_gives_up_fifteen_minutes_after_its_first_attempt()
    {
        var store = new InMemoryDeviceCredentialStore(StaleDesktop);
        var checks = 0;
        using var fixture = Fixture.Create(store, (request, _) =>
            Body(request).Contains(StaleSecret, StringComparison.Ordinal)
                ? checks++ < 4 ? ReplicaUnavailable() : CredentialInvalid()
                : ReplicaUnavailable());

        var outcome = await fixture.Pairing.RegisterDesktopAsync(fixture.Http, store, TestContext.Current.CancellationToken);

        Assert.Equal(LocalPairingOutcome.Stopped, outcome);
        Assert.Equal(5, checks);
        Assert.Equal(0, fixture.Count(RegisterPath));

        var waited = TimeSpan.FromTicks(fixture.Waits.Sum(wait => wait.Ticks));
        Assert.True(waited >= TimeSpan.FromMinutes(15), $"Gave up after {waited}.");
        Assert.True(waited - fixture.Waits[^1] < TimeSpan.FromMinutes(15), $"Kept going until {waited}.");
        Assert.Contains(fixture.Log.Lines, line => line.Level == LogLevel.Warning && line.Message.Contains("Settings → Devices", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_phone_whose_desktop_never_hands_over_a_code_gives_up_after_fifteen_minutes()
    {
        var store = new InMemoryDeviceCredentialStore();
        using var fixture = Fixture.Create(store, (_, _) => throw new InvalidOperationException("No request was expected."));

        var outcome = await fixture.PairPhoneAsync(store, new ScriptedHandoff(), manualPairingRequested: false);

        Assert.Equal(LocalPairingOutcome.Stopped, outcome);
        AssertGaveUpAtTheDeadline(fixture);
        Assert.Empty(fixture.Paths);
        Assert.Null(store.Current);
        Assert.Equal(0, fixture.Forgotten);
    }

    [Fact]
    public async Task A_phone_whose_desktop_never_hands_over_a_code_stops_with_the_host()
    {
        var store = new InMemoryDeviceCredentialStore();
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var fixture = Fixture.Create(
            store,
            (_, _) => throw new InvalidOperationException("No request was expected."),
            onWait: waits => { if (waits == 5) stopping.Cancel(); });

        var outcome = await fixture.PairPhoneAsync(store, new ScriptedHandoff(), manualPairingRequested: false, stopping.Token);

        Assert.Equal(LocalPairingOutcome.Cancelled, outcome);
        Assert.Empty(fixture.Paths);
        Assert.Null(store.Current);
    }

    /// <summary>
    /// A code is single-use and lives ten minutes. One that was burnt, lapsed or
    /// never known before the phone redeemed it is replaced: the phone asks the
    /// desktop for a fresh one. The service answers the first two with a 409.
    /// </summary>
    [Theory]
    [InlineData("pairing.code_expired", HttpStatusCode.Conflict)]
    [InlineData("pairing.code_used", HttpStatusCode.Conflict)]
    [InlineData("pairing.code_not_found", HttpStatusCode.NotFound)]
    public async Task A_code_that_was_spent_or_lapsed_is_replaced_by_a_fresh_one(string code, HttpStatusCode status)
    {
        var store = new InMemoryDeviceCredentialStore();
        var handoff = new ScriptedHandoff { Desktop = request => request == 1 ? "SPENT-01" : PairingCode };
        using var fixture = Fixture.Create(store, (request, _) =>
            Is(request, "/devices/pair") && Body(request).Contains("SPENT-01", StringComparison.Ordinal)
                ? StubHttpMessageHandler.Problem(status, code, "That code cannot be redeemed.")
                : Service(request));

        var outcome = await fixture.PairPhoneAsync(store, handoff, manualPairingRequested: false);

        Assert.Equal(LocalPairingOutcome.Paired, outcome);
        Assert.Equal(2, handoff.Requests);
        Assert.Equal(2, fixture.Count(PairPath));
        Assert.Equal(PhoneDevice, store.Current?.DeviceId);
        Assert.Equal(1, fixture.Forgotten);
    }

    /// <summary>
    /// A code that keeps being refused is not a code problem any more, and the
    /// phone gives up after a few rather than asking for codes for the life of
    /// the run.
    /// </summary>
    [Fact]
    public async Task A_phone_whose_codes_keep_being_refused_gives_up()
    {
        var store = new InMemoryDeviceCredentialStore();
        var handoff = new ScriptedHandoff { Desktop = request => $"REFUSED-{request}" };
        using var fixture = Fixture.Create(store, (request, _) =>
            Is(request, "/devices/pair")
                ? StubHttpMessageHandler.Problem(HttpStatusCode.NotFound, "pairing.code_not_found", "That code was not recognised.")
                : Service(request));

        var outcome = await fixture.PairPhoneAsync(store, handoff, manualPairingRequested: false);

        Assert.Equal(LocalPairingOutcome.Stopped, outcome);
        Assert.Equal(3, fixture.Count(PairPath));
        Assert.Equal(3, handoff.Requests);
        Assert.Null(store.Current);
        Assert.Equal(0, fixture.Forgotten);
    }

    /// <summary>
    /// A redeem that may have landed has added a phone under the owner. Sending
    /// it again, or a fresh code after it, would add a second "Phone harness", so
    /// the attempt ends there. "Replica unavailable" is among them: the code was
    /// burnt and the device write may have landed before it timed out.
    /// </summary>
    [Theory]
    [InlineData("timed out")]
    [InlineData("pipeline timed out")]
    [InlineData("response dropped")]
    [InlineData("500 with no code")]
    [InlineData("replica unavailable")]
    public async Task A_redeem_that_may_have_landed_is_not_sent_again(string ending)
    {
        var store = new InMemoryDeviceCredentialStore();
        var handoff = new ScriptedHandoff { Desktop = _ => PairingCode };
        using var fixture = Fixture.Create(store, (request, _) => Is(request, "/devices/pair") ? Ambiguous(ending) : Service(request));

        var outcome = await fixture.PairPhoneAsync(store, handoff, manualPairingRequested: false);

        Assert.Equal(LocalPairingOutcome.Stopped, outcome);
        Assert.Equal(1, fixture.Count(PairPath));
        Assert.Equal(1, handoff.Requests);
        Assert.Empty(fixture.Waits);
        Assert.Null(store.Current);
        Assert.Equal(0, fixture.Forgotten);
    }

    /// <summary>
    /// A redeem that provably did not land is sent again with the same code — the
    /// code was never spent, so the desktop is not asked for another.
    /// </summary>
    [Theory]
    [InlineData("connection refused")]
    [InlineData("replica busy")]
    public async Task A_redeem_that_provably_did_not_land_is_sent_again(string ending)
    {
        var store = new InMemoryDeviceCredentialStore();
        var handoff = new ScriptedHandoff { Desktop = _ => PairingCode };
        var redeems = 0;
        using var fixture = Fixture.Create(store, (request, _) =>
            Is(request, "/devices/pair") && redeems++ == 0 ? NotLanded(ending) : Service(request));

        var outcome = await fixture.PairPhoneAsync(store, handoff, manualPairingRequested: false);

        Assert.Equal(LocalPairingOutcome.Paired, outcome);
        Assert.Equal(2, fixture.Count(PairPath));
        Assert.Equal(1, handoff.Requests);
        Assert.Single(fixture.Waits);
        Assert.Equal(PhoneDevice, store.Current?.DeviceId);
    }

    /// <summary>
    /// A person who pairs the phone by hand while it waits for the desktop's code
    /// keeps that pairing: redeeming the code would save over it and orphan the
    /// device it made.
    /// </summary>
    [Fact]
    public async Task A_phone_paired_by_hand_while_it_waited_for_a_code_keeps_that_pairing()
    {
        var store = new InMemoryDeviceCredentialStore();
        var byHand = new DeviceCredential(Owner, Guid.NewGuid(), "Phone", "paired-by-hand");
        var handoff = new ScriptedHandoff
        {
            Desktop = _ =>
            {
                store.Save(byHand);
                return PairingCode;
            }
        };
        using var fixture = Fixture.Create(store, (request, _) => Service(request));

        var outcome = await fixture.PairPhoneAsync(store, handoff, manualPairingRequested: false);

        Assert.Equal(LocalPairingOutcome.AlreadyPaired, outcome);
        Assert.Equal(0, fixture.Count(PairPath));
        Assert.Equal(byHand, store.Current);
        Assert.Equal(0, fixture.Forgotten);
    }

    /// <summary>
    /// The desktop mints a code only when the phone asks for one: it trades its
    /// own credential for a token and sends that token, once, on the bearer
    /// route — all on the pipeline-free client — then hands the code over,
    /// withdrawing the request, and keeps listening for the next.
    /// </summary>
    [Fact]
    public async Task The_desktop_mints_a_code_only_when_the_phone_asks_and_hands_it_over()
    {
        var store = new InMemoryDeviceCredentialStore(Desktop);
        var handoff = new ScriptedHandoff();
        string? handed = null;
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Fixture? fixture = null;
        using var created = fixture = Fixture.Create(
            store,
            (request, _) => Service(request),
            onWait: waits =>
            {
                if (waits == 3) handoff.RequestCode();
                if (waits == 5) handed = handoff.TakeCode();
                if (waits == 8) stopping.Cancel();
            });

        var outcome = await fixture.ServeAsync(store, handoff, stopping.Token);

        Assert.Equal(LocalPairingOutcome.Cancelled, outcome);
        Assert.Equal([TokenPath, CodesPath], fixture.Paths);
        Assert.Null(fixture.Handler.Requests[0].AuthorizationScheme);
        Assert.Equal(("Bearer", AccessToken), (fixture.Handler.Requests[1].AuthorizationScheme, fixture.Handler.Requests[1].AuthorizationParameter));
        Assert.Equal(PairingCode, handed);
        Assert.False(handoff.CodeRequested);
        Assert.All(fixture.Waits, wait => Assert.Equal(LocalDevelopmentPairing.HandoffPollInterval, wait));
    }

    /// <summary>
    /// A desktop holding no credential has nothing to mint a code with; it mints
    /// nothing and leaves the request for when it has one.
    /// </summary>
    [Fact]
    public async Task A_desktop_without_a_credential_mints_nothing()
    {
        var store = new InMemoryDeviceCredentialStore();
        var handoff = new ScriptedHandoff();
        handoff.RequestCode();
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var fixture = Fixture.Create(
            store,
            (_, _) => throw new InvalidOperationException("No request was expected."),
            onWait: waits => { if (waits == 3) stopping.Cancel(); });

        var outcome = await fixture.ServeAsync(store, handoff, stopping.Token);

        Assert.Equal(LocalPairingOutcome.Cancelled, outcome);
        Assert.Empty(fixture.Paths);
        Assert.True(handoff.CodeRequested);
    }

    /// <summary>
    /// A code the service cannot issue yet is asked for again, with the growing
    /// wait; the request stays until a code is handed over.
    /// </summary>
    [Fact]
    public async Task A_code_the_service_cannot_issue_yet_is_asked_for_again()
    {
        var store = new InMemoryDeviceCredentialStore(Desktop);
        var handoff = new ScriptedHandoff();
        handoff.RequestCode();
        var issues = 0;
        string? handed = null;
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Fixture? fixture = null;
        using var created = fixture = Fixture.Create(
            store,
            (request, _) => Is(request, "/devices/codes") && issues++ < 2 ? ReplicaUnavailable() : Service(request),
            onWait: waits =>
            {
                handed ??= handoff.CodeRequested ? null : handoff.TakeCode();
                if (handed is not null) stopping.Cancel();
            });

        var outcome = await fixture.ServeAsync(store, handoff, stopping.Token);

        Assert.Equal(LocalPairingOutcome.Cancelled, outcome);
        Assert.Equal(3, fixture.Count(CodesPath));
        Assert.Equal(PairingCode, handed);
        Assert.Equal([2, 4], fixture.Waits.Take(2).Select(wait => wait.TotalSeconds));
    }

    /// <summary>
    /// A desktop whose own credential the token route refuses as unknown mints
    /// nothing more this run: it stops, and the phone gives up at its deadline
    /// with the manual recipe. The next start checks the credential and recovers
    /// (criterion 9).
    /// </summary>
    [Fact]
    public async Task A_desktop_whose_own_credential_is_refused_stops_minting()
    {
        var store = new InMemoryDeviceCredentialStore(Desktop);
        var handoff = new ScriptedHandoff();
        handoff.RequestCode();
        using var fixture = Fixture.Create(store, (request, _) =>
            Is(request, "/devices/token") ? CredentialInvalid() : throw new InvalidOperationException("No code was to be asked for."));

        var outcome = await fixture.ServeAsync(store, handoff, TestContext.Current.CancellationToken);

        Assert.Equal(LocalPairingOutcome.Stopped, outcome);
        Assert.Equal([TokenPath], fixture.Paths);
        Assert.Null(handoff.TakeCode());
        Assert.Contains(fixture.Log.Lines, line => line.Level == LogLevel.Warning && line.Message.Contains("device.credential_invalid", StringComparison.Ordinal));
    }

    /// <summary>
    /// A bare 401 on the codes route — a token the service no longer takes after
    /// a restart, with no problem body — says nothing about the desktop's
    /// credential. The desktop waits and asks again with a fresh token, and keeps
    /// serving.
    /// </summary>
    [Fact]
    public async Task A_bare_401_on_the_codes_route_is_asked_again_and_keeps_serving()
    {
        var store = new InMemoryDeviceCredentialStore(Desktop);
        var handoff = new ScriptedHandoff();
        handoff.RequestCode();
        var issues = 0;
        string? handed = null;
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var fixture = Fixture.Create(
            store,
            (request, _) => Is(request, "/devices/codes") && issues++ == 0 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Service(request),
            onWait: _ =>
            {
                handed ??= handoff.CodeRequested ? null : handoff.TakeCode();
                if (handed is not null) stopping.Cancel();
            });

        var outcome = await fixture.ServeAsync(store, handoff, stopping.Token);

        Assert.Equal(LocalPairingOutcome.Cancelled, outcome);
        Assert.Equal([TokenPath, CodesPath, TokenPath, CodesPath], fixture.Paths);
        Assert.Equal(PairingCode, handed);
        Assert.DoesNotContain(fixture.Log.Lines, line => line.Level >= LogLevel.Warning);
    }

    /// <summary>
    /// A service that cannot issue a code for a quarter of an hour is not going
    /// to by the next poll (guideline ADR 0015: no unbounded retries). The desktop
    /// gives up on that request — one warning, the request withdrawn — and serves
    /// the next one a phone makes.
    /// </summary>
    [Fact]
    public async Task A_request_the_service_cannot_serve_for_fifteen_minutes_is_given_up_and_the_next_is_served()
    {
        var store = new InMemoryDeviceCredentialStore(Desktop);
        var handoff = new ScriptedHandoff();
        handoff.RequestCode();
        var serviceUp = false;
        var waitsWhenGivenUp = -1;
        string? handed = null;
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Fixture? fixture = null;
        using var created = fixture = Fixture.Create(
            store,
            (request, _) => Is(request, "/devices/codes") && !serviceUp ? ReplicaUnavailable() : Service(request),
            onWait: waits =>
            {
                if (!serviceUp && handoff.Withdrawn > 0)
                {
                    waitsWhenGivenUp = waits - 1;
                    serviceUp = true;
                    handoff.RequestCode();
                    return;
                }

                handed ??= serviceUp && !handoff.CodeRequested ? handoff.TakeCode() : null;
                if (handed is not null) stopping.Cancel();
            });

        var outcome = await fixture.ServeAsync(store, handoff, stopping.Token);

        Assert.Equal(LocalPairingOutcome.Cancelled, outcome);
        Assert.Equal(1, handoff.Withdrawn);
        Assert.Equal(PairingCode, handed);

        var beforeGivingUp = fixture.Waits.Take(waitsWhenGivenUp).ToList();
        var waited = TimeSpan.FromTicks(beforeGivingUp.Sum(wait => wait.Ticks));
        Assert.True(waited >= TimeSpan.FromMinutes(15), $"Gave up after {waited}.");
        Assert.True(waited - beforeGivingUp[^1] < TimeSpan.FromMinutes(15), $"Kept going until {waited}.");

        var warning = Assert.Single(fixture.Log.Lines, line => line.Level >= LogLevel.Warning);
        Assert.Contains("sync.replica_unavailable", warning.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A hand-over the file system refuses — a scanner holding the file, a phone
    /// still reading the last code — keeps the request, and the next poll mints
    /// and hands over again instead of ending the loop.
    /// </summary>
    [Fact]
    public async Task A_hand_over_the_file_system_refuses_keeps_the_request_and_is_tried_again()
    {
        var store = new InMemoryDeviceCredentialStore(Desktop);
        var handoff = new ScriptedHandoff { FailingHandOvers = 1 };
        handoff.RequestCode();
        string? handed = null;
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var fixture = Fixture.Create(
            store,
            (request, _) => Service(request),
            onWait: _ =>
            {
                handed ??= handoff.CodeRequested ? null : handoff.TakeCode();
                if (handed is not null) stopping.Cancel();
            });

        var outcome = await fixture.ServeAsync(store, handoff, stopping.Token);

        Assert.Equal(LocalPairingOutcome.Cancelled, outcome);
        Assert.Equal(2, fixture.Count(CodesPath));
        Assert.Equal(PairingCode, handed);
        Assert.Equal(LocalDevelopmentPairing.HandoffPollInterval, fixture.Waits[0]);
    }

    /// <summary>
    /// Every log line the run writes is one a person may paste into an issue,
    /// so none carries a credential, a token or the pairing code — on the way
    /// that waits, retries and is refused as much as on the way that just works,
    /// on the phone, on the desktop that registers, and on the desktop that mints.
    /// </summary>
    [Fact]
    public async Task No_log_line_carries_a_credential_a_token_or_a_code()
    {
        var phone = new InMemoryDeviceCredentialStore(StalePhone);
        var redeems = 0;
        using var fixture = Fixture.Create(phone, (request, _) =>
            Body(request).Contains(StalePhoneSecret, StringComparison.Ordinal) ? CredentialInvalid()
            : Is(request, "/devices/pair") && redeems++ == 0
                ? StubHttpMessageHandler.Problem(HttpStatusCode.Conflict, "pairing.code_expired", "That code has expired.")
            : Service(request));

        var paired = await fixture.PairPhoneAsync(phone, new ScriptedHandoff { Desktop = _ => PairingCode }, manualPairingRequested: false);

        var desktop = new InMemoryDeviceCredentialStore(StaleDesktop);
        var probes = 0;
        using var registering = Fixture.Create(desktop, (request, _) =>
            Is(request, "/devices/token") && probes++ == 0 ? ReplicaUnavailable() : DesktopService(request));

        var registered = await registering.Pairing.RegisterDesktopAsync(registering.Http, desktop, TestContext.Current.CancellationToken);

        var handoff = new ScriptedHandoff();
        handoff.RequestCode();
        var issues = 0;
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var minting = Fixture.Create(
            new InMemoryDeviceCredentialStore(Desktop),
            (request, _) => Is(request, "/devices/codes") && issues++ == 0 ? ReplicaUnavailable() : Service(request),
            onWait: waits => { if (!handoff.CodeRequested) stopping.Cancel(); });

        await minting.ServeAsync(new InMemoryDeviceCredentialStore(Desktop), handoff, stopping.Token);

        Assert.Equal(LocalPairingOutcome.Paired, paired);
        Assert.Equal(LocalPairingOutcome.Registered, registered);
        Assert.Equal(PairingCode, handoff.TakeCode());

        var lines = fixture.Log.Lines.Concat(registering.Log.Lines).Concat(minting.Log.Lines).ToList();
        Assert.NotEmpty(lines);
        foreach (var secret in new[] { DesktopSecret, PhoneSecret, StaleSecret, StalePhoneSecret, AccessToken, PairingCode, "K7MN-9PQR" })
        {
            Assert.DoesNotContain(lines, line =>
                line.Message.Contains(secret, StringComparison.OrdinalIgnoreCase)
                || line.Properties.Values.Any(value => value?.ToString()?.Contains(secret, StringComparison.OrdinalIgnoreCase) == true));
        }
    }

    /// <summary>The sync service as the phone reaches it when all is well.</summary>
    private static HttpResponseMessage Service(HttpRequestMessage request) =>
        Is(request, "/devices/token") ? Token()
        : Is(request, "/devices/codes") ? Code()
        : Is(request, "/devices/pair") ? Registration(PhoneDevice, PhoneSecret)
        : throw new InvalidOperationException($"Nothing answers {request.RequestUri}.");

    /// <summary>The sync service as the desktop reaches it when all is well: the
    /// readiness probe's made-up device is refused, and registering works.</summary>
    private static HttpResponseMessage DesktopService(HttpRequestMessage request) =>
        Is(request, "/devices/token") ? CredentialInvalid()
        : Is(request, "/devices/register") ? Registration(DesktopDevice, DesktopSecret)
        : throw new InvalidOperationException($"Nothing answers {request.RequestUri}.");

    /// <summary>An ending that does not say whether the call landed.</summary>
    private static HttpResponseMessage Ambiguous(string ending) => ending switch
    {
        "timed out" => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 30 seconds elapsing."),
        "pipeline timed out" => throw new TimeoutRejectedException("The operation didn't complete within the allowed timeout of '00:00:30'."),
        "response dropped" => throw new HttpRequestException(HttpRequestError.ResponseEnded, "The response ended prematurely."),
        "500 with no code" => new HttpResponseMessage(HttpStatusCode.InternalServerError),
        "replica unavailable" => ReplicaUnavailable(),
        _ => throw new ArgumentOutOfRangeException(nameof(ending), ending, null)
    };

    /// <summary>An ending that says the call did not land.</summary>
    private static HttpResponseMessage NotLanded(string ending) => ending switch
    {
        "connection refused" => throw new HttpRequestException(HttpRequestError.ConnectionError, "No connection could be made because the target machine actively refused it."),
        "replica busy" => StubHttpMessageHandler.Problem(
            HttpStatusCode.TooManyRequests, "sync.replica_busy", "The replica is busy; try again shortly."),
        _ => throw new ArgumentOutOfRangeException(nameof(ending), ending, null)
    };

    /// <summary>A service that cannot say yet whether a credential holds.</summary>
    private static HttpResponseMessage Unready(string ending) => ending switch
    {
        "replica unavailable" => ReplicaUnavailable(),
        "connection refused" => throw new HttpRequestException(HttpRequestError.ConnectionError, "No connection could be made because the target machine actively refused it."),
        _ => throw new ArgumentOutOfRangeException(nameof(ending), ending, null)
    };

    private static string Body(HttpRequestMessage request) =>
        request.Content is null ? string.Empty : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();

    private static bool Is(HttpRequestMessage request, string route) =>
        request.RequestUri!.AbsolutePath.EndsWith(route, StringComparison.Ordinal);

    private static HttpResponseMessage ReplicaUnavailable() => StubHttpMessageHandler.Problem(
        HttpStatusCode.ServiceUnavailable, "sync.replica_unavailable", "The replica is not reachable yet.");

    private static HttpResponseMessage CredentialInvalid() => StubHttpMessageHandler.Problem(
        HttpStatusCode.Unauthorized, "device.credential_invalid", "That device id and credential do not match a registered device.");

    private static HttpResponseMessage Token() => StubHttpMessageHandler.Json(
        HttpStatusCode.OK,
        $$"""{"accessToken":"{{AccessToken}}","expiresAt":"2026-10-19T10:30:00+00:00","tokenType":"Bearer"}""");

    private static HttpResponseMessage Code() => StubHttpMessageHandler.Json(
        HttpStatusCode.Created,
        $$"""{"code":"{{PairingCode}}","expiresAt":"2026-10-19T10:10:00+00:00"}""");

    private static HttpResponseMessage Registration(Guid device, string secret) => StubHttpMessageHandler.Json(
        HttpStatusCode.Created,
        $$"""{"ownerId":"{{Owner}}","deviceId":"{{device}}","credential":"{{secret}}"}""");

    private sealed class Fixture : IDisposable
    {
        private Fixture(HttpClient http, StubHttpMessageHandler handler, LocalDevelopmentPairing pairing, CapturingLogger<LocalDevelopmentPairing> log)
        {
            Http = http;
            Handler = handler;
            Pairing = pairing;
            Log = log;
        }

        public StubHttpMessageHandler Handler { get; }

        /// <summary>The wire with nothing in front of it, as the
        /// <see cref="LocalDevelopmentPairing.HttpClientName"/> client is.</summary>
        public HttpClient Http { get; }

        public LocalDevelopmentPairing Pairing { get; }

        public CapturingLogger<LocalDevelopmentPairing> Log { get; }

        /// <summary>How long each wait between attempts was asked to be.</summary>
        public List<TimeSpan> Waits { get; } = [];

        public string[] Paths => [.. Handler.Requests.Select(request => request.Path)];

        public int Count(string path) => Paths.Count(sent => sent == path);

        /// <summary>How many times the phone asked for its cached views to be
        /// forgotten.</summary>
        public int Forgotten { get; private set; }

        public Task<LocalPairingOutcome> PairPhoneAsync(
            IDeviceCredentialStore phone,
            IPairingCodeHandoff handoff,
            bool manualPairingRequested,
            CancellationToken? cancellationToken = null,
            Action? forget = null) =>
            Pairing.PairPhoneAsync(
                Http,
                phone,
                handoff,
                () => manualPairingRequested,
                _ =>
                {
                    Forgotten++;
                    forget?.Invoke();
                    return Task.CompletedTask;
                },
                cancellationToken ?? TestContext.Current.CancellationToken);

        /// <summary>The desktop's side, minting over the same scripted wire with a
        /// token it trades its own credential for.</summary>
        public Task<LocalPairingOutcome> ServeAsync(
            IDeviceCredentialStore desktop,
            IPairingCodeHandoff handoff,
            CancellationToken cancellationToken) =>
            Pairing.ServePairingCodesAsync(Http, desktop, handoff, cancellationToken);

        /// <param name="random">The jitter's draw; a half lands every wait on
        /// its base.</param>
        public static Fixture Create(
            IDeviceCredentialStore credentials,
            Func<HttpRequestMessage, int, HttpResponseMessage> respond,
            Action<int>? onWait = null,
            Func<double>? random = null)
        {
            var handler = new StubHttpMessageHandler(respond);
            var http = new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") };
            var log = new CapturingLogger<LocalDevelopmentPairing>();

            Fixture? fixture = null;
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero));
            var pairing = new LocalDevelopmentPairing(
                log,
                clock,
                (delay, _) =>
                {
                    // The waits complete at once, so a loop that never ends would
                    // hang the run rather than fail it.
                    if (fixture!.Waits.Count >= 100) throw new InvalidOperationException("The pairing kept waiting; a loop that should have ended did not.");
                    fixture.Waits.Add(delay);
                    clock.Advance(delay);
                    onWait?.Invoke(fixture.Waits.Count);
                    return Task.CompletedTask;
                },
                random ?? (() => 0.5),
                LocalDevelopmentPairing.GiveUpAfter);

            fixture = new Fixture(http, handler, pairing, log);
            return fixture;
        }

        public void Dispose() => Http.Dispose();
    }
}
