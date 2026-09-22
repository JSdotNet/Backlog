using System.Text.Json.Serialization;

namespace Backlog.Infrastructure.Claude;

/// <summary>
/// One Claude organization Backlog can read usage from: an endpoint, the Admin API
/// key that opens it, which actor in it is you, and optionally a workspace to
/// narrow reports to.
/// <para>
/// An account rather than a flat set of fields because a person can belong to
/// more than one organization — a personal Console and an employer's — and each
/// issues its own admin key against its own endpoint. Spend is read from every
/// configured account and added up; nothing here ranks one above another.
/// </para>
/// <para>
/// <see cref="Id"/> is the identity, not the actor or the endpoint: two accounts
/// may legitimately share either (the same email in two organizations, two
/// organizations on <c>api.anthropic.com</c>), and an id that changed when a field
/// was edited would be an id the Settings page could not keep a card open on.
/// </para>
/// </summary>
public sealed record ClaudeAccount
{
    /// <summary>A stable, opaque id. Assigned when the account is created and never
    /// shown; it exists so a card can be addressed while every visible field on it
    /// is still blank.</summary>
    public string Id { get; init; } = NewId();

    /// <summary>An optional human label — "personal", "work" — for telling two
    /// accounts apart on the Settings page. Not sent anywhere.</summary>
    public string? DisplayName { get; init; }

    /// <summary>The Admin API key (<c>sk-ant-admin…</c>). A regular inference key
    /// cannot read usage reports, however valid it is.</summary>
    public string? AdminApiKey { get; init; }

    /// <summary>Optional workspace filter, so a single workspace's usage can be
    /// reported instead of the whole organization.</summary>
    public string? WorkspaceId { get; init; }

    /// <summary>
    /// Which actor in the organization is you — the account Anthropic attributes
    /// Claude Code activity to, usually an email address.
    /// <para>
    /// Needed because the Claude Code report is organization-wide and lists every
    /// actor, while the dashboard is a personal view. There is no endpoint that
    /// says who an Admin API key belongs to — an admin key belongs to the
    /// organization, not to a person — so this is the one fact about the
    /// credential that cannot be discovered from it.
    /// </para>
    /// <para>
    /// Null narrows to nothing rather than to everybody. A dashboard that silently
    /// showed the whole organization's spend under the heading "your usage" would
    /// be worse than one that asks for a name.
    /// </para>
    /// </summary>
    public string? Actor { get; init; }

    public string ApiVersion { get; init; } = ClaudeSettingsStore.DefaultApiVersion;

    public string ApiEndpoint { get; init; } = ClaudeSettingsStore.DefaultApiEndpoint;

    /// <summary>
    /// True when a key is present. Anthropic only issues admin keys to
    /// organizations, so a configured key is also the practical signal that an
    /// organization exists behind it.
    /// </summary>
    [JsonIgnore]
    public bool IsConfigured => !string.IsNullOrWhiteSpace(AdminApiKey);

    /// <summary>True when nothing has been typed into the account: the state a fresh
    /// one is created in, and the one forgetting the last account returns it to.</summary>
    [JsonIgnore]
    public bool IsBlank =>
        string.IsNullOrWhiteSpace(DisplayName)
        && string.IsNullOrWhiteSpace(AdminApiKey)
        && string.IsNullOrWhiteSpace(WorkspaceId)
        && string.IsNullOrWhiteSpace(Actor)
        && string.Equals(ApiEndpoint, ClaudeSettingsStore.DefaultApiEndpoint, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the account can answer the personal question the dashboard
    /// asks: it has a key to read the organization with and an actor to narrow it
    /// to.</summary>
    [JsonIgnore]
    public bool CanReportSpend => IsConfigured && !string.IsNullOrWhiteSpace(Actor);

    /// <summary>
    /// True when the key looks like an Admin API key (<c>sk-ant-admin…</c>).
    /// <para>
    /// A hint, not a gate. Anthropic accepts three credentials on the usage reports —
    /// an admin key, an <c>org:admin</c> OAuth token, or a personal or service account
    /// key that isn't scoped to a workspace — and a workspace-scoped key, which is
    /// refused, reads exactly like the personal one that isn't. So a false here means
    /// "worth checking", not "won't work", and Settings says so rather than refusing to
    /// send it.
    /// </para>
    /// </summary>
    [JsonIgnore]
    public bool LooksLikeAdminKey =>
        !string.IsNullOrWhiteSpace(AdminApiKey)
        && AdminApiKey.StartsWith("sk-ant-admin", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What a surface shows for this account. The label when somebody gave one,
    /// otherwise the actor, otherwise the endpoint's host — the most specific thing
    /// that has been typed so far — and "Claude account" while nothing has.
    /// </summary>
    [JsonIgnore]
    public string Label
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DisplayName)) return DisplayName.Trim();
            if (!string.IsNullOrWhiteSpace(Actor)) return Actor.Trim();

            if (Uri.TryCreate(ApiEndpoint, UriKind.Absolute, out var endpoint)
                && !string.Equals(ApiEndpoint, ClaudeSettingsStore.DefaultApiEndpoint, StringComparison.OrdinalIgnoreCase))
            {
                return endpoint.Host;
            }

            return "Claude account";
        }
    }

    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>Two ids that name the same account. Ordinal: ids are generated, never
    /// typed.</summary>
    public static bool IsSameId(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal);
}
