using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Knowledge.Abstractions;
using Backlog.UI.Components.Selects;

namespace Backlog.Desktop.UI.Knowledge;

/// <summary>
/// The "where is this repository's knowledge read from?" control, as one answer
/// two surfaces share.
/// <para>
/// The knowledge pane and the settings screen both offer the choice, and they
/// have to offer the same one: the same options, in the same order, spelling the
/// local folder the same way. Two copies of that would drift the first time a
/// case was added to one of them — a branch that no longer exists, a repository
/// with no clone — and the drift would be silent, because each surface would
/// still look right on its own.
/// </para>
/// <para>
/// It holds the settings store and the branch catalog directly, the way
/// <see cref="KnowledgeUpdateService"/> holds the git adapter, and for the same
/// reason: a screen legitimately talks to an adapter, and what it hands upward
/// is options and a summary rather than either collaborator.
/// </para>
/// </summary>
public sealed class KnowledgeSourceSelection(
    GitHubSettingsStore repositories,
    IGitHubBranchCatalog branches)
{
    /// <summary>
    /// The value standing for the local clone.
    /// <para>
    /// Spelled with a character git forbids in a branch name, so it can never
    /// collide with a branch somebody actually called "local".
    /// </para>
    /// </summary>
    public const string LocalFolderValue = "@local";

    /// <summary>The value standing for "go and fetch the branch list". Picking it
    /// is a request, not a source — see <see cref="IsLoadRequest"/>.</summary>
    public const string LoadBranchesValue = "@load";

    /// <summary>Branch lists already fetched, per repository alias. Fetched once
    /// and kept for the session: the list changes far more slowly than the panes
    /// that ask for it, and nothing here is a decision anybody makes twice.</summary>
    private readonly Dictionary<string, List<string>> _branches = new(StringComparer.Ordinal);

    /// <summary>Whether this scope has a source to choose at all. The storage
    /// folder has none — it is nobody's repository and nobody's branch — so the
    /// control is left out rather than shown offering one option.</summary>
    public bool CanChoose(string? repositoryAlias) => Repository(repositoryAlias) is not null;

    public GitHubRepositoryRef? Repository(string? repositoryAlias) =>
        string.IsNullOrWhiteSpace(repositoryAlias) ? null : repositories.Current.Find(repositoryAlias);

    /// <summary>Whether a chosen value was the request to fetch the branch list
    /// rather than a source to switch to.</summary>
    public static bool IsLoadRequest(string? value) => string.Equals(value, LoadBranchesValue, StringComparison.Ordinal);

    public string CurrentValue(string? repositoryAlias)
    {
        if (Repository(repositoryAlias) is not { } repository) return string.Empty;

        return repository.KnowledgeSource is KnowledgeSourceKind.LocalFolder
            ? LocalFolderValue
            : repository.KnowledgeBranch ?? string.Empty;
    }

    /// <summary>
    /// What the control offers: the default branch, whichever branches have been
    /// loaded, the local clone where there is one, and — until the list has been
    /// fetched — the entry that fetches it.
    /// <para>
    /// The configured branch is listed even when nothing has been fetched, so a
    /// pane opened offline shows the choice somebody made rather than quietly
    /// falling back to the default entry and looking like it was never made.
    /// </para>
    /// </summary>
    public IReadOnlyList<SelectorOption> Options(string? repositoryAlias)
    {
        if (Repository(repositoryAlias) is not { } repository) return [];

        var options = new List<SelectorOption>
        {
            new(string.Empty, "Default branch", "Whatever GitHub calls this repository's default branch")
        };

        var loaded = Loaded(repository.Alias);

        foreach (var branch in loaded)
        {
            options.Add(new SelectorOption(branch, branch, Group: "Branches"));
        }

        if (repository.KnowledgeBranch is { } stored && !loaded.Contains(stored, StringComparer.Ordinal))
        {
            options.Add(new SelectorOption(stored, stored, "Configured", "Branches"));
        }

        if (!string.IsNullOrWhiteSpace(repository.CloneDirectory))
        {
            options.Add(new SelectorOption(
                LocalFolderValue,
                "Local clone",
                "The only source you can edit knowledge in"));
        }

        // Offered rather than done on render, because listing branches is a
        // network call and opening a pane must never wait on GitHub. It stops
        // being offered once the list is in.
        if (loaded.Count == 0)
        {
            options.Add(new SelectorOption(LoadBranchesValue, "Load branches…", "Asks GitHub which branches this repository has"));
        }

        return options;
    }

    /// <summary>The sentence under the control, saying what is being read and
    /// whether it can be changed.</summary>
    public string Summary(string? repositoryAlias)
    {
        if (Repository(repositoryAlias) is not { } repository) return string.Empty;

        return repository.KnowledgeSource is KnowledgeSourceKind.LocalFolder
            ? "Reading the local clone. Knowledge can be edited here."
            : $"Reading {repository.KnowledgeBranch ?? "the default branch"} as a read-only snapshot. "
              + "Add a local clone directory and select it to make changes.";
    }

    /// <summary>The short label the pane wears beside its tabs — the branch name,
    /// or the word for the clone. No sentence: it sits on a strip, not under a
    /// field.</summary>
    public string Label(string? repositoryAlias)
    {
        if (Repository(repositoryAlias) is not { } repository) return string.Empty;

        return repository.KnowledgeSource is KnowledgeSourceKind.LocalFolder
            ? "Local clone"
            : repository.KnowledgeBranch ?? "Default branch";
    }

    /// <summary>Whether the branch list for this repository is already in.</summary>
    public bool HasBranches(string? repositoryAlias) =>
        Repository(repositoryAlias) is { } repository && Loaded(repository.Alias).Count > 0;

    /// <summary>
    /// Switches the source. The branch is passed even when the clone was chosen,
    /// so switching away and back remembers which branch was picked.
    /// </summary>
    /// <returns>An error to show, or null.</returns>
    public string? Select(string? repositoryAlias, string? value)
    {
        if (Repository(repositoryAlias) is not { } repository) return null;
        if (IsLoadRequest(value)) return null;

        var local = string.Equals(value, LocalFolderValue, StringComparison.Ordinal);

        return repositories.SetKnowledgeSource(repository.Alias, local ? null : value, local);
    }

    /// <summary>
    /// Fetches the branch list. Contacts the network, so it runs when somebody
    /// asks and never on its own.
    /// </summary>
    /// <returns>An error to show, or null.</returns>
    public async Task<string?> LoadBranchesAsync(string? repositoryAlias, CancellationToken cancellationToken = default)
    {
        if (Repository(repositoryAlias) is not { } repository) return null;

        try
        {
            var listed = await branches.ListBranchesAsync(repository, cancellationToken).ConfigureAwait(false);
            _branches[repository.Alias] = [.. listed];

            return listed.Count == 0 ? $"{repository.FullName} reported no branches." : null;
        }
        catch (Exception exception) when (exception is GitHubException or GitHubNotConfiguredException)
        {
            return exception.Message;
        }
    }

    private List<string> Loaded(string alias) =>
        _branches.TryGetValue(alias, out var loaded) ? loaded : [];
}
