namespace Backlog.Infrastructure.SpecManager;

/// <summary>
/// Which spec-manager installation the connector talks to.
/// <para>
/// One value, and never a token: what the sign-in is given is the token store's
/// (<see cref="ISpecManagerTokenStore"/>), kept per base URL so pointing this at a
/// second installation signs in there separately rather than sending one
/// installation's token to another.
/// </para>
/// <para>
/// <see cref="SectionName"/> names the section a host binds it from (ADR 0018 in
/// <c>.devbook/arc42/adr/guidelines/</c>). The desktop composition binds no
/// configuration today, so <see cref="SpecManagerRegistration.AddSpecManager"/>
/// takes a delegate instead and the default below is what ships.
/// </para>
/// </summary>
public sealed class SpecManagerOptions
{
    /// <summary>The configuration section these options belong to.</summary>
    public const string SectionName = "SpecManager";

    /// <summary>The production installation.</summary>
    public const string DefaultBaseUrl = "https://specmanager-app.lemonbush-705caac9.westeurope.azurecontainerapps.io";

    /// <summary>The installation's root, absolute, http or https.</summary>
    public Uri BaseUrl { get; set; } = new(DefaultBaseUrl);

    /// <summary>The base URL as the token store keys it and as every path is
    /// appended to: scheme, host, port and path, without a trailing slash.</summary>
    internal string Root => BaseUrl.GetLeftPart(UriPartial.Path).TrimEnd('/');

    /// <summary>Whether <see cref="BaseUrl"/> is one the client can call.</summary>
    internal bool IsValid() =>
        BaseUrl is { IsAbsoluteUri: true }
        && (BaseUrl.Scheme == Uri.UriSchemeHttps || BaseUrl.Scheme == Uri.UriSchemeHttp);
}
