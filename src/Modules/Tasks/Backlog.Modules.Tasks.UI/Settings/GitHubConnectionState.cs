using Backlog.Infrastructure.GitHub;

namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// How this machine reaches GitHub, as the settings screen last heard it: the
/// connection <see cref="GitHubIntegration.DescribeConnectionAsync"/> describes and
/// the logins the <c>gh</c> CLI holds.
/// <para>
/// One holder shared by the settings screen's pages, because more than one reads the same answer
/// - the Repositories page shows the connection on every card and judges each
/// account against the CLI's logins, while the shell's Accounts and AI pages read
/// both too - and the pages cannot reach each other: the Repositories page is this
/// module's, hosted through a parameterless <c>DynamicComponent</c> that a shell
/// re-render does not reach. Held here, the network call and the subprocess are
/// made once per opening of the screen rather than once per page, and a refresh any page asks for
/// - a repository token stored or forgotten - is what every page then shows.
/// </para>
/// <para>
/// Called from the pages' renderer, so it takes no lock. What it does guard against
/// is re-entry: <see cref="Changed"/> re-renders the pages synchronously, and a page
/// that renders for the first time inside that asks for the connection - so a check
/// is recorded as under way before anything is raised, and the page waits on it
/// rather than starting a second one.
/// </para>
/// <para>
/// The namespace is the project's root rather than a Settings folder's, for the
/// reason <see cref="RepositoriesSettings"/> gives.
/// </para>
/// </summary>
public sealed class GitHubConnectionState(GitHubIntegration gitHub)
{
    /// <summary>Said when the probe itself fails, rather than leaving the pages on
    /// "checking" for good.</summary>
    internal const string UnreachableSummary = "Couldn't work out how to reach GitHub from this machine.";

    private Task? _connectionCheck;
    private Task? _cliAccountsLoad;

    /// <summary>Which check is the latest, so an earlier one that answers late
    /// cannot overwrite the answer to a later one.</summary>
    private int _connectionGeneration;
    private int _cliAccountsGeneration;

    /// <summary>Raised whenever <see cref="Connection"/> or <see cref="CliAccounts"/>
    /// changes, including when a check starts and the connection goes back to
    /// unknown.</summary>
    public event Action? Changed;

    /// <summary>What the last check said, or null while one is under way or none
    /// has been asked for yet.</summary>
    public GitHubConnection? Connection { get; private set; }

    /// <summary>What <c>gh</c> last said it is signed in to. Empty until it has
    /// been asked, and empty for good on a machine without <c>gh</c>.</summary>
    public IReadOnlyList<GhCliAccount> CliAccounts { get; private set; } = [];

    /// <summary>
    /// Lets the next <see cref="EnsureConnectionAsync"/> and
    /// <see cref="EnsureCliAccountsAsync"/> ask afresh. The settings screen calls it
    /// as it opens: the holder lives as long as the window, and every opening of the
    /// screen has always asked again, which is how a <c>gh auth login</c> made since
    /// the last visit gets noticed. What was last heard stays readable until the new
    /// answer replaces it.
    /// </summary>
    public void Forget()
    {
        _connectionCheck = null;
        _cliAccountsLoad = null;
    }

    /// <summary>Asks how this machine reaches GitHub unless that has been asked
    /// already, so the second page to open waits on the first one's answer rather
    /// than asking again.</summary>
    public Task EnsureConnectionAsync() => _connectionCheck ?? RefreshConnectionAsync();

    /// <summary>Asks again - after a credential changed, or when somebody presses
    /// "Check the connection". Never throws: a probe that fails is reported as a
    /// connection that could not be worked out.</summary>
    public Task RefreshConnectionAsync() => StartConnectionCheck().Settled;

    /// <summary>
    /// Asks again for a caller that words a failure itself - the AI page's usage
    /// test, which reports the exception's own message on its line. The answer is
    /// recorded for every page all the same; a failure is recorded as the
    /// unreachable connection and then thrown.
    /// </summary>
    public Task<GitHubConnection> TestConnectionAsync() => StartConnectionCheck().Answer;

    /// <summary>Reads <c>gh</c>'s logins unless that has been done already.</summary>
    public Task EnsureCliAccountsAsync() => _cliAccountsLoad ?? RefreshCliAccountsAsync();

    /// <summary>Reads <c>gh</c>'s logins again, so a <c>gh auth login</c> in
    /// another window is noticed without restarting the app. Never throws.</summary>
    public Task RefreshCliAccountsAsync()
    {
        var loaded = new TaskCompletionSource();
        _cliAccountsLoad = loaded.Task;
        _ = LoadCliAccountsAsync(++_cliAccountsGeneration, loaded);
        return loaded.Task;
    }

    private (Task Settled, Task<GitHubConnection> Answer) StartConnectionCheck()
    {
        // Claimed before DescribeAsync raises anything; see the class remarks.
        var settled = new TaskCompletionSource();
        _connectionCheck = settled.Task;

        var answer = DescribeAsync(++_connectionGeneration);
        _ = SettleAsync(answer, settled);
        return (settled.Task, answer);
    }

    private async Task<GitHubConnection> DescribeAsync(int generation)
    {
        Connection = null;
        Changed?.Invoke();

        GitHubConnection connection;
        try
        {
            connection = await gitHub.DescribeConnectionAsync();
        }
        catch (Exception)
        {
            Record(new GitHubConnection(false, UnreachableSummary));
            throw;
        }

        Record(connection);
        return connection;

        void Record(GitHubConnection answer)
        {
            if (generation != _connectionGeneration) return;

            Connection = answer;
            Changed?.Invoke();
        }
    }

    /// <summary>Completes a check a page only waits on. Its failure is already
    /// recorded as <see cref="UnreachableSummary"/>, which is what the pages show,
    /// so it is not thrown at a page's render a second time.</summary>
    private static async Task SettleAsync(Task check, TaskCompletionSource settled)
    {
        try
        {
            await check;
        }
        catch (Exception)
        {
            // Recorded by DescribeAsync as the unreachable connection.
        }

        settled.TrySetResult();
    }

    private async Task LoadCliAccountsAsync(int generation, TaskCompletionSource loaded)
    {
        IReadOnlyList<GhCliAccount> accounts;
        try
        {
            accounts = await gitHub.ListCliAccountsAsync();
        }
        catch (Exception)
        {
            // No gh, or a gh too old to answer. An empty list judges no account
            // against the CLI and degrades the Accounts picker to typing a login,
            // rather than blocking either page.
            accounts = [];
        }

        if (generation == _cliAccountsGeneration)
        {
            CliAccounts = accounts;
            Changed?.Invoke();
        }

        loaded.TrySetResult();
    }
}
