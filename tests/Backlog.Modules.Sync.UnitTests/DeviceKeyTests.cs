using System.Security.Cryptography;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features.IssueDeviceToken;
using Backlog.Modules.Sync.Features.IssuePairingCode;
using Backlog.Modules.Sync.Features.RedeemPairingCode;
using Backlog.SharedKernel.Results;
using Backlog.Tests;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// A device bound to a non-exportable key (local ADR 0052 §2), through the handlers:
/// it registers or pairs with its public key, gets no credential, and trades a signed
/// <c>private_key_jwt</c> assertion for its token. A credential device binds a key once
/// on its next token fetch and loses the credential.
/// </summary>
public sealed class DeviceKeyTests : IDisposable
{
    private readonly SyncModuleFixture _sync = new();
    private readonly DeviceSigningKey _key = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose() => _key.Dispose();

    [Fact]
    public async Task Registering_with_a_public_key_stores_the_key_and_mints_no_credential()
    {
        var result = await _sync.Register.Handle(
            await _sync.Enrolled("Study desktop") with { PublicKey = _key.PublicKey }, Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Credential);

        var stored = await _sync.Devices.FindById(new DeviceId(result.Value.DeviceId), Cancellation);
        Assert.NotNull(stored);
        Assert.Equal(_key.PublicKey, stored.PublicKey);
        Assert.Null(stored.CredentialHash);
    }

    [Fact]
    public async Task A_public_key_that_is_not_one_is_refused_before_the_enrolment_code_is_spent()
    {
        var enrolled = await _sync.Enrolled("Study desktop");

        var refused = await _sync.Register.Handle(enrolled with { PublicKey = "not-a-key" }, Cancellation);

        Assert.True(refused.IsFailure);
        Assert.Equal(SyncErrorCodes.DevicePublicKeyInvalid, refused.Error.Code);
        Assert.Equal(ErrorType.Validation, refused.Error.Type);

        var retried = await _sync.Register.Handle(enrolled with { PublicKey = _key.PublicKey }, Cancellation);
        Assert.True(retried.IsSuccess);
    }

    [Fact]
    public async Task A_key_on_another_curve_is_refused()
    {
        using var p384 = new DeviceSigningKey(ECCurve.NamedCurves.nistP384);

        var refused = await _sync.Register.Handle(
            await _sync.Enrolled("Study desktop") with { PublicKey = p384.PublicKey }, Cancellation);

        Assert.Equal(SyncErrorCodes.DevicePublicKeyInvalid, refused.Error.Code);
    }

    [Fact]
    public async Task Pairing_with_a_public_key_stores_the_key_and_mints_no_credential()
    {
        var (scope, _) = await _sync.RegisterFirstDevice();
        var code = await _sync.IssueCode.Handle(new IssuePairingCodeCommand(scope), Cancellation);

        var paired = await _sync.Redeem.Handle(
            new RedeemPairingCodeCommand(code.Value.Code, "Phone", _key.PublicKey), Cancellation);

        Assert.True(paired.IsSuccess);
        Assert.Null(paired.Value.Credential);

        var stored = await _sync.Devices.FindById(new DeviceId(paired.Value.DeviceId), Cancellation);
        Assert.Equal(_key.PublicKey, stored!.PublicKey);
        Assert.Null(stored.CredentialHash);
        Assert.Equal(scope.DeviceId, stored.PairedBy);
    }

    [Fact]
    public async Task Pairing_with_a_public_key_that_is_not_one_leaves_the_code_unspent()
    {
        var (scope, _) = await _sync.RegisterFirstDevice();
        var code = await _sync.IssueCode.Handle(new IssuePairingCodeCommand(scope), Cancellation);

        var refused = await _sync.Redeem.Handle(
            new RedeemPairingCodeCommand(code.Value.Code, "Phone", "AAAA"), Cancellation);

        Assert.Equal(SyncErrorCodes.DevicePublicKeyInvalid, refused.Error.Code);
        Assert.True((await _sync.Redeem.Handle(
            new RedeemPairingCodeCommand(code.Value.Code, "Phone", _key.PublicKey), Cancellation)).IsSuccess);
    }

