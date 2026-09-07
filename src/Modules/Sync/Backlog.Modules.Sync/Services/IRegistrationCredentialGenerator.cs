using System.Buffers.Text;
using System.Security.Cryptography;

namespace Backlog.Modules.Sync.Services;

/// <summary>Mints the long-lived secret a device keeps in its OS credential
/// store.</summary>
public interface IRegistrationCredentialGenerator
{
    /// <summary>A new credential. Shown to the device once; only its hash is
    /// kept here.</summary>
    string Next();
}

/// <summary>
/// 256 bits from the OS random source, Base64Url-encoded — 43 characters, no
/// padding and nothing that needs escaping in a header or a JSON string.
/// <para>
/// The credential is never typed by a person, so there is no reason to make it
/// short or to draw it from a friendly alphabet: it is copied from one process
/// into a credential store. 256 bits is the size at which guessing stops being
/// a consideration at all, which is what lets the stored form be a plain hash
/// (see <see cref="Sha256CredentialHasher"/>).
/// </para>
/// </summary>
public sealed class RandomRegistrationCredentialGenerator : IRegistrationCredentialGenerator
{
    /// <summary>How many characters a Base64Url-encoded 256-bit value takes.</summary>
    internal const int EncodedLength = 43;

    public string Next() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
}
