using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.DomainModels;
using Backlog.Modules.Sync.Features.DescribeOwner;
using Backlog.Modules.Sync.Features.IssueDeviceToken;
using Backlog.Modules.Sync.Features.IssuePairingCode;
using Backlog.Modules.Sync.Features.RedeemPairingCode;
using Backlog.Modules.Sync.Features.RegisterFirstDevice;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// The pairing exchange, end to end through the handlers: a first device mints
/// an owner, a code carries a second device into it, and each device trades its
/// credential for a token that names that owner and nothing else.
/// </summary>
public class DevicePairingTests
{
    private readonly SyncModuleFixture _sync = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Registering_the_first_device_mints_an_owner_and_a_device()
    {
        var result = Assert.IsType<Ok<DeviceRegistrationResponse>>((await _sync.Register.Handle(await _sync.Enrolled("Study desktop"), Cancellation)).Value).Value;

        Assert.NotEqual(Guid.Empty, result.OwnerId);
        Assert.NotEqual(Guid.Empty, result.DeviceId);
        Assert.NotEqual(result.OwnerId, result.DeviceId);
    }

    [Fact]
    public async Task Two_registrations_are_two_owners()
    {
        var first = Assert.IsType<Ok<DeviceRegistrationResponse>>((await _sync.Register.Handle(await _sync.Enrolled("One"), Cancellation)).Value).Value;
        var second = Assert.IsType<Ok<DeviceRegistrationResponse>>((await _sync.Register.Handle(await _sync.Enrolled("Two"), Cancellation)).Value).Value;

        Assert.NotEqual(first.OwnerId, second.OwnerId);
        Assert.NotEqual(first.Credential, second.Credential);
    }

    [Fact]
    public async Task The_registration_credential_is_256_bits_of_base64url()
    {
        var result = Assert.IsType<Ok<DeviceRegistrationResponse>>((await _sync.Register.Handle(await _sync.Enrolled("Study desktop"), Cancellation)).Value).Value;

        Assert.Equal(43, result.Credential!.Length);
        Assert.DoesNotContain('=', result.Credential);
        Assert.DoesNotContain('+', result.Credential);
        Assert.DoesNotContain('/', result.Credential);
    }

    [Fact]
    public async Task Only_the_hash_of_the_credential_is_stored()
    {
        var result = Assert.IsType<Ok<DeviceRegistrationResponse>>((await _sync.Register.Handle(await _sync.Enrolled("Study desktop"), Cancellation)).Value).Value;

        var stored = await _sync.Devices.FindById(new DeviceId(result.DeviceId), Cancellation);

        Assert.NotNull(stored);
        Assert.NotEqual(result.Credential, stored.CredentialHash);
        Assert.Equal(_sync.Hasher.Hash(result.Credential!), stored.CredentialHash);
        Assert.Equal(64, stored.CredentialHash!.Length);
    }

    [Fact]
    public async Task A_device_has_to_be_called_something()
    {
        var result = Assert.IsType<Error>((await _sync.Register.Handle(new RegisterFirstDeviceCommand("   "), Cancellation)).Value);

        Assert.Equal(SyncErrorCodes.DeviceNameRequired, result.Code);
        Assert.Equal(ErrorType.Validation, result.Type);
    }

    [Fact]
    public async Task A_pairing_code_is_eight_symbols_from_the_alphabet_good_for_ten_minutes()
    {
        var (scope, _) = await _sync.RegisterFirstDevice();

        var result = Assert.IsType<Ok<PairingCodeResponse>>((await _sync.IssueCode.Handle(new IssuePairingCodeCommand(scope), Cancellation)).Value).Value;

        Assert.Equal(PairingCodeFormat.Length, result.Code.Length);
        Assert.True(PairingCodeFormat.IsWellFormed(result.Code));
        Assert.Equal(_sync.Clock.GetUtcNow().AddMinutes(10), result.ExpiresAt);
    }

    [Fact]
    public async Task Only_the_hash_of_the_pairing_code_is_stored()
    {
        var (scope, _) = await _sync.RegisterFirstDevice();
        var issued = Assert.IsType<Ok<PairingCodeResponse>>((await _sync.IssueCode.Handle(new IssuePairingCodeCommand(scope), Cancellation)).Value).Value;

        Assert.Null(await _sync.Codes.FindByHash(issued.Code, Cancellation));

        var stored = await _sync.Codes.FindByHash(_sync.Hasher.Hash(issued.Code), Cancellation);
        Assert.NotNull(stored);
        Assert.Equal(scope.OwnerId, stored.OwnerId);
        Assert.Equal(scope.DeviceId, stored.IssuedBy);
    }

