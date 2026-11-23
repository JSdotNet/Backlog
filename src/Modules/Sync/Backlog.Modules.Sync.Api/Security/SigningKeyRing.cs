using Backlog.Modules.Sync.Api.Options;

namespace Backlog.Modules.Sync.Api.Security;

/// <summary>
/// The keys one load of the signing key produced: the one this service signs
/// with, and the one it signed with before the last rotation, accepted until
/// <see cref="PreviousAcceptedUntil"/> so a device holding a token minted just
/// before the rotation is not signed out by it.
/// </summary>
internal sealed record SigningKeys(byte[] Current, byte[]? Previous = null, DateTimeOffset? PreviousAcceptedUntil = null);

/// <summary>Where the signing key comes from: the vault in a deployment,
/// configuration in a Development run.</summary>
internal interface ISigningKeySource
{
    Task<SigningKeys> LoadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The signing keys this process holds, shared by the token issuer, the token
/// validator and the cursor codec so the three never disagree on which key is
/// current. Loaded before the host starts serving and refreshed on a timer by
/// <see cref="SigningKeyRefresher"/>; a refresh that fails keeps the keys it
/// had.
/// </summary>
internal sealed class SigningKeyRing(ISigningKeySource source, TimeProvider clock)
{
    private volatile SigningKeys? _keys;

    /// <summary>A ring holding one fixed key and nothing else, for code that
    /// needs the ring without a host around it.</summary>
    internal static SigningKeyRing Fixed(byte[] key)
    {
        var ring = new SigningKeyRing(new FixedSource(new SigningKeys(key)), TimeProvider.System);
        ring._keys = new SigningKeys(key);
        return ring;
    }

    /// <summary>The key every token and cursor is signed with now.</summary>
    public byte[] Current => Keys.Current;

    /// <summary>
    /// Every key a token or cursor may carry a valid signature under, current
    /// first: the previous key only while its acceptance window is open.
    /// </summary>
    public IReadOnlyList<byte[]> Accepted()
    {
        var keys = Keys;

        return keys is { Previous: { } previous, PreviousAcceptedUntil: { } until } && clock.GetUtcNow() < until
            ? [keys.Current, previous]
            : [keys.Current];
    }

    /// <summary>Loads the keys from the source, refusing a key shorter than
    /// <see cref="SyncTokenOptions.MinimumKeyBytes"/> so a bad rotation never
    /// replaces a good key.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var loaded = await source.LoadAsync(cancellationToken).ConfigureAwait(false);

        if (loaded.Current.Length < SyncTokenOptions.MinimumKeyBytes)
        {
            throw new InvalidOperationException(
                $"The current signing key decodes to {loaded.Current.Length} bytes; at least {SyncTokenOptions.MinimumKeyBytes} are required.");
        }

        // A previous key that is too short is dropped rather than refused: it
        // only ever widens what is accepted, and the current key is what matters.
        _keys = loaded.Previous is { Length: < SyncTokenOptions.MinimumKeyBytes }
            ? loaded with { Previous = null, PreviousAcceptedUntil = null }
            : loaded;
    }

    private SigningKeys Keys => _keys
        ?? throw new InvalidOperationException("The signing keys have not been loaded; SigningKeyRefresher loads them before the host starts.");

    private sealed class FixedSource(SigningKeys keys) : ISigningKeySource
    {
        public Task<SigningKeys> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(keys);
    }
}
