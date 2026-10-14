using Backlog.Infrastructure.GitHub;

// The component's published namespace, not the folder's: see the note at the top
// of Settings.razor for why the two must differ.
namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// What the Settings pages share, owned by the <see cref="Settings"/> screen and
/// handed to each page it mounts.
/// <para>
/// The pages were one component until they were split one file per page, and a
/// few facts were read by more than one of them: who this machine reaches GitHub
/// as (the AI page's usage card and the Repositories page), which logins the
/// GitHub CLI holds (the Accounts page and every repository's account select),
/// and that a feature was switched on the Features page (which pages are offered,
/// and the Repositories page's drafts). Those live here, once, so a page that
/// changes one through this class redraws every page that reads it, as it did
/// when they were one.
/// </para>
/// <para>
/// A reference-typed parameter is also what keeps the screen drawing as one: a
/// page given it is redrawn whenever the screen is, the way the markup was when it
/// sat in the screen's own file.
/// </para>
/// </summary>
public sealed class SettingsScreenContext
{
    private readonly GitHubIntegration _gitHub;
    private readonly Action _redraw;
    private readonly Action _featuresChanged;

    internal SettingsScreenContext(GitHubIntegration gitHub, Action redraw, Action featuresChanged)
    {
        _gitHub = gitHub;
        _redraw = redraw;
        _featuresChanged = featuresChanged;
    }

    /// <summary>How this machine reaches GitHub, or null until it has first been
    /// asked. A re-check keeps the previous answer until the new one arrives, so the
    /// AI page's figures and the menu badge do not dip while it runs. The AI page's
    /// usage test replaces it with the answer it got, through
    /// <see cref="ReportConnection"/>.</summary>
    public GitHubConnection? Connection { get; private set; }

    /// <summary>True while <see cref="CheckConnectionAsync"/> is waiting on an answer.</summary>
    public bool IsCheckingConnection { get; private set; }

    /// <summary>An answer about the connection that a page got for itself.</summary>
    public void ReportConnection(GitHubConnection connection)
    {
        Connection = connection;
        _redraw();
    }

    /// <summary>What the <c>gh</c> CLI last said it is signed in to. Empty until
    /// the screen has rendered once, and empty for good on a machine without
    /// <c>gh</c> - which degrades the picker to typing a login rather than
    /// blocking the panel.</summary>
    public IReadOnlyList<GhCliAccount> CliAccounts { get; private set; } = [];

    /// <summary>Raised after a feature was switched on the Features page, once the
    /// screen has settled which pages it offers.</summary>
    public event Action? FeaturesChanged;

    /// <summary>A feature was switched: the screen re-checks the page it is on, and
    /// any page that keeps state derived from the features hears it.</summary>
    public void NotifyFeaturesChanged()
    {
        _featuresChanged();
        FeaturesChanged?.Invoke();
    }

    /// <summary>Asks again how this machine reaches GitHub. The previous answer
    /// stands until the new one arrives; <see cref="IsCheckingConnection"/> says a
    /// check is under way.</summary>
    public async Task CheckConnectionAsync()
    {
        IsCheckingConnection = true;
        _redraw();

        try
        {
            Connection = await _gitHub.DescribeConnectionAsync();
        }
        catch (Exception)
        {
            Connection = new GitHubConnection(false, "Couldn't work out how to reach GitHub from this machine.");
        }
        finally
        {
            IsCheckingConnection = false;
        }

        _redraw();
    }

    /// <summary>
    /// Reads the logins <c>gh</c> is signed in to, once the page is on screen.
    /// <para>
    /// After the first render rather than during it, for the same reason the
    /// connection check is: it is a subprocess, and holding the page up on one
    /// would make Settings open slowly for everybody to serve one panel.
    /// </para>
    /// </summary>
    public async Task LoadCliAccountsAsync()
    {
        try
        {
            CliAccounts = await _gitHub.ListCliAccountsAsync();
        }
        catch (Exception)
        {
            // No gh, or a gh too old to answer. An empty list degrades the picker
            // to manual entry rather than blocking the panel.
            CliAccounts = [];
        }

        _redraw();
    }

    /// <summary>Asks again, so a <c>gh auth login</c> in another window is noticed
    /// without restarting the app. The connection probe is what forgets the CLI's
    /// previous answer, so it is re-asked first.</summary>
    public async Task RefreshCliAccountsAsync()
    {
        await CheckConnectionAsync();
        await LoadCliAccountsAsync();
    }

    /// <summary>
    /// What stands between this account and a call going out as it, or null when
    /// nothing does.
    /// <para>
    /// Only what can be answered here. Whether <c>gh</c> would really hand over a
    /// token is a subprocess away, so the CLI case is judged against the list this
    /// screen already read - and only when that list said something, because an
    /// empty list means "no gh here to ask", not "gh is not signed in as this".
    /// </para>
    /// </summary>
    public string? AccountProblem(GitHubAccount account)
    {
        if (account.Credential is GitHubCredentialKind.PersonalAccessToken && !account.HasToken)
        {
            return $"No personal access token has been pasted for '{account.Login}', so nothing worked as it can reach GitHub.";
        }

        if (account.Credential is GitHubCredentialKind.GhCli
            && CliAccounts.Count > 0
            && !CliAccounts.Any(candidate => GitHubAccount.IsSameLogin(candidate.Login, account.Login)))
        {
            return $"The GitHub CLI on this machine is not signed in as '{account.Login}'. Sign in with `gh auth login`, or give this account a personal access token instead.";
        }

        return null;
    }

    /// <summary>The accounts configured on this machine: listed on the Accounts
    /// page, offered to every repository on the Repositories page, and read by the
    /// AI page's usage card.</summary>
    public IReadOnlyList<GitHubAccount> ConfiguredAccounts => _gitHub.Settings.Current.Accounts;

    /// <summary>The identity one repository's calls actually leave as, worked out
    /// the same way the transport works it out - by asking the settings for the
    /// path, rather than by reading the binding and hoping the two agree.</summary>
    public GitHubAccountChoice RepositoryBinding(GitHubRepositoryRef repository) =>
        _gitHub.Settings.Current.AccountForPath($"repos/{repository.Owner}/{repository.Name}");

    /// <summary>A binding this machine cannot meet, said with both halves somebody
    /// needs: what was being reached for, which the choice carries as its subject,
    /// and which account it was to be reached as.</summary>
    public static string BindingProblem(GitHubAccountChoice binding) => binding.Account is null
        ? $"{binding.Subject} is worked as '{binding.Login}', and this machine has no account for that login. Add it on the Accounts tab, or pick another one here."
        : $"{binding.Subject} is worked as '{binding.Login}', and no personal access token has been pasted for it.";

    /// <summary>The input's classes for a field whose rejection is reported in
    /// the status line beside it rather than by the component.</summary>
    public static string SettingInputClass(string? error) =>
        error is null ? "setting__input" : "setting__input setting__input--error";
}