    [Fact]
    public async Task Redeeming_a_code_joins_the_same_owner_as_a_new_device()
    {
        var (scope, firstCredential) = await _sync.RegisterFirstDevice();
        var issued = Assert.IsType<Ok<PairingCodeResponse>>((await _sync.IssueCode.Handle(new IssuePairingCodeCommand(scope), Cancellation)).Value).Value;

        var result = Assert.IsType<Ok<DeviceRegistrationResponse>>((await _sync.Redeem.Handle(
            new RedeemPairingCodeCommand(issued.Code, "Phone"), Cancellation)).Value).Value;

        Assert.Equal(scope.OwnerId.Value, result.OwnerId);
        Assert.NotEqual(scope.DeviceId.Value, result.DeviceId);
        Assert.NotEqual(firstCredential, result.Credential);
    }

    [Fact]
    public async Task A_code_is_accepted_in_the_form_it_was_displayed_in()
    {
        var (scope, _) = await _sync.RegisterFirstDevice();
        var issued = Assert.IsType<Ok<PairingCodeResponse>>((await _sync.IssueCode.Handle(new IssuePairingCodeCommand(scope), Cancellation)).Value).Value;

        var result = await _sync.Redeem.Handle(
            new RedeemPairingCodeCommand(PairingCodeFormat.Display(issued.Code).ToLowerInvariant(), "Phone"),
            Cancellation);

        Assert.IsType<Ok<DeviceRegistrationResponse>>(result.Value);
    }

    [Fact]
    public async Task A_code_that_is_not_a_code_is_a_validation_failure()
    {
        var result = Assert.IsType<Error>((await _sync.Redeem.Handle(new RedeemPairingCodeCommand("no", "Phone"), Cancellation)).Value);

        Assert.Equal(SyncErrorCodes.PairingCodeMalformed, result.Code);
        Assert.Equal(ErrorType.Validation, result.Type);
    }

    [Fact]
    public async Task A_well_formed_code_nobody_issued_is_not_found()
    {
        var result = Assert.IsType<Error>((await _sync.Redeem.Handle(new RedeemPairingCodeCommand("23456789", "Phone"), Cancellation)).Value);

        Assert.Equal(SyncErrorCodes.PairingCodeNotFound, result.Code);
        Assert.Equal(ErrorType.NotFound, result.Type);
    }

    [Fact]
    public async Task A_code_stops_working_after_ten_minutes()
    {
        var (scope, _) = await _sync.RegisterFirstDevice();
        var issued = Assert.IsType<Ok<PairingCodeResponse>>((await _sync.IssueCode.Handle(new IssuePairingCodeCommand(scope), Cancellation)).Value).Value;

        _sync.Clock.Advance(TimeSpan.FromMinutes(10));

        var result = Assert.IsType<Error>((await _sync.Redeem.Handle(
            new RedeemPairingCodeCommand(issued.Code, "Phone"), Cancellation)).Value);

        Assert.Equal(SyncErrorCodes.PairingCodeExpired, result.Code);
        Assert.Equal(ErrorType.Conflict, result.Type);
    }

    [Fact]
    public async Task A_code_still_works_a_minute_before_it_expires()
    {
        var (scope, _) = await _sync.RegisterFirstDevice();
        var issued = Assert.IsType<Ok<PairingCodeResponse>>((await _sync.IssueCode.Handle(new IssuePairingCodeCommand(scope), Cancellation)).Value).Value;

        _sync.Clock.Advance(TimeSpan.FromMinutes(9));

        var result = await _sync.Redeem.Handle(
            new RedeemPairingCodeCommand(issued.Code, "Phone"), Cancellation);

        Assert.IsType<Ok<DeviceRegistrationResponse>>(result.Value);
    }

    [Fact]
    public async Task A_code_pairs_one_device_and_no_more()
    {
        var (scope, _) = await _sync.RegisterFirstDevice();
        var issued = Assert.IsType<Ok<PairingCodeResponse>>((await _sync.IssueCode.Handle(new IssuePairingCodeCommand(scope), Cancellation)).Value).Value;

        Assert.IsType<Ok<DeviceRegistrationResponse>>((await _sync.Redeem.Handle(
            new RedeemPairingCodeCommand(issued.Code, "Phone"), Cancellation)).Value);

        var second = Assert.IsType<Error>((await _sync.Redeem.Handle(
            new RedeemPairingCodeCommand(issued.Code, "Tablet"), Cancellation)).Value);

        Assert.Equal(SyncErrorCodes.PairingCodeUsed, second.Code);
        Assert.Equal(ErrorType.Conflict, second.Type);
        Assert.Equal(2, await _sync.Devices.CountByOwner(scope.OwnerId, Cancellation));
    }

