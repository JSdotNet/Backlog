namespace Backlog.Modules.Sync.Api.Options;

/// <summary>
/// The "Backlog sync" GitHub OAuth App the owner signs in through (local ADR 0058 §1).
/// <see cref="ClientId"/> is the container app's <c>GitHub__ClientId</c> setting;
/// <see cref="ClientSecret"/> is read from the vault secret
/// <c>github-oauth-client-secret</c> (<c>KeyVaultSecretsConfigurationSource</c>) and
/// only a Development run takes it from configuration.
/// </summary>
public sealed class GitHubOAuthOptions
{
    /// <summary>The section this binds to, the one entries 13 and 15 set.</summary>
    public const string SectionName = "GitHub";

    /// <summary>The OAuth App's client id.</summary>
    public string? ClientId { get; set; }

    /// <summary>The OAuth App's client secret.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Where the browser signs in.</summary>
    public Uri AuthorizeEndpoint { get; set; } = new("https://github.com/login/oauth/authorize");

    /// <summary>Where the service trades the authorization code for a token.</summary>
    public Uri TokenEndpoint { get; set; } = new("https://github.com/login/oauth/access_token");

    /// <summary>The REST API the signed-in user's id is read from.</summary>
    public Uri ApiBaseAddress { get; set; } = new("https://api.github.com/");

    /// <summary>Whether both halves of the OAuth App are configured.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
