namespace Backlog.Modules.Sync.Api.Options;

/// <summary>
/// The one owner this service accepts (local ADR 0058 §1). Not a secret: the
/// numeric GitHub user id is public. Bound from <c>Owner</c>, so the deployment
/// sets it as <c>Owner__GitHubUserId</c>.
/// </summary>
public sealed class OwnerOptions
{
    /// <summary>The section this binds to — top level, beside <see cref="GitHubOAuthOptions"/>,
    /// as the GitHub OAuth App's settings already are.</summary>
    public const string SectionName = "Owner";

    /// <summary>The owner's numeric GitHub user id, never the login name. Required
    /// anywhere but Development; absent there, enrolment signs nobody in.</summary>
    public long? GitHubUserId { get; set; }

    /// <summary>An existing owner id the GitHub user's devices join — an owner minted
    /// before sign-in existed, so its data is kept. Absent, the id is derived from
    /// <see cref="GitHubUserId"/>; a deployed service then refuses enrolment and recovery
    /// while devices of another owner are on record, rather than orphan them. Set as
    /// <c>Owner__OwnerId</c>, from azd's <c>SYNC_OWNER_ID</c>.</summary>
    public Guid? OwnerId { get; set; }
}
