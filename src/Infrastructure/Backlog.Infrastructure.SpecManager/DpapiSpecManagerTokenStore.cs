using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Backlog.Infrastructure.SpecManager;

/// <summary>
/// The token store on Windows: every installation's entry in one file, serialized
/// as UTF-8 JSON, handed to <see cref="ProtectedData.Protect"/> under
/// <see cref="DataProtectionScope.CurrentUser"/>, and the envelope written as base64
/// text — the envelope <c>DpapiDeviceCredentialStore</c> uses for the sync
/// credential, for the same reason: another account on the machine, or the file
/// copied to another machine, opens nothing.
/// <para>
/// Written through a temporary file and a move, so a crash mid-write leaves the
/// previous envelope rather than half of a new one — which matters more here than
/// for the device credential, because a refresh rotates the token: a lost write is
/// a sign-in the installation has already invalidated.
/// </para>
/// <para>
/// An envelope this user cannot open reads as nothing kept, and the recovery is the
/// one the settings screen already offers: sign in again.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSpecManagerTokenStore : ISpecManagerTokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Lock _gate = new();
    private readonly string _path;
    private Dictionary<string, SpecManagerCredential> _entries;

    /// <summary>A store at <paramref name="path"/>; <see cref="DefaultPath"/> when
    /// null.</summary>
    public DpapiSpecManagerTokenStore(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
        _entries = Read();
    }

    /// <summary><c>%LOCALAPPDATA%\Backlog\spec-manager-credentials.json</c>, beside
    /// the per-user <c>github.json</c>.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Backlog",
        "spec-manager-credentials.json");

    /// <summary>The one file this store writes.</summary>
    public string StorePath => _path;

    public SpecManagerCredential? Get(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        lock (_gate)
        {
            return _entries.TryGetValue(baseUrl, out var credential) ? credential : null;
        }
    }

    public void Save(string baseUrl, SpecManagerCredential credential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(credential);

        lock (_gate)
        {
            var next = new Dictionary<string, SpecManagerCredential>(_entries, StringComparer.OrdinalIgnoreCase)
            {
                [baseUrl] = credential
            };

            // Written before it is adopted: a write that throws leaves this store
            // answering what the file still holds.
            Write(next);
            _entries = next;
        }
    }

    public void Remove(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        lock (_gate)
        {
            if (!_entries.ContainsKey(baseUrl)) return;

            var next = new Dictionary<string, SpecManagerCredential>(_entries, StringComparer.OrdinalIgnoreCase);
            next.Remove(baseUrl);

            Write(next);
            _entries = next;
        }
    }

    private void Write(Dictionary<string, SpecManagerCredential> entries)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(entries, JsonOptions);

        // A name of its own per write, so a leftover from a crashed one is never
        // what this one opens.
        var temporary = $"{_path}.{Guid.NewGuid():n}.tmp";

        try
        {
            var envelope = ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);
            var text = Encoding.ASCII.GetBytes(Convert.ToBase64String(envelope));

            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(text);
                // On the disk, not in a cache, before the move makes it the file:
                // otherwise a power cut after the move can leave an empty one.
                file.Flush(flushToDisk: true);
            }

            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            // The one buffer that holds the tokens in the clear.
            Array.Clear(plaintext);

            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private Dictionary<string, SpecManagerCredential> Read()
    {
        try
        {
            if (!File.Exists(_path)) return new(StringComparer.OrdinalIgnoreCase);

            var envelope = Convert.FromBase64String(File.ReadAllText(_path));
            var plaintext = ProtectedData.Unprotect(envelope, optionalEntropy: null, DataProtectionScope.CurrentUser);
            var entries = JsonSerializer.Deserialize<Dictionary<string, SpecManagerCredential>>(plaintext, JsonOptions);

            Array.Clear(plaintext);

            return entries is null
                ? new(StringComparer.OrdinalIgnoreCase)
                : new(entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Value?.ClientId)), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or FormatException
            or CryptographicException
            or JsonException
            or ArgumentException
            or NotSupportedException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
