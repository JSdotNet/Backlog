using System.ComponentModel.DataAnnotations;

namespace Backlog.Modules.Sync.Api.Options;

/// <summary>
/// How this service signs the device tokens it issues, and therefore how it
/// validates them (inherited ADR 0018: bind, validate, fail fast).
/// <para>
/// The key is a secret and never appears in <c>appsettings.json</c>. A
/// deployment reads it from Key Vault (<see cref="KeyVaultUri"/>, local ADR
/// 0052); only a Development run takes it from configuration. What is checked
/// in is the shape of the section and the non-secret defaults.
/// </para>
/// </summary>
public sealed class SyncTokenOptions
{
    /// <summary>The section this binds to. Named for the module that owns it,
    /// per inherited ADR 0018.</summary>
    public const string SectionName = "Modules:Sync:Tokens";

    /// <summary>How many bytes the signing key must decode to. HS256 takes a
    /// key of at least the hash size, and a shorter one is rejected by the
    /// token library rather than silently weakened.</summary>
    public const int MinimumKeyBytes = 32;

    /// <summary>The vault secret holding the signing key. Rotation adds a
    /// version to it; see docs/deployment/sync.md.</summary>
    public const string SigningKeySecretName = "sync-token-signing-key";

    /// <summary>The HMAC key, base64. At least
    /// <see cref="MinimumKeyBytes"/> bytes once decoded. Read only in a
    /// Development run — user-secrets, or a key generated for the run; a
    /// deployment reads the key from <see cref="KeyVaultUri"/> instead.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>The vault the signing key is read from, through the service's
    /// managed identity (local ADR 0052). Required anywhere but Development;
    /// set, it wins over <see cref="SigningKey"/> in Development too.</summary>
    public string? KeyVaultUri { get; set; }

    /// <summary>How often a running instance re-reads the signing key, so a
    /// rotation reaches it without a restart. Part of the window the previous
    /// key stays accepted for.</summary>
    [Range(1, 15)]
    public int KeyRefreshMinutes { get; set; } = 5;

    /// <summary>Whether the key comes from the vault.</summary>
    public bool UsesKeyVault => !string.IsNullOrWhiteSpace(KeyVaultUri);

    /// <summary>Who issued the token. A URI so it reads as a name rather than a
    /// word, which is the convention every JWT library expects.</summary>
    public string Issuer { get; set; } = "https://backlog.jsdotnet.dev/sync";

    /// <summary>Who the token is for. This service and nothing else.</summary>
    public string Audience { get; set; } = "backlog-sync";

    /// <summary>How long a device token is good for. Inherited ADR 0012 puts
    /// the band at 15 to 60 minutes, and the range is that band: a deployment
    /// that asked for something shorter would not start.</summary>
    [Range(15, 60)]
    public int LifetimeMinutes { get; set; } = 30;

    /// <summary>The key as bytes, or an empty array when
    /// <see cref="SigningKey"/> is not base64.</summary>
    public byte[] DecodeSigningKey()
    {
        Span<byte> buffer = new byte[SigningKey.Length];

        return Convert.TryFromBase64String(SigningKey, buffer, out var written)
            ? buffer[..written].ToArray()
            : [];
    }

    /// <summary>Whether the configured key is usable. Checked at startup so a
    /// misconfigured deployment fails to start rather than failing on the first
    /// device that tries to sync.</summary>
    public bool HasUsableSigningKey() => DecodeSigningKey().Length >= MinimumKeyBytes;
}