    [Fact]
    public async Task A_valid_assertion_gets_a_token_naming_the_device()
    {
        var device = await RegisterKeyBound();

        var result = await _sync.IssueToken.Handle(Signed(device, _key.Assertion(device.Value, _sync.Clock.GetUtcNow())), Cancellation);

        Assert.True(result.IsSuccess);
        Assert.Equal(device, Assert.Single(_sync.Tokens.Issued).DeviceId);
    }

    [Fact]
    public async Task The_same_assertion_works_once()
    {
        var device = await RegisterKeyBound();
        var assertion = _key.Assertion(device.Value, _sync.Clock.GetUtcNow());

        Assert.True((await _sync.IssueToken.Handle(Signed(device, assertion), Cancellation)).IsSuccess);

        AssertInvalid(await _sync.IssueToken.Handle(Signed(device, assertion), Cancellation));
    }

    [Fact]
    public async Task An_assertion_signed_by_another_key_is_refused()
    {
        var device = await RegisterKeyBound();
        using var other = new DeviceSigningKey();

        AssertInvalid(await _sync.IssueToken.Handle(Signed(device, other.Assertion(device.Value, _sync.Clock.GetUtcNow())), Cancellation));
    }

    [Fact]
    public async Task An_expired_assertion_is_refused_past_the_clock_skew()
    {
        var device = await RegisterKeyBound();
        var issuedAt = _sync.Clock.GetUtcNow();
        var assertion = _key.Assertion(device.Value, issuedAt, TimeSpan.FromMinutes(1));

        _sync.Clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(31));

