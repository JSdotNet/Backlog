namespace Backlog.Infrastructure.Sync;

/// <summary>Where a resolved sync address came from — the thing the Settings
/// page shows next to it, so "No such host is known" has an explanation.</summary>
public enum SyncServiceAddressSource
{
    /// <summary>Nothing set anywhere. The address is still the service-discovery
    /// name, and outside an AppHost run it will not resolve — or, for
    /// <see cref="SyncServiceEndpoint.ResolveStandalone"/>, the head's own fallback.</summary>
    None,

    /// <summary>Launched by the Aspire AppHost, which injected the
    /// <c>services__sync__*</c> variables service discovery reads.</summary>
    ServiceDiscovery,

    /// <summary>The <see cref="SyncServiceEndpoint.EnvironmentVariable"/> variable.</summary>
    EnvironmentVariable,

    /// <summary>The URL entered on the Settings page.</summary>
    Settings,

    /// <summary>The address compiled into a head at build time — the release
    /// APK's way to reach the deployed service with nothing set on the phone.</summary>
    BuildDefault
}

/// <summary>The address the sync clients will use and why.</summary>
public sealed record SyncServiceAddress(Uri Address, SyncServiceAddressSource Source);

/// <summary>
/// Which sync service this head talks to.
/// <para>
/// Under an AppHost run the answer is service discovery: <c>https+http://sync</c>
/// is rewritten to the sync resource of <em>this</em> run, ports are dynamic and a
/// literal one would be wrong by the next launch. The installed app is never
/// launched that way, and for it the name <c>sync</c> is a DNS lookup that fails.
/// So the discovery name is the last rung, and two sit above it: the URL the
/// person entered on the Settings page, then <see cref="EnvironmentVariable"/>,
/// which is the same variable the mobile head honours when it runs standalone.
/// </para>
/// <para>
/// Resolved on every call rather than once at startup, because the hosts hand it
/// to <c>IHttpClientFactory</c> as the base-address callback: a URL saved on the
/// Settings page reaches the next client the factory creates, and nobody has to
/// restart the app to find out whether they typed it right.
/// </para>
/// </summary>
public sealed class SyncServiceEndpoint
{
    /// <summary>The variable a standalone head reads for the service's base URL.
    /// Shared with <c>Backlog.Mobile</c> on purpose: one name to document.</summary>
    public const string EnvironmentVariable = "BACKLOG_SYNC_URL";

    /// <summary>The name Aspire service discovery resolves under an AppHost run.</summary>
    public static readonly Uri ServiceDiscoveryAddress = new("https+http://sync");

    // Either of the two Aspire writes for a resource with both endpoints. The
    // check is only for the source reported back; the address is the same
    // whether they are present or not, so a head under Aspire behaves exactly
    // as it did before this type existed.
    private static readonly string[] ServiceDiscoveryVariables =
    [
        "services__sync__https__0",
        "services__sync__http__0"
    ];

    private readonly SyncServiceSettingsStore _settings;
    private readonly Func<string, string?> _environment;

    public SyncServiceEndpoint(SyncServiceSettingsStore settings)
        : this(settings, Environment.GetEnvironmentVariable)
    {
    }

    public SyncServiceEndpoint(SyncServiceSettingsStore settings, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(environment);
        _settings = settings;
        _environment = environment;
    }

    public SyncServiceAddress Resolve()
    {
        if (TryParse(_settings.Current.ServiceUrl, out var fromSettings))
        {
            return new SyncServiceAddress(fromSettings, SyncServiceAddressSource.Settings);
        }

        if (TryParse(_environment(EnvironmentVariable), out var fromEnvironment))
        {
            return new SyncServiceAddress(fromEnvironment, SyncServiceAddressSource.EnvironmentVariable);
        }

        var discovered = ServiceDiscoveryVariables.Any(name => !string.IsNullOrWhiteSpace(_environment(name)));
        return new SyncServiceAddress(
            ServiceDiscoveryAddress,
            discovered ? SyncServiceAddressSource.ServiceDiscovery : SyncServiceAddressSource.None);
    }

    /// <summary>
    /// The address for a head with no Settings page and no service discovery —
    /// the phone. Its rungs, in order: the AppHost's <c>services__sync__http__0</c>
    /// taken literally, because nothing on the device rewrites the discovery name;
    /// <see cref="EnvironmentVariable"/>; the address compiled in at build time;
    /// and the head's own <paramref name="fallback"/>.
    /// <para>
    /// Pure and resolved once, unlike <see cref="Resolve"/>: the phone has no
    /// Settings URL to change underneath it, so there is nothing to re-read.
    /// </para>
    /// </summary>
    public static SyncServiceAddress ResolveStandalone(
        Func<string, string?> environment,
        string? buildDefault,
        Uri fallback)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(fallback);

        if (TryParse(environment("services__sync__http__0"), out var discovered))
        {
            return new SyncServiceAddress(discovered, SyncServiceAddressSource.ServiceDiscovery);
        }

        if (TryParse(environment(EnvironmentVariable), out var fromEnvironment))
        {
            return new SyncServiceAddress(fromEnvironment, SyncServiceAddressSource.EnvironmentVariable);
        }

        if (TryParse(buildDefault, out var baked))
        {
            return new SyncServiceAddress(baked, SyncServiceAddressSource.BuildDefault);
        }

        return new SyncServiceAddress(fallback, SyncServiceAddressSource.None);
    }

    // A malformed value is skipped, not thrown: this runs inside the client
    // factory's configure callback, where an exception would surface as a
    // failed sync with no mention of the variable that caused it.
    private static bool TryParse(string? value, out Uri address)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            address = parsed;
            return true;
        }

        address = ServiceDiscoveryAddress;
        return false;
    }
}
