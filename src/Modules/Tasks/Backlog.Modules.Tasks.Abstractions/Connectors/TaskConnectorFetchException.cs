namespace Backlog.Modules.Tasks.Abstractions.Connectors;

/// <summary>
/// Why a connector's fetch failed, in the terms a person can act on. Each connector
/// reads its own system's answers — a status, a refused token, a name its settings
/// do not hold — and says which of these it was; the sync never reads an HTTP status
/// itself, because it names no connector.
/// </summary>
public enum TaskConnectorFetchFailure
{
    /// <summary>The source has no repository or product by that name, or will not
    /// say whether it has one to this account.</summary>
    NotFound,

    /// <summary>The source has it, and the account it is reached as may not read
    /// it.</summary>
    NoAccess,

    /// <summary>Backlog's own settings do not name it, so there is no account to
    /// reach it as.</summary>
    NotConfigured,

    /// <summary>Nobody is signed in to the source, or the sign-in stopped being
    /// honoured.</summary>
    SignInRequired,

    /// <summary>The source did not answer. What any failure a connector does not
    /// classify is read as.</summary>
    Unreachable,
}

/// <summary>
/// A fetch that failed for a reason the connector recognised, carrying the sentence
/// the person reads — what went wrong and what to do about it, in the connector's
/// own terms: a repository and the account it is worked as for GitHub, a product
/// slug for spec-manager.
/// <para>
/// Thrown from <see cref="ITaskConnector.FetchAsync"/> in place of the connector's
/// infrastructure exception. The sync then writes nothing and archives nothing, as
/// it does for any failed fetch, and reports <see cref="Exception.Message"/> rather
/// than the general "could not be reached".
/// </para>
/// </summary>
public sealed class TaskConnectorFetchException(TaskConnectorFetchFailure kind, string message, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>Which kind of failure it was.</summary>
    public TaskConnectorFetchFailure Kind { get; } = kind;
}
