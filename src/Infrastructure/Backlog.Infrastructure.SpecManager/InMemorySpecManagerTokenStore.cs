using System.Collections.Concurrent;

namespace Backlog.Infrastructure.SpecManager;

/// <summary>
/// A token store that keeps nothing past the process: for a platform without DPAPI,
/// where a sign-in per run is the price of never writing a refresh token in the
/// clear, and for tests.
/// </summary>
public sealed class InMemorySpecManagerTokenStore : ISpecManagerTokenStore
{
    private readonly ConcurrentDictionary<string, SpecManagerCredential> _entries = new(StringComparer.OrdinalIgnoreCase);

    public SpecManagerCredential? Get(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        return _entries.TryGetValue(baseUrl, out var credential) ? credential : null;
    }

    public void Save(string baseUrl, SpecManagerCredential credential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentNullException.ThrowIfNull(credential);

        _entries[baseUrl] = credential;
    }

    public void Remove(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        _entries.TryRemove(baseUrl, out _);
    }
}
