using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// The account a repository is worked as, and what stands in its way, in the words
/// both settings pages use: the repository card on the Repositories page, and the
/// Accounts page that lists every binding this machine cannot meet. One copy, so
/// the two pages cannot word the same state two ways.
/// <para>
/// The namespace is the project's root rather than a Settings folder's, for the
/// reason <see cref="RepositoriesSettings"/> gives.
/// </para>
/// </summary>
public static class RepositoryAccountBinding
{
    /// <summary>The identity one repository's calls actually leave as, worked out
    /// the same way the transport works it out - by asking the settings for the
    /// path, rather than by reading the binding and hoping the two agree.</summary>
    public static GitHubAccountChoice Of(GitHubSettings settings, GitHubRepositoryRef repository)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(repository);

        return settings.AccountForPath($"repos/{repository.Owner}/{repository.Name}");
    }

    /// <summary>A binding this machine cannot meet, said with both halves somebody
    /// needs: what was being reached for, which the choice carries as its subject,
    /// and which account it was to be reached as.</summary>
    public static string Problem(GitHubAccountChoice binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        return binding.Account is null
            ? $"{binding.Subject} is worked as '{binding.Login}', and this machine has no account for that login. Add it on the Accounts tab, or pick another one here."
            : $"{binding.Subject} is worked as '{binding.Login}', and no personal access token has been pasted for it.";
    }

    public static string Summary(GitHubAccountChoice binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (binding.IsBound) return $"Worked as '{binding.Login}'.";

        return binding.Token is null
            ? "Default - whichever account this machine is signed in as, which is what Backlog has always done."
            : "Default - falling back to this repository's own personal access token.";
    }

    /// <summary>
    /// What stands between this account and a call going out as it, or null when
    /// nothing does.
    /// <para>
    /// Only what can be answered here. Whether <c>gh</c> would really hand over a
    /// token is a subprocess away, so the CLI case is judged against the list the
    /// page already read - and only when that list said something, because an empty
    /// list means "no gh here to ask", not "gh is not signed in as this".
    /// </para>
    /// </summary>
    public static string? AccountProblem(GitHubAccount account, IReadOnlyList<GhCliAccount> cliAccounts)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(cliAccounts);

        if (account.Credential is GitHubCredentialKind.PersonalAccessToken && !account.HasToken)
        {
            return $"No personal access token has been pasted for '{account.Login}', so nothing worked as it can reach GitHub.";
        }

        if (account.Credential is GitHubCredentialKind.GhCli
            && cliAccounts.Count > 0
            && !cliAccounts.Any(candidate => GitHubAccount.IsSameLogin(candidate.Login, account.Login)))
        {
            return $"The GitHub CLI on this machine is not signed in as '{account.Login}'. Sign in with `gh auth login`, or give this account a personal access token instead.";
        }

        return null;
    }
}
