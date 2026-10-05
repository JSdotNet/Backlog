namespace Backlog.Infrastructure.SpecManager;

/// <summary>
/// The token store a head takes when it has no reason to pick another: DPAPI at
/// <paramref name="path"/> on Windows, and in memory elsewhere — a sign-in per run
/// rather than a refresh token written in the clear.
/// </summary>
public static class SpecManagerTokenStoreFactory
{
    /// <param name="path">Where the DPAPI envelope goes;
    /// <see cref="DpapiSpecManagerTokenStore.DefaultPath"/> when null.</param>
    public static ISpecManagerTokenStore Create(string? path = null) =>
        OperatingSystem.IsWindows()
            ? new DpapiSpecManagerTokenStore(path)
            : new InMemorySpecManagerTokenStore();
}
