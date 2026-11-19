namespace Backlog.Infrastructure.Sync;

/// <summary>
/// This device's signing key (local ADR 0058 §2): an ECDSA P-256 key whose private
/// half never leaves the store that holds it. A device bound to it proves itself by
/// signing a <see cref="DeviceAssertion"/> instead of presenting a stored secret.
/// </summary>
public interface IDeviceKey
{
    /// <summary>The public half as the service takes it: base64url
    /// SubjectPublicKeyInfo (DER).</summary>
    string PublicKey { get; }

    /// <summary>An ES256 signature over <paramref name="data"/>: SHA-256, the
    /// signature as IEEE P1363 <c>r||s</c>.</summary>
    byte[] Sign(ReadOnlySpan<byte> data);
}
