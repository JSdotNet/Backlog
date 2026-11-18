using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Sync.UnitTests;

/// <summary>
/// The phone's device key (local ADR 0052 §2) off a device: Android Keystore signs
/// <c>SHA256withECDSA</c> as a DER sequence and hands its public key out as
/// SubjectPublicKeyInfo, so <see cref="DerSignedDeviceKey"/> is what turns that into the
/// ES256 assertion the service verifies. A .NET key signing in DER stands in for the
/// Keystore here; the Keystore itself is proved only on a phone.
/// </summary>
public sealed class DerSignedDeviceKeyTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Device = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-18T09:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void An_assertion_signed_in_DER_is_an_ES256_JWT_the_public_key_verifies()
    {
        using var keystore = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = KeystoreShaped(keystore);

        var assertion = DeviceAssertion.Create(Device, key, new FakeTimeProvider(Now));
        var jwt = DeviceKeyClientTests.Jwt.Read(assertion, key.PublicKey);

        Assert.True(jwt.SignatureValid);
        Assert.Equal(SyncAssertion.Algorithm, jwt.Header.GetProperty("alg").GetString());
        Assert.Equal(Device.ToString("D"), jwt.Claims.GetProperty("iss").GetString());
        Assert.Equal(Device.ToString("D"), jwt.Claims.GetProperty("sub").GetString());
        Assert.Equal(SyncAssertion.Audience, jwt.Claims.GetProperty("aud").GetString());
        Assert.Equal(Now.ToUnixTimeSeconds(), jwt.Claims.GetProperty("iat").GetInt64());
        Assert.Equal(Now.Add(DeviceAssertion.Lifetime).ToUnixTimeSeconds(), jwt.Claims.GetProperty("exp").GetInt64());
    }

    [Fact]
    public void The_public_key_is_the_keys_SubjectPublicKeyInfo_in_base64url()
    {
        using var keystore = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var key = KeystoreShaped(keystore);

        Assert.Equal(Base64Url.EncodeToString(keystore.ExportSubjectPublicKeyInfo()), key.PublicKey);
    }

    /// <summary>A DER integer drops leading zero bytes and adds one before a high bit, so
    /// r and s arrive anywhere from 1 to 33 bytes long; each is 32 in ES256. Random
    /// signatures mostly meet the padded case; the short one is pinned below.</summary>
    [Fact]
    public void Every_signature_from_a_DER_signing_key_is_64_bytes_and_verifies()
    {
        using var keystore = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = KeystoreShaped(keystore);

        for (var i = 0; i < 300; i++)
        {
            var data = BitConverter.GetBytes(i);
            var signature = key.Sign(data);

            Assert.Equal(64, signature.Length);
            Assert.True(keystore.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        }
    }

    [Fact]
    public void A_short_integer_is_padded_on_the_left_and_a_sign_byte_is_dropped()
    {
        // SEQUENCE { INTEGER 0x01, INTEGER 0x00 0xFF..(32 bytes) }
        byte[] s = [.. Enumerable.Repeat((byte)0xFF, 32)];
        byte[] der = [0x30, 0x26, 0x02, 0x01, 0x01, 0x02, 0x21, 0x00, .. s];

        var p1363 = DerSignedDeviceKey.ToIeeeP1363(der);

        byte[] expected = [.. new byte[31], 0x01, .. s];
        Assert.Equal(expected, p1363);
    }

    [Theory]
    [InlineData("3006020101020101FF")] // a byte after the sequence
    [InlineData("300302010102")] // r alone, and a stray byte after the sequence
    [InlineData("3003020101")] // no s at all
    [InlineData("0201013006020101020101")] // not a sequence
    public void A_signature_that_is_not_a_DER_pair_of_integers_is_a_cryptographic_failure(string hex)
    {
        Assert.Throws<CryptographicException>(() => DerSignedDeviceKey.ToIeeeP1363(Convert.FromHexString(hex)));
    }

    [Fact]
    public void An_integer_wider_than_the_curve_is_a_cryptographic_failure()
    {
        byte[] wide = [0x01, .. new byte[32]];
        byte[] der = [0x30, 0x26, 0x02, 0x21, .. wide, 0x02, 0x01, 0x01];

        Assert.Throws<CryptographicException>(() => DerSignedDeviceKey.ToIeeeP1363(der));
    }

    [Fact]
    public void A_negative_integer_is_a_cryptographic_failure()
    {
        Assert.Throws<CryptographicException>(() => DerSignedDeviceKey.ToIeeeP1363(Convert.FromHexString("3006020181020101")));
    }

    /// <summary>The service takes P-256 alone: a key on any other curve is refused before
    /// it signs anything, rather than bound and then never verified.</summary>
    [Fact]
    public void A_key_on_another_curve_is_refused()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        Assert.Throws<CryptographicException>(() => new DerSignedDeviceKey(other.ExportSubjectPublicKeyInfo(), SignDer(other)));
    }

    [Fact]
    public void A_key_that_is_not_an_EC_key_is_refused()
    {
        using var rsa = RSA.Create(2048);

        Assert.Throws<CryptographicException>(() => new DerSignedDeviceKey(rsa.ExportSubjectPublicKeyInfo(), _ => []));
    }

    [Fact]
    public void A_public_key_that_is_not_SubjectPublicKeyInfo_is_refused()
    {
        Assert.Throws<CryptographicException>(() => new DerSignedDeviceKey([0x30, 0x00], _ => []));
    }

    [Fact]
    public void A_failure_to_sign_reaches_the_caller_as_a_cryptographic_failure()
    {
        using var keystore = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = new DerSignedDeviceKey(
            keystore.ExportSubjectPublicKeyInfo(),
            _ => throw new CryptographicException("The Keystore refused to sign."));

        Assert.Throws<CryptographicException>(() => key.Sign([1, 2, 3]));
    }

    /// <summary>The phone as the head composes it: its credential in secure storage, a
    /// Keystore-shaped key beside it. Its next token fetch is the migration, and the secret
    /// is then gone from secure storage — and stays gone after a restart.</summary>
    [Fact]
    public async Task A_paired_phone_moves_onto_its_key_on_the_next_fetch_and_deletes_its_stored_secret()
    {
        using var keystore = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keys = new KeystoreShapedStore(KeystoreShaped(keystore));
        var platform = new DeviceKeyClientTests.DictionarySecureValues();
        var store = new SecureValueDeviceCredentialStore(platform, "test");
        await store.SaveAsync(new DeviceCredential(Owner, Device, "Pixel", "a-registration-credential"), TestContext.Current.CancellationToken);
        var sent = new List<DeviceTokenRequest>();
        using var fixture = DeviceKeyClientTests.TokenService(store, keys, sent);

        Assert.Equal("token-0", await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));

        var migration = Assert.Single(sent);
        Assert.Equal("a-registration-credential", migration.Credential);
        Assert.Equal(keys.Key.PublicKey, migration.PublicKey);
        Assert.True(DeviceKeyClientTests.Jwt.Read(migration.ClientAssertion!, migration.PublicKey!).SignatureValid);

        var restarted = new SecureValueDeviceCredentialStore(platform, "test");
        Assert.Equal(new DeviceCredential(Owner, Device, "Pixel", null), restarted.Current);

        fixture.Provider.Invalidate();
        Assert.Equal("token-1", await fixture.Provider.GetAccessTokenAsync(TestContext.Current.CancellationToken));
        Assert.Null(sent[^1].Credential);
        Assert.Null(sent[^1].PublicKey);
        Assert.True(DeviceKeyClientTests.Jwt.Read(sent[^1].ClientAssertion!, keys.Key.PublicKey).SignatureValid);
    }

    /// <summary>A key that hands out SubjectPublicKeyInfo and signs in DER, as Android
    /// Keystore's <c>SHA256withECDSA</c> does.</summary>
    private static DerSignedDeviceKey KeystoreShaped(ECDsa keystore) =>
        new(keystore.ExportSubjectPublicKeyInfo(), SignDer(keystore));

    private static Func<byte[], byte[]> SignDer(ECDsa keystore) =>
        data => keystore.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

    private sealed class KeystoreShapedStore(DerSignedDeviceKey key) : IDeviceKeyStore
    {
        public DerSignedDeviceKey Key { get; } = key;

        public DeviceKeyProtection Protection => DeviceKeyProtection.Hardware;

        public IDeviceKey GetOrCreate() => Key;
    }
}
