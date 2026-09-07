using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// The credential store ADR 0005 calls "the OS credential store — DPAPI on
/// Windows".
/// <para>
/// The record is serialized as UTF-8 JSON, handed to
/// <see cref="ProtectedData.Protect"/> under
/// <see cref="DataProtectionScope.CurrentUser"/>, and the envelope written out
/// as base64 text. The key never leaves the OS: another account on the same
/// machine reads the file and gets nothing, and copying it to another machine
/// gets nothing either. That is the whole reason the credential is not simply
/// another line in a settings file.
/// </para>
/// <para>
/// The file keeps the <c>.json</c> name even though what lands on disk is
/// base64: what is inside the envelope is JSON, and naming it for the envelope
/// would tell a reader less rather than more.
/// </para>
/// <para>
/// An envelope this user cannot open — a file copied from another profile, a
/// half-written one, a leftover from an earlier format — reads as "no
/// credential" rather than as an error. There is exactly one recovery from a
/// credential that cannot be read, which is to pair the device again, and that
/// is what an unpaired screen already offers. No logger is taken here on
/// purpose: the store is constructed by hosts as a plain object and its one
/// failure mode is already visible on the screen that reads it.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiDeviceCredentialStore : IDeviceCredentialStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    /// <summary>The per-user location, beside the app's other local state.</summary>
    public DpapiDeviceCredentialStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog",
            "device-credential.json"))
    {
    }

    public DpapiDeviceCredentialStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Current = Read();
    }

    public event Action? Changed;

    public DeviceCredential? Current { get; private set; }

    public string StorePath => _path;

    /// <summary>
    /// Writes the credential, then adopts it. In that order on purpose: a write
    /// that fails throws, and leaves <see cref="Current"/> and
    /// <see cref="Changed"/> exactly as they were. Keeping the credential for
    /// the run and telling everybody it changed would put the app in a state it
    /// cannot get back to — paired until the process exits, unpaired after it,
    /// with nothing on screen having said so. The caller — the pairing client —
    /// turns the exception into a failure a person can read.
    /// </summary>
    public void Save(DeviceCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(credential, JsonOptions);

        try
        {
            var envelope = ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);

            File.WriteAllText(_path, Convert.ToBase64String(envelope));
        }
        finally
        {
            // The plaintext is cleared rather than left for the collector: it is
            // the one buffer in this class that holds the secret in the clear,
            // and a failed write is exactly when it must not survive.
            Array.Clear(plaintext);
        }

        Current = credential;
        Changed?.Invoke();
    }

    public void Clear()
    {
        Current = null;

        try
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        Changed?.Invoke();
    }

    private DeviceCredential? Read()
    {
        try
        {
            if (!File.Exists(_path)) return null;

            var envelope = Convert.FromBase64String(File.ReadAllText(_path));
            var plaintext = ProtectedData.Unprotect(envelope, optionalEntropy: null, DataProtectionScope.CurrentUser);
            var credential = JsonSerializer.Deserialize<DeviceCredential>(plaintext, JsonOptions);

            Array.Clear(plaintext);

            return credential is null || string.IsNullOrWhiteSpace(credential.Credential) ? null : credential;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or FormatException
            or CryptographicException
            or JsonException
            or ArgumentException)
        {
            return null;
        }
    }
}
