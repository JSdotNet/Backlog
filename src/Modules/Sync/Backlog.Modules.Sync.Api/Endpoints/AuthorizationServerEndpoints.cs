using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Options;
using Backlog.Modules.Sync.Api.Security;
using Backlog.Modules.Sync.Features.Connector;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Backlog.Modules.Sync.Api.Endpoints;

/// <summary>
/// The remote connector's OAuth 2.1 authorization server (local ADR 0052 §8): RFC 8414
/// metadata, the authorize endpoint, the consent page and the token endpoint, all at the root
/// and all anonymous — the owner's GitHub sign-in, the consent ticket and the client secret
/// are what authorize each step.
/// <para>
/// These are protocol endpoints, so a failure is answered in RFC 6749's shape —
/// <c>{error, error_description}</c> (§5.2) or an error on the redirect (§4.1.2.1) — rather
/// than as RFC 7807 problem details, which a connector client would not read.
/// </para>
/// </summary>
internal static class AuthorizationServerEndpoints
{
    private const string FormContentType = "application/x-www-form-urlencoded";

    /// <summary>The authorize parameters that are refused on the redirect when repeated.
    /// <c>client_id</c> and <c>redirect_uri</c> are not here: repeated, they arrive as null and
    /// are refused in place, because an unverified redirect must never receive anything.</summary>
    private static readonly string[] RedirectedAuthorizeParameters =
        ["response_type", "code_challenge", "code_challenge_method", "scope", "state", "resource"];

    internal static IEndpointRouteBuilder MapAuthorizationServer(this IEndpointRouteBuilder app)
    {
        var server = app.MapGroup(string.Empty)
            .AllowAnonymous()
            .RequireRateLimiting(ConnectorAuthorizationRegistration.RateLimitPolicy)
            .WithTags("Connector");

        server.MapGet(SyncRoutes.AuthorizationServerMetadata, Metadata)
            .WithSummary("The authorization server's RFC 8414 metadata.");

        server.MapGet(SyncRoutes.ConnectorAuthorize, Authorize)
            .WithSummary("Starts the remote connector's authorization: checks the request and sends the owner to GitHub.");

        server.MapGet(SyncRoutes.ConnectorConsent, ShowConsent)
            .WithSummary("The consent page the owner reaches after signing in.");

        server.MapPost(SyncRoutes.ConnectorConsent, DecideConsent)
            .WithSummary("The owner's Allow or Deny, carrying the one-time consent ticket.");

        server.MapPost(SyncRoutes.ConnectorToken, Token)
            .WithSummary("Exchanges an authorization code or a refresh token for the connector's tokens.");

        return app;
    }

    private static IResult Metadata(IOptions<ConnectorOptions> options)
    {
        var issuer = options.Value.Issuer!;

        return Results.Json(new AuthorizationServerMetadataDocument(
            issuer,
            issuer + SyncRoutes.ConnectorAuthorize,
            issuer + SyncRoutes.ConnectorToken,
            ["code"],
            ["authorization_code", "refresh_token"],
            ["S256"],
            ["client_secret_basic", "client_secret_post"],
            [.. ConnectorScopes.Supported]));
    }

    private static async Task<IResult> Authorize(
        HttpContext context,
        ConnectorSettings settings,
        ICommandHandler<StartConnectorAuthorizationCommand, Result<Uri>> handler,
        CancellationToken cancellationToken)
    {
        var query = context.Request.Query;

        var result = await handler.Handle(
            new StartConnectorAuthorizationCommand(
                Single(query["response_type"]),
                Single(query["client_id"]),
                Single(query["redirect_uri"]),
                Single(query["code_challenge"]),
                Single(query["code_challenge_method"]),
                Single(query["scope"]),
                Single(query["state"]),
                Single(query["resource"]),
                RedirectedAuthorizeParameters.FirstOrDefault(name => query[name].Count > 1)),
            cancellationToken);

        return result.IsSuccess
            ? Results.Redirect(result.Value.AbsoluteUri)
            : ConnectorPages.Refusal(context, settings, result.Error.Message);
    }

    private static async Task<IResult> ShowConsent(
        HttpContext context,
        string? ticket,
        ConnectorSettings settings,
        IQueryHandler<DescribeConnectorConsentQuery, Result<ConnectorConsentPrompt>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new DescribeConnectorConsentQuery(ticket), cancellationToken);

