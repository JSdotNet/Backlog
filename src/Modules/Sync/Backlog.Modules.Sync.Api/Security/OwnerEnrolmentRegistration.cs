using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Backlog.Modules.Sync.Api.Options;
using Backlog.Modules.Sync.Features.Enrolment;
using Backlog.Modules.Sync.Services;
using Microsoft.Extensions.Options;

namespace Backlog.Modules.Sync.Api.Security;

/// <summary>
/// Who may enrol a device (local ADR 0058 §1): the configured owner, signed in through
/// the GitHub OAuth App. Called before <c>AddSyncModule</c>, so the configured owner and
/// the real sign-in win over the module's stand-ins.
/// </summary>
internal static class OwnerEnrolmentRegistration
{
    private const string GitHubUserIdPath = $"{OwnerOptions.SectionName}:GitHubUserId";

    /// <summary>The rate limit the anonymous enrolment routes share: each start keeps a
    /// ticket for minutes, so a flood must not be free.</summary>
    internal const string RateLimitPolicy = "enrolment";

    /// <summary>
    /// Binds and checks <see cref="OwnerOptions"/> and <see cref="GitHubOAuthOptions"/>.
    /// Anywhere but Development the owner and both halves of the OAuth App are required,
    /// so a deployed service cannot start with anonymous owner creation open. In
    /// Development an unset owner leaves enrolment signing nobody in.
    /// </summary>
    internal static void AddOwnerEnrolment(this IHostApplicationBuilder builder)
    {
        var owner = builder.Services.AddOptions<OwnerOptions>().BindConfiguration(OwnerOptions.SectionName);
        var gitHub = builder.Services.AddOptions<GitHubOAuthOptions>().BindConfiguration(GitHubOAuthOptions.SectionName);

        owner.Validate(
            options => options.GitHubUserId is null or > 0,
            $"{GitHubUserIdPath} must be a numeric GitHub user id.");

        if (!builder.Environment.IsDevelopment())
        {
            owner.Validate(
                options => options.GitHubUserId is not null,
                $"{GitHubUserIdPath} must name the owner's numeric GitHub user id; only a Development run enrols without the owner's sign-in.");
        }

        gitHub.Validate<IOptions<OwnerOptions>>(
            (options, configuredOwner) => configuredOwner.Value.GitHubUserId is null || options.IsConfigured,
            $"{GitHubOAuthOptions.SectionName}:ClientId and {GitHubOAuthOptions.SectionName}:ClientSecret must both be set when {GitHubUserIdPath} is.");

        owner.ValidateOnStart();
        gitHub.ValidateOnStart();

        // Deployed, an unpinned owner is derived from the GitHub user, which is a new,
        // empty owner wherever devices were enrolled before; the guard refuses enrolment
        // while another owner's devices are on record, and the warning says so at start.
        var deployed = !builder.Environment.IsDevelopment();

        builder.Services.AddSingleton(sp =>
        {
            var configured = sp.GetRequiredService<IOptions<OwnerOptions>>().Value;
            return new OwnerEnrolment(configured.GitHubUserId, configured.OwnerId, GuardsUnpinnedOwner: deployed);
        });

        if (deployed)
        {
            builder.Services.AddHostedService<UnpinnedOwnerWarning>();
        }

        // One window for the whole service rather than per caller: there is one owner,
        // who signs in a handful of times a year, and the callers are anonymous.
        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddFixedWindowLimiter(RateLimitPolicy, window =>
            {
                window.PermitLimit = 60;
                window.Window = TimeSpan.FromMinutes(1);
                window.QueueLimit = 0;
                window.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });
        });

        builder.Services.AddHttpClient<IGitHubOwnerSignIn, GitHubOwnerSignIn>(client => client.Timeout = TimeSpan.FromSeconds(20));
    }
}
