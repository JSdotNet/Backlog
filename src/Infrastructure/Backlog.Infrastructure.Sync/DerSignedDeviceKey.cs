using System.Buffers.Text;
using System.Formats.Asn1;
using System.Security.Cryptography;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// An <see cref="IDeviceKey"/> over a key this process cannot hold, only ask to sign: the
/// phone's, in Android Keystore (local ADR 0052 §2). Such a key hands out its public half
/// as SubjectPublicKeyInfo and signs <c>SHA256withECDSA</c> as a DER
/// <c>SEQUENCE { r, s }</c>; the assertion the service verifies is ES256, whose signature
/// is <c>r||s</c> at 32 bytes each. This is the conversion, kept off the device so it can
/// be tested.
/// </summary>
public sealed class DerSignedDeviceKey : IDeviceKey
{
    /// <summary>The width of r and of s on P-256.</summary>
    private const int FieldSize = 32;

    private const string P256 = "1.2.840.10045.3.1.7";
    private const string EcPublicKey = "1.2.840.10045.2.1";

    private readonly Func<byte[], byte[]> _signDer;

    /// <param name="subjectPublicKeyInfo">The public half, DER — what
    /// <c>java.security.PublicKey.getEncoded()</c> returns for an EC key.</param>
    /// <param name="signDer">Signs its argument with SHA-256 and the private half and
    /// returns the DER signature. A failure is a
    /// <see cref="CryptographicException"/>.</param>
    /// <exception cref="CryptographicException">The public key is not a P-256 one.</exception>
    public DerSignedDeviceKey(ReadOnlySpan<byte> subjectPublicKeyInfo, Func<byte[], byte[]> signDer)
    {
        ArgumentNullException.ThrowIfNull(signDer);

        // Checked to be sure of what is bound: the service takes P-256 alone, and a key on
        // another curve would be bound and then never verify. The curve is read off the
        // AlgorithmIdentifier itself rather than off an imported key's parameters, which
        // name it differently on each platform. The import then proves the point is on it.
        if (CurveOf(subjectPublicKeyInfo) != P256)
        {
            throw new CryptographicException("The device key is not on P-256.");
        }

        using var key = ECDsa.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
        }
        catch (AsnContentException ex)
        {
            throw new CryptographicException("The device key's public half is not SubjectPublicKeyInfo.", ex);
        }

        PublicKey = Base64Url.EncodeToString(key.ExportSubjectPublicKeyInfo());
        _signDer = signDer;
    }

    public string PublicKey { get; }

    public byte[] Sign(ReadOnlySpan<byte> data) => ToIeeeP1363(_signDer(data.ToArray()));

    /// <summary>
    /// A DER ECDSA signature as P-256's fixed <c>r||s</c>. A DER integer carries no
    /// leading zeros and gains a zero byte before a set high bit, so each half arrives
    /// between 1 and 33 bytes long and is put back at 32.
    /// </summary>
    /// <exception cref="CryptographicException">Not a DER pair of positive integers that
    /// fit the curve.</exception>
    public static byte[] ToIeeeP1363(ReadOnlySpan<byte> der)
    {
        var signature = new byte[FieldSize * 2];

        try
        {
            var reader = new AsnReader(der.ToArray(), AsnEncodingRules.DER);
            var pair = reader.ReadSequence();
            Place(pair.ReadIntegerBytes().Span, signature.AsSpan(0, FieldSize));
            Place(pair.ReadIntegerBytes().Span, signature.AsSpan(FieldSize, FieldSize));
            pair.ThrowIfNotEmpty();
            reader.ThrowIfNotEmpty();
        }
        catch (AsnContentException ex)
        {
            throw new CryptographicException("The device key's signature is not a DER ECDSA signature.", ex);
        }

        return signature;
    }

    /// <summary>The named curve of an EC SubjectPublicKeyInfo:
    /// <c>SEQUENCE { SEQUENCE { id-ecPublicKey, namedCurve }, BIT STRING }</c>.</summary>
    private static string CurveOf(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        try
        {
            var reader = new AsnReader(subjectPublicKeyInfo.ToArray(), AsnEncodingRules.DER);
            var algorithm = reader.ReadSequence().ReadSequence();
            if (algorithm.ReadObjectIdentifier() != EcPublicKey)
            {
                throw new CryptographicException("The device key is not an EC key.");
            }

            return algorithm.ReadObjectIdentifier();
        }
        catch (AsnContentException ex)
        {
            throw new CryptographicException("The device key's public half is not SubjectPublicKeyInfo.", ex);
        }
    }

    private static void Place(ReadOnlySpan<byte> integer, Span<byte> field)
    {
        if (integer.Length > 0 && (integer[0] & 0x80) != 0)
        {
            throw new CryptographicException("The device key's signature holds a negative integer.");
        }

        // The sign byte DER puts before a set high bit.
        if (integer.Length == FieldSize + 1 && integer[0] == 0) integer = integer[1..];

        if (integer.Length > FieldSize)
        {
            throw new CryptographicException("The device key's signature is wider than P-256.");
        }

        integer.CopyTo(field[(FieldSize - integer.Length)..]);
    }
}