        return result.IsSuccess
            ? ConnectorPages.Consent(context, settings, result.Value)
            : ConnectorPages.Refusal(context, settings, result.Error.Message);
    }

    private static async Task<IResult> DecideConsent(
        HttpContext context,
        ConnectorSettings settings,
        ICommandHandler<DecideConnectorConsentCommand, Result<Uri>> handler,
        CancellationToken cancellationToken)
    {
        if (!context.Request.HasFormContentType)
        {
            ConnectorRefusals.Record(OAuthErrors.InvalidRequest);
            return ConnectorPages.Refusal(context, settings, "The consent answer must be a form post.");
        }

        var form = await context.Request.ReadFormAsync(cancellationToken);

        // A repeated ticket or decision is malformed, not a Deny: the ticket stays unspent.
        if (form["ticket"].Count > 1 || form["decision"].Count > 1)
        {
            ConnectorRefusals.Record(OAuthErrors.InvalidRequest);
            return ConnectorPages.Refusal(context, settings, "The consent answer carried a field more than once.");
        }
        var allow = string.Equals(Single(form["decision"]), "allow", StringComparison.Ordinal);

        var result = await handler.Handle(new DecideConnectorConsentCommand(Single(form["ticket"]), allow), cancellationToken);

        return result.IsSuccess
            ? Results.Redirect(result.Value.AbsoluteUri)
            : ConnectorPages.Refusal(context, settings, result.Error.Message);
    }

    private static async Task<IResult> Token(
        HttpContext context,
        ICommandHandler<ExchangeAuthorizationCodeCommand, Result<ConnectorTokenResponse>> exchange,
        ICommandHandler<RefreshConnectorTokenCommand, Result<ConnectorTokenResponse>> refresh,
        CancellationToken cancellationToken)
    {
        NoStore(context.Response);

        if (!context.Request.HasFormContentType)
        {
            return Refused(context, OAuthErrors.InvalidRequest, $"The token request must be {FormContentType}.", usedBasic: false);
        }

        var form = await context.Request.ReadFormAsync(cancellationToken);
        var basic = ReadBasic(context.Request);

        // RFC 6749 §3.2: request parameters must not be included more than once.
        if (form.FirstOrDefault(field => field.Value.Count > 1) is { Key: { } repeated })
        {
            return Refused(context, OAuthErrors.InvalidRequest, $"The {repeated} parameter was sent more than once.", usedBasic: basic is not null);
        }

        var formSecret = Single(form["client_secret"]);

        if (basic is not null && formSecret is not null)
        {
            return Refused(context, OAuthErrors.InvalidRequest, "Authenticate the client one way only.", usedBasic: true);
        }

        var credentials = basic ?? new ConnectorClientCredentials(Single(form["client_id"]), formSecret);

        Result<ConnectorTokenResponse> result = Single(form["grant_type"]) switch
        {
            "authorization_code" => await exchange.Handle(
                new ExchangeAuthorizationCodeCommand(credentials, Single(form["code"]), Single(form["redirect_uri"]), Single(form["code_verifier"])),
                cancellationToken),
            "refresh_token" => await refresh.Handle(
                new RefreshConnectorTokenCommand(credentials, Single(form["refresh_token"]), Single(form["scope"])),
                cancellationToken),
            _ => Unsupported(),
        };

        return result.IsSuccess
            ? Results.Json(result.Value)
            : OAuthError(context, result.Error.Code, result.Error.Message, usedBasic: basic is not null);
    }

    /// <summary>A refusal the endpoint makes itself, before any handler: counted, then answered.</summary>
    private static IResult Refused(HttpContext context, string error, string description, bool usedBasic)
    {
        ConnectorRefusals.Record(error);
        return OAuthError(context, error, description, usedBasic);
    }

    private static Error Unsupported()
    {
        ConnectorRefusals.Record(OAuthErrors.UnsupportedGrantType);
        return Error.Validation(OAuthErrors.UnsupportedGrantType, "Only authorization_code and refresh_token are supported.");
    }

    /// <summary>
    /// An RFC 6749 §5.2 error body. <c>invalid_client</c> is a 401, with a Basic challenge
    /// when the client tried Basic (§5.2 requires it then); every other error is a 400.
    /// </summary>
    private static IResult OAuthError(HttpContext context, string error, string description, bool usedBasic)
    {
        var status = StatusCodes.Status400BadRequest;

        if (error == OAuthErrors.InvalidClient)
        {
            status = StatusCodes.Status401Unauthorized;
            if (usedBasic)
            {
                context.Response.Headers.WWWAuthenticate = "Basic realm=\"backlog-sync\"";
            }
        }

        return Results.Json(new OAuthErrorBody(error, description), statusCode: status);
    }

    /// <summary>Client credentials from a Basic header, each half form-urlencoded per RFC 6749
    /// §2.3.1; null when there is no usable Basic header.</summary>
    private static ConnectorClientCredentials? ReadBasic(HttpRequest request)
    {
        if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var header)
            || !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(header.Parameter))
        {
            return null;
        }

        Span<byte> buffer = new byte[header.Parameter.Length];
        if (!Convert.TryFromBase64String(header.Parameter, buffer, out var written))
        {
            return new ConnectorClientCredentials(null, null);
        }

        var pair = Encoding.UTF8.GetString(buffer[..written]);
        var colon = pair.IndexOf(':', StringComparison.Ordinal);

        return colon < 0
            ? new ConnectorClientCredentials(null, null)
            : new ConnectorClientCredentials(
                Uri.UnescapeDataString(pair[..colon].Replace('+', ' ')),
                Uri.UnescapeDataString(pair[(colon + 1)..].Replace('+', ' ')));
    }

    internal static void NoStore(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.Pragma = "no-cache";
    }

    /// <summary>One value of a parameter, or null when it is absent or repeated — a repeated
    /// OAuth parameter is malformed (RFC 6749 §3.1).</summary>
    private static string? Single(Microsoft.Extensions.Primitives.StringValues values) =>
        values.Count == 1 ? values[0] : null;

    private sealed record OAuthErrorBody(
        [property: JsonPropertyName("error")] string Error,
        [property: JsonPropertyName("error_description")] string ErrorDescription);

    private sealed record AuthorizationServerMetadataDocument(
        [property: JsonPropertyName("issuer")] string Issuer,
        [property: JsonPropertyName("authorization_endpoint")] string AuthorizationEndpoint,
        [property: JsonPropertyName("token_endpoint")] string TokenEndpoint,
        [property: JsonPropertyName("response_types_supported")] string[] ResponseTypesSupported,
        [property: JsonPropertyName("grant_types_supported")] string[] GrantTypesSupported,
        [property: JsonPropertyName("code_challenge_methods_supported")] string[] CodeChallengeMethodsSupported,
        [property: JsonPropertyName("token_endpoint_auth_methods_supported")] string[] TokenEndpointAuthMethodsSupported,
        [property: JsonPropertyName("scopes_supported")] string[] ScopesSupported);
}