    [Fact]
    public async Task A_paired_device_trades_its_credential_for_a_token_naming_its_owner()
    {
        var (scope, credential) = await _sync.RegisterFirstDevice();

        var result = Assert.IsType<Ok<DeviceTokenResponse>>((await _sync.IssueToken.Handle(
            new IssueDeviceTokenCommand(scope.DeviceId.Value, credential), Cancellation)).Value).Value;

        Assert.Equal("Bearer", result.TokenType);
        Assert.Equal(scope, Assert.Single(_sync.Tokens.Issued));
    }

    [Fact]
    public async Task A_wrong_credential_gets_no_token()
    {
        var (scope, _) = await _sync.RegisterFirstDevice();

        var result = Assert.IsType<Error>((await _sync.IssueToken.Handle(
            new IssueDeviceTokenCommand(scope.DeviceId.Value, "not-the-credential"), Cancellation)).Value);

        Assert.Equal(SyncErrorCodes.DeviceCredentialInvalid, result.Code);
        Assert.Empty(_sync.Tokens.Issued);
    }

    [Fact]
    public async Task An_unknown_device_fails_the_same_way_a_wrong_credential_does()
    {
        var (_, credential) = await _sync.RegisterFirstDevice();

        var result = Assert.IsType<Error>((await _sync.IssueToken.Handle(
            new IssueDeviceTokenCommand(Guid.CreateVersion7(), credential), Cancellation)).Value);

        Assert.Equal(SyncErrorCodes.DeviceCredentialInvalid, result.Code);
    }

    [Fact]
    public async Task A_credential_belonging_to_another_device_gets_no_token()
    {
        var (first, _) = await _sync.RegisterFirstDevice("One");
        var second = Assert.IsType<Ok<DeviceRegistrationResponse>>((await _sync.Register.Handle(await _sync.Enrolled("Two"), Cancellation)).Value).Value;

        var result = Assert.IsType<Error>((await _sync.IssueToken.Handle(
            new IssueDeviceTokenCommand(first.DeviceId.Value, second.Credential), Cancellation)).Value);

        Assert.Equal(SyncErrorCodes.DeviceCredentialInvalid, result.Code);
    }

    [Fact]
    public async Task Describing_the_owner_counts_its_devices()
    {
        var (scope, _) = await _sync.RegisterFirstDevice("Study desktop");
        var issued = Assert.IsType<Ok<PairingCodeResponse>>((await _sync.IssueCode.Handle(new IssuePairingCodeCommand(scope), Cancellation)).Value).Value;
        await _sync.Redeem.Handle(new RedeemPairingCodeCommand(issued.Code, "Phone"), Cancellation);

        var result = Assert.IsType<Ok<DeviceStatusResponse>>((await _sync.Describe.Handle(new DescribeOwnerQuery(scope), Cancellation)).Value).Value;

        Assert.Equal(scope.OwnerId.Value, result.OwnerId);
        Assert.Equal(scope.DeviceId.Value, result.DeviceId);
        Assert.Equal("Study desktop", result.DeviceName);
        Assert.Equal(2, result.PairedDeviceCount);
    }

    [Fact]
    public async Task Another_owners_devices_are_not_counted()
    {
        var (mine, _) = await _sync.RegisterFirstDevice("Mine");
        await _sync.Register.Handle(await _sync.Enrolled("Somebody elses laptop"), Cancellation);

        var result = Assert.IsType<Ok<DeviceStatusResponse>>((await _sync.Describe.Handle(new DescribeOwnerQuery(mine), Cancellation)).Value).Value;

        Assert.Equal(1, result.PairedDeviceCount);
    }

    [Fact]
    public async Task A_scope_naming_a_device_that_is_not_registered_is_rejected()
    {
        var scope = new OwnerScope(OwnerId.New(), DeviceId.New());

        var result = Assert.IsType<Error>((await _sync.Describe.Handle(new DescribeOwnerQuery(scope), Cancellation)).Value);

        Assert.Equal(SyncErrorCodes.DeviceCredentialInvalid, result.Code);
    }
}
