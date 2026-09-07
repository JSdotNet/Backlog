using System.Security.Cryptography;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Api.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Backlog.Modules.Sync.Api.Security;

/// <summary>
/// The service's one security layer (inherited ADR 0012): options, token
/// validation, and the authorization policy, registered together rather than
/// inline beside the endpoints that rely on them.
/// </summary>
public static class SyncAuthenticationExtensions
{
    private const string SigningKeyPath = $"{SyncTokenOptions.SectionName}:SigningKey";

    /// <summary>
    /// Binds <see cref="SyncTokenOptions"/>, wires JWT bearer validation
    /// against it, and registers the fallback-deny authorization policy.
    /// </summary>
    /// <returns>
    /// True when no signing key was configured and a throwaway one was
    /// generated for a development run. The caller logs that after
    /// <c>Build()</c>, because there is no logger yet at this point and a
    /// warning nobody sees is not a warning.
    /// </returns>
    public static bool AddSyncAuthentication(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var generatedDevelopmentKey = EnsureDevelopmentSigningKey(builder);

        builder.Services
            .AddOptions<SyncTokenOptions>()
            .BindConfiguration(SyncTokenOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options => options.HasUsableSigningKey(),
                $"{SigningKeyPath} must be base64 for at least {SyncTokenOptions.MinimumKeyBytes} bytes.")
            .ValidateOnStart();

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configured from the bound options rather than from configuration
        // directly, so the generated development key above reaches the
        // validator by the same route the issuer reads it.
        builder.Services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<SyncTokenOptions>>(ConfigureJwtBearer);

        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(PairedDevicePolicy())
            .AddPolicy(SyncPolicies.PairedDevice, PairedDevicePolicy());

        return generatedDevelopmentKey;
    }

    /// <summary>
    /// A caller has to be authenticated and its token has to name an owner.
    /// The owner claim is part of the policy rather than a check inside an
    /// endpoint, so an endpoint cannot be added that forgets it.
    /// </summary>
    private static AuthorizationPolicy PairedDevicePolicy() =>
        new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .RequireClaim(SyncClaims.OwnerId)
            .Build();

    private static void ConfigureJwtBearer(JwtBearerOptions jwt, IOptions<SyncTokenOptions> sync)
    {
        var settings = sync.Value;

        // The claims are read back under the names they were written with.
        // The default mapping rewrites `sub` to the long WS-Federation URI,
        // which would leave SyncClaims naming something the principal does not
        // carry.
        jwt.MapInboundClaims = false;

        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            // Every one of these, on purpose: inherited ADR 0012 says partial
            // validation is not an option just because the issuer is our own.
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(settings.DecodeSigningKey()),

            // Half a minute covers a clock that drifted; anything longer starts
            // to extend the lifetime the token asked for.
            ClockSkew = TimeSpan.FromSeconds(30),

            // Pinned, so a token naming `alg: none` or a weaker HMAC is not
            // something the validator will even consider.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],

            NameClaimType = SyncClaims.DeviceId,
        };

        // Inherited ADR 0013 asks that authorization failures be audited. What
        // is logged is that a call failed and why the library said so, never
        // the token or any part of it.
        jwt.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Logger(context.HttpContext).LogWarning(
                    "Device token rejected on {Method} {Path}: {Reason}.",
                    context.Request.Method,
                    context.Request.Path,
                    context.Exception.GetType().Name);

                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                Logger(context.HttpContext).LogWarning(
                    "Unauthenticated call to {Method} {Path} challenged: {Error}.",
                    context.Request.Method,
                    context.Request.Path,
                    context.Error ?? "no bearer token");

                return Task.CompletedTask;
            },
        };
    }

    private static ILogger Logger(HttpContext context) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SyncAuthenticationExtensions).FullName!);

    /// <summary>
    /// Lets a development run start without anybody having configured a secret,
    /// by minting one that lives as long as the process. Only in Development:
    /// anywhere else a missing key has to stop the start, because a generated
    /// key would mean every restart silently signs every device out.
    /// </summary>
    private static bool EnsureDevelopmentSigningKey(IHostApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment()
            || !string.IsNullOrWhiteSpace(builder.Configuration[SigningKeyPath]))
        {
            return false;
        }

        builder.Configuration[SigningKeyPath] =
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(SyncTokenOptions.MinimumKeyBytes));

        return true;
    }

    /// <summary>The warning that goes with a generated key, said once at
    /// startup where somebody reading the log will meet it.</summary>
    public static void WarnAboutEphemeralSigningKey(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Logger.LogWarning(
            "No {Key} configured; generated an ephemeral development key. "
            + "Every device token is invalidated when this service restarts.",
            SigningKeyPath);
    }
}