/// <summary>
/// The two pages the authorization server renders itself: the consent page and a refusal.
/// Server-rendered, every value HTML-encoded, no external asset, never framed, never cached,
/// and no referrer — the consent ticket is in the address.
/// </summary>
internal static class ConnectorPages
{
    internal static IResult Consent(HttpContext context, ConnectorSettings settings, ConnectorConsentPrompt prompt)
    {
        var html = HtmlEncoder.Default;
        var scopes = string.Concat(prompt.Scopes.Select(s =>
            $"<li><strong>{html.Encode(s.Description)}</strong> <code>{html.Encode(s.Scope)}</code></li>"));
        var days = (int)prompt.RefreshTokenLifetime.TotalDays;

        var body = $"""
            <h1>Connect {html.Encode(prompt.ClientName)} to Backlog?</h1>
            <p>{html.Encode(prompt.ClientName)} asks to:</p>
            <ul>{scopes}</ul>
            <p>It stays connected for {days} days after it was last used, until you revoke it in Backlog's MCP server settings.</p>
            <form method="post" action="{SyncRoutes.ConnectorConsent}">
              <input type="hidden" name="ticket" value="{html.Encode(prompt.Ticket)}">
              <button type="submit" name="decision" value="allow">Allow</button>
              <button type="submit" name="decision" value="deny">Deny</button>
            </form>
            """;

        return Page(context, settings, StatusCodes.Status200OK, $"Connect {prompt.ClientName}", body);
    }

    internal static IResult Refusal(HttpContext context, ConnectorSettings settings, string message)
    {
        var body = $"""
            <h1>Backlog cannot connect this client</h1>
            <p>{HtmlEncoder.Default.Encode(message)}</p>
            """;

        return Page(context, settings, StatusCodes.Status400BadRequest, "Connection refused", body);
    }

    private static IResult Page(HttpContext context, ConnectorSettings settings, int status, string title, string body)
    {
        var headers = context.Response.Headers;
        AuthorizationServerEndpoints.NoStore(context.Response);

        // form-action names the redirect's origin too: a browser holds the form's redirect to
        // claude.ai to the same directive.
        var redirectOrigin = new Uri(settings.RedirectUri).GetLeftPart(UriPartial.Authority);
        headers.ContentSecurityPolicy =
            $"default-src 'none'; style-src 'unsafe-inline'; form-action 'self' {redirectOrigin}; frame-ancestors 'none'; base-uri 'none'";
        headers.XFrameOptions = "DENY";
        headers[HeaderNames.XContentTypeOptions] = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";

        var page = $"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{HtmlEncoder.Default.Encode(title)}</title>
            <style>body{"{"}font-family:system-ui,sans-serif;max-width:32rem;margin:3rem auto;padding:0 1rem;line-height:1.5{"}"}button{"{"}font:inherit;padding:.5rem 1.25rem;margin-right:.5rem{"}"}</style>
            </head>
            <body>
            {body}
            </body>
            </html>
            """;

        return Results.Content(page, "text/html", Encoding.UTF8, status);
    }
}