        AssertInvalid(await _sync.IssueToken.Handle(Signed(device, assertion), Cancellation));
    }

    [Fact]
    public async Task An_assertion_just_past_its_expiry_is_taken_within_the_clock_skew()
    {
        var device = await RegisterKeyBound();
        var assertion = _key.Assertion(device.Value, _sync.Clock.GetUtcNow(), TimeSpan.FromMinutes(1));

        _sync.Clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(10));

        Assert.True((await _sync.IssueToken.Handle(Signed(device, assertion), Cancellation)).IsSuccess);
    }

    [Fact]
    public async Task An_assertion_issued_in_the_future_is_refused()
    {
        var device = await RegisterKeyBound();

        AssertInvalid(await _sync.IssueToken.Handle(
            Signed(device, _key.Assertion(device.Value, _sync.Clock.GetUtcNow().AddMinutes(2))), Cancellation));
    }

    [Fact]
    public async Task An_assertion_for_another_audience_is_refused()
    {
        var device = await RegisterKeyBound();

        AssertInvalid(await _sync.IssueToken.Handle(
            Signed(device, _key.Assertion(device.Value, _sync.Clock.GetUtcNow(), audience: "https://elsewhere.example/token")), Cancellation));
    }

    [Fact]
    public async Task An_assertion_living_longer_than_five_minutes_is_refused()
    {
        var device = await RegisterKeyBound();

        AssertInvalid(await _sync.IssueToken.Handle(
            Signed(device, _key.Assertion(device.Value, _sync.Clock.GetUtcNow(), TimeSpan.FromMinutes(6))), Cancellation));
    }

    [Fact]
    public async Task An_assertion_issued_by_another_device_is_refused()
    {
        var device = await RegisterKeyBound();

        AssertInvalid(await _sync.IssueToken.Handle(
            Signed(device, _key.Assertion(device.Value, _sync.Clock.GetUtcNow(), issuer: Guid.NewGuid())), Cancellation));
    }

    [Fact]
    public async Task An_assertion_naming_another_algorithm_is_refused()
    {
        var device = await RegisterKeyBound();

        AssertInvalid(await _sync.IssueToken.Handle(
            Signed(device, _key.Assertion(device.Value, _sync.Clock.GetUtcNow(), algorithm: "ES384")), Cancellation));
    }

    [Fact]
    public async Task An_assertion_of_another_type_is_refused()
    {
        var device = await RegisterKeyBound();
        var assertion = _key.Assertion(device.Value, _sync.Clock.GetUtcNow());

        AssertInvalid(await _sync.IssueToken.Handle(
            new IssueDeviceTokenCommand(device.Value, ClientAssertionType: "urn:example:other", ClientAssertion: assertion), Cancellation));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a.b")]
    [InlineData("not.a.jwt")]
    [InlineData("e30.e30.")]
    public async Task Something_that_is_not_an_assertion_is_refused(string assertion)
    {
        var device = await RegisterKeyBound();

        AssertInvalid(await _sync.IssueToken.Handle(Signed(device, assertion), Cancellation));
    }

    [Fact]
    public async Task A_key_bound_device_has_no_credential_to_offer()
    {
        var device = await RegisterKeyBound();

        AssertInvalid(await _sync.IssueToken.Handle(new IssueDeviceTokenCommand(device.Value, "anything"), Cancellation));
    }

    [Fact]
    public async Task A_credential_device_cannot_sign_in_by_assertion()
    {
        var (scope, _) = await _sync.RegisterFirstDevice();

        AssertInvalid(await _sync.IssueToken.Handle(
            Signed(scope.DeviceId, _key.Assertion(scope.DeviceId.Value, _sync.Clock.GetUtcNow())), Cancellation));
    }

    [Fact]
    public async Task A_credential_device_binds_its_key_once_and_its_credential_stops_working()
    {
        var (scope, credential) = await _sync.RegisterFirstDevice();
        var device = scope.DeviceId;

        var migrated = await _sync.IssueToken.Handle(
            new IssueDeviceTokenCommand(device.Value, credential, PublicKey: _key.PublicKey), Cancellation);

        Assert.True(migrated.IsSuccess);
        var stored = await _sync.Devices.FindById(device, Cancellation);
        Assert.Equal(_key.PublicKey, stored!.PublicKey);
        Assert.Null(stored.CredentialHash);

        AssertInvalid(await _sync.IssueToken.Handle(new IssueDeviceTokenCommand(device.Value, credential), Cancellation));

        using var other = new DeviceSigningKey();
        AssertInvalid(await _sync.IssueToken.Handle(
            new IssueDeviceTokenCommand(device.Value, credential, PublicKey: other.PublicKey), Cancellation));

        Assert.True((await _sync.IssueToken.Handle(
            Signed(device, _key.Assertion(device.Value, _sync.Clock.GetUtcNow())), Cancellation)).IsSuccess);
    }

    [Fact]
    public async Task A_migration_with_a_key_that_is_not_one_keeps_the_credential()
    {
        var (scope, credential) = await _sync.RegisterFirstDevice();

        var refused = await _sync.IssueToken.Handle(
            new IssueDeviceTokenCommand(scope.DeviceId.Value, credential, PublicKey: "not-a-key"), Cancellation);

        Assert.Equal(SyncErrorCodes.DevicePublicKeyInvalid, refused.Error.Code);
        Assert.True((await _sync.IssueToken.Handle(
            new IssueDeviceTokenCommand(scope.DeviceId.Value, credential), Cancellation)).IsSuccess);
    }

    [Fact]
    public async Task A_migration_with_a_wrong_credential_binds_nothing()
    {
        var (scope, credential) = await _sync.RegisterFirstDevice();

        AssertInvalid(await _sync.IssueToken.Handle(
            new IssueDeviceTokenCommand(scope.DeviceId.Value, "wrong", PublicKey: _key.PublicKey), Cancellation));

        Assert.Null((await _sync.Devices.FindById(scope.DeviceId, Cancellation))!.PublicKey);
        Assert.True((await _sync.IssueToken.Handle(
            new IssueDeviceTokenCommand(scope.DeviceId.Value, credential), Cancellation)).IsSuccess);
    }

    private async Task<DeviceId> RegisterKeyBound()
    {
        var result = await _sync.Register.Handle(
            await _sync.Enrolled("Study desktop") with { PublicKey = _key.PublicKey }, Cancellation);
        Assert.True(result.IsSuccess);
        return new DeviceId(result.Value.DeviceId);
    }

    private static IssueDeviceTokenCommand Signed(DeviceId device, string assertion) =>
        new(device.Value, ClientAssertionType: SyncAssertion.ClientAssertionType, ClientAssertion: assertion);

    private static void AssertInvalid<T>(Result<T> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(SyncErrorCodes.DeviceCredentialInvalid, result.Error.Code);
    }
}
