namespace Backlog.Modules.Tasks.Abstractions.Connectors;

/// <summary>
/// What a connector a person has to sign in to implements beside
/// <see cref="ITaskConnector"/>, on the same instance.
/// <para>
/// Optional: a connector that reaches its source without a person — a token the
/// host already holds — does not implement it. The settings screen asks each
/// registered connector whether it does and offers Sign in and Sign out by its
/// <see cref="ITaskConnector.Descriptor"/>, so it never names a connector.
/// </para>
/// <para>
/// Credentials never pass through here. How the connector signs in — a browser, a
/// device code — and where it keeps what it was given are its adapter's business;
/// this answers only whether someone is signed in and as whom.
/// </para>
/// </summary>
public interface ITaskConnectorSignIn
{
    /// <summary>Who is signed in, or null when nobody is.</summary>
    TaskConnectorAccount? Account { get; }

    /// <summary>Raised after <see cref="Account"/> changed — a sign-in, a sign-out,
    /// or a sign-in the source stopped honouring. May be raised off the UI
    /// thread.</summary>
    event Action? AccountChanged;

    /// <summary>Signs a person in, interactively where the connector needs to.
    /// Answers null on success and otherwise what went wrong, written for the person
    /// who asked; a cancelled sign-in throws <see cref="OperationCanceledException"/>
    /// rather than answering.</summary>
    Task<string?> SignInAsync(CancellationToken cancellationToken);

    /// <summary>Forgets the signed-in account and what the connector kept for it.
    /// Signing out when nobody is signed in does nothing.</summary>
    Task SignOutAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The account a connector is signed in as.
/// </summary>
/// <param name="DisplayName">What a person reads, such as their name at the
/// source.</param>
/// <param name="SignedInAt">When the sign-in happened.</param>
public sealed record TaskConnectorAccount(string DisplayName, DateTimeOffset SignedInAt);
