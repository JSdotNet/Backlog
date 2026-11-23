namespace Backlog.Modules.Sync.Features.Connector;

/// <summary>The lifetimes of the connector's short-lived steps, and the redirect that carries
/// an answer back to claude.ai.</summary>
internal static class ConnectorRedirects
{
    /// <summary>How long a sign-in may stay at GitHub.</summary>
    internal static readonly TimeSpan SignInLifetime = TimeSpan.FromMinutes(10);

    /// <summary>How long the consent page may stay open.</summary>
    internal static readonly TimeSpan ConsentLifetime = TimeSpan.FromMinutes(10);

    /// <summary>How long claude.ai has to exchange the code; it does so at once.</summary>
    internal static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);

    /// <summary>The registered redirect with an answer on it, and the client's state when it sent one.</summary>
    internal static Uri Answer(string redirect, string? clientState, params (string Name, string Value)[] parameters)
    {
        var all = clientState is null ? parameters : [.. parameters, ("state", clientState)];
        return WithQuery(new Uri(redirect), all);
    }

    /// <summary>The registered redirect with an error on it, counted as a refusal.</summary>
    internal static Uri Error(string redirect, string? clientState, string error, string description)
    {
        ConnectorRefusals.Record(error);
        return Answer(redirect, clientState, ("error", error), ("error_description", description));
    }

    /// <summary>The redirect with the parameters appended to whatever query it already has.</summary>
    private static Uri WithQuery(Uri redirect, (string Name, string Value)[] parameters)
    {
        var query = string.Join('&', parameters.Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value)}"));
        var builder = new UriBuilder(redirect);
        builder.Query = string.IsNullOrEmpty(builder.Query) ? query : $"{builder.Query.TrimStart('?')}&{query}";
        return builder.Uri;
    }
}
