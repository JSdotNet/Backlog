using System.Buffers.Text;
using System.Security.Cryptography;

namespace Backlog.Infrastructure.Sync;

/// <summary>An <see cref="IDeviceKey"/> over an <see cref="ECDsa"/> this process
/// holds — a software key, or a CNG one opened through <c>ECDsaCng</c>.</summary>
internal sealed class EcdsaDeviceKey(ECDsa key) : IDeviceKey, IDisposable
{
    public string PublicKey { get; } = Base64Url.EncodeToString(key.ExportSubjectPublicKeyInfo());

    public byte[] Sign(ReadOnlySpan<byte> data) =>
        key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    public void Dispose() => key.Dispose();
}
