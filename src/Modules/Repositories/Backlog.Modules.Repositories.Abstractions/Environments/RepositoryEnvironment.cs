namespace Backlog.Modules.Repositories.Abstractions.Environments;

/// <summary>
/// Classifies a <see cref="RepositoryEnvironment"/>: the Environment Type of
/// <c>domain/repositories</c>. Stored by name, so the order of the members is free.
/// </summary>
public enum EnvironmentType
{
    Local,
    Development,
    Test,
    Staging,
    Production,
    Cloud,
    Tooling
}

/// <summary>The four shapes a <see cref="LaunchLink"/> takes.</summary>
public enum LaunchLinkKind
{
    /// <summary>An absolute <c>http</c> or <c>https</c> address.</summary>
    Url,

    /// <summary>A command line, run in the repository's clone directory when it has one.</summary>
    Command,

    /// <summary>A folder, relative to the repository's clone directory when it has one.</summary>
    WorkspacePath,

    /// <summary>A dashboard or cloud resource id, such as an Azure resource id.</summary>
    ResourceId
}

/// <summary>
/// How an environment is opened: the Launch Link of <c>domain/repositories</c>. Equal by
/// kind and value.
/// </summary>
public sealed record LaunchLink(LaunchLinkKind Kind, string Value);

/// <summary>
/// A non-secret pointer that helps the person reach an environment — the credential
/// store that holds the secret, a tenant label, a VPN requirement, a local profile. It
/// never contains the credential.
/// </summary>
public sealed record AccessHint(string Text);

/// <summary>
/// A named destination the person wants quick access to — the Environment of
/// <c>domain/repositories</c>, named so here because <see cref="System.Environment"/> and
/// Sessions' own Environment, where an agent session ran, are other things (local ADR
/// 0073 §3).
/// <para>
/// Its name, type and launch link are fixed for its life; only its arrangement —
/// pinned, hidden, and its place among its home's environments — changes. Its place is
/// its position in the list its home holds, so it carries no order of its own.
/// </para>
/// </summary>
public sealed record RepositoryEnvironment(
    Guid Id,
    string Name,
    EnvironmentType Type,
    LaunchLink Link,
    IReadOnlyList<AccessHint> AccessHints,
    bool Pinned = false,
    bool Hidden = false);

/// <summary>
/// Where an environment belongs: one registered repository, by its <c>owner/name</c>, or
/// the registry's one no-repository group.
/// </summary>
public readonly record struct EnvironmentHome
{
    private EnvironmentHome(string? repositoryId) => RepositoryId = repositoryId;

    /// <summary>The repository's <c>owner/name</c>; null for the no-repository group.</summary>
    public string? RepositoryId { get; }

    /// <summary>Whether this is the no-repository group.</summary>
    public bool IsNoRepository => RepositoryId is null;

    /// <summary>The group for the environments no registered repository owns.</summary>
    public static EnvironmentHome NoRepository => default;

    /// <summary>The home of a repository's own environments.</summary>
    /// <exception cref="ArgumentException">The id is blank.</exception>
    public static EnvironmentHome Repository(string repositoryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryId);
        return new EnvironmentHome(repositoryId.Trim());
    }

    /// <summary>Whether this home is <paramref name="other"/>, the repository id compared
    /// without regard to case, the way the registry compares it.</summary>
    public bool Is(EnvironmentHome other) =>
        string.Equals(RepositoryId, other.RepositoryId, StringComparison.OrdinalIgnoreCase);
}

/// <summary>One environment as the store keeps it: the environment and its home.</summary>
public sealed record StoredEnvironment(EnvironmentHome Home, RepositoryEnvironment Environment);
