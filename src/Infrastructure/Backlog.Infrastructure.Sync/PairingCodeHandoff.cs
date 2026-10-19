using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// How the development harnesses pass a pairing code from the desktop to the
/// phone, and nothing else: the phone asks, the desktop mints a code with its own
/// credential and hands it over, and the phone takes it once. Only a code crosses
/// between them, never a credential (<c>.devbook/domain/sync/features.md#pairing-a-device</c>:
/// neither device ever holds the other's credential). Development only.
/// </summary>
public interface IPairingCodeHandoff
{
    /// <summary>Whether the phone has asked for a code that has not been handed
    /// over yet. Read by the desktop.</summary>
    bool CodeRequested { get; }

    /// <summary>Whether a code has been handed over and not taken yet. Read by
    /// the phone, before <see cref="TakeCode"/>, to tell a code that could not be
    /// read from one that has not come.</summary>
    bool CodeHandedOver { get; }

    /// <summary>The phone asks for a code, discarding any code left from an
    /// earlier request: it may belong to an owner the desktop has since
    /// replaced.</summary>
    void RequestCode();

    /// <summary>The desktop hands a code over and withdraws the request it
    /// answers.</summary>
    void HandOver(string code);

    /// <summary>The desktop withdraws a request it has given up on serving, so
    /// only a request a phone makes afresh is served.</summary>
    void WithdrawRequest();

    /// <summary>The phone takes the code handed over, so it is read once; or
    /// <c>null</c> when there is none yet.</summary>
    string? TakeCode();
}

/// <summary>
/// <see cref="IPairingCodeHandoff"/> as two files in one folder, both harnesses
/// running as the same Windows user: an empty request file the phone writes and
/// the desktop removes, and the code in the envelope a stored credential gets —
/// DPAPI under <see cref="DataProtectionScope.CurrentUser"/>, base64 on disk, never
/// the code in the clear (ADR 0030). The code is written beside its final name
/// and moved into place, so the phone never reads half of one.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiPairingCodeHandoff : IPairingCodeHandoff
{
    private const string RequestFileName = "pairing-code.request";
    private const string CodeFileName = "pairing-code";

    private readonly string _request;
    private readonly string _code;

    public DpapiPairingCodeHandoff(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        Folder = Path.GetFullPath(folder);
        _request = Path.Combine(Folder, RequestFileName);
        _code = Path.Combine(Folder, CodeFileName);
    }

    /// <summary>The folder both harnesses were given.</summary>
    public string Folder { get; }

    public bool CodeRequested => File.Exists(_request);

    public bool CodeHandedOver => File.Exists(_code);

    public void RequestCode()
    {
        Directory.CreateDirectory(Folder);
        Delete(_code);
        File.WriteAllText(_request, string.Empty);
    }

    public void HandOver(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        Directory.CreateDirectory(Folder);

        var plaintext = Encoding.UTF8.GetBytes(code);
        var staged = _code + ".tmp";

        try
        {
            var envelope = ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);
            File.WriteAllText(staged, Convert.ToBase64String(envelope));
        }
        finally
        {
            Array.Clear(plaintext);
        }

        File.Move(staged, _code, overwrite: true);
        Delete(_request);
    }

    public void WithdrawRequest() => Delete(_request);

    public string? TakeCode()
    {
        if (!File.Exists(_code)) return null;

        try
        {
            var envelope = Convert.FromBase64String(File.ReadAllText(_code));
            var plaintext = ProtectedData.Unprotect(envelope, optionalEntropy: null, DataProtectionScope.CurrentUser);
            var code = Encoding.UTF8.GetString(plaintext);
            Array.Clear(plaintext);
            return string.IsNullOrWhiteSpace(code) ? null : code;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or IOException or UnauthorizedAccessException)
        {
            // Not an envelope this user can open: no code. The phone saw one
            // was handed over (CodeHandedOver) and asks again, so nothing is
            // lost by dropping it.
            return null;
        }
        finally
        {
            Delete(_code);
        }
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
