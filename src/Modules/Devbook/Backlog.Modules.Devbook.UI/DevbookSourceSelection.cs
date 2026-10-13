using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.UI.Components.Selects;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The "where is this repository's knowledge read from?" control, as one answer
/// two surfaces share.
/// <para>
/// The Devbook pane and the settings screen both offer the choice, and they
/// have to offer the same one: the same options, in the same order, spelling the
/// local folder the same way. Two copies of that would drift the first time a
/// case was added to one of them — a branch that no longer exists, a repository
/// with no clone — and the drift would be silent, because each surface would
/// still look right on its own.
/// </para>
/// <para>
/// It holds the settings store and the branch catalog directly, the way
/// <see cref="DevbookUpdateService"/> holds the git adapter, and for the same
/// reason: a screen legitimately talks to an adapter, and what it hands upward
/// is options and a summary rather than either collaborator.
/// </para>
/// </summary>
public sealed class DevbookSourceSelection(
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

    /// <summary>
    /// The words for the default branch, the local clone, and the load request.
    /// <para>
    /// The pane's search field reads and writes these as text, so typed text is
    /// mapped back through them (see <see cref="ValueFor"/>). Each holds a space,
    /// which git forbids in a branch name, so none of them can collide with a
    /// branch — the same guarantee the <c>@</c> gives <see cref="LocalFolderValue"/>.
    /// </para>
    /// </summary>
    public const string DefaultBranchLabel = "Default branch";

    /// <inheritdoc cref="DefaultBranchLabel"/>
    public const string LocalFolderLabel = "Local clone";

    /// <inheritdoc cref="DefaultBranchLabel"/>
    public const string LoadBranchesLabel = "Load branches…";

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

        return repository.DevbookSource is DevbookSourceKind.LocalFolder
            ? LocalFolderValue
            : repository.DevbookBranch ?? string.Empty;
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
            new(string.Empty, DefaultBranchLabel, "Whatever GitHub calls this repository's default branch")
        };

        var loaded = Loaded(repository.Alias);

        foreach (var branch in loaded)
        {
            options.Add(new SelectorOption(branch, branch, Group: "Branches"));
        }

        if (repository.DevbookBranch is { } stored && !loaded.Contains(stored, StringComparer.Ordinal))
        {
            options.Add(new SelectorOption(stored, stored, "Configured", "Branches"));
        }

        if (!string.IsNullOrWhiteSpace(repository.CloneDirectory))
        {
            options.Add(new SelectorOption(
                LocalFolderValue,
                LocalFolderLabel,
                "The only source you can edit the devbook in"));
        }

        // Offered rather than done on render, because listing branches is a
        // network call and opening a pane must never wait on GitHub. It stops
        // being offered once the list is in.
        if (loaded.Count == 0)
        {
            options.Add(new SelectorOption(LoadBranchesValue, LoadBranchesLabel, "Asks GitHub which branches this repository has"));
        }

        return options;
    }

    /// <summary>
    /// What the pane's search field suggests: the entries of <see cref="Options"/>,
    /// in the same order, each carrying its label as its value — the field writes a
    /// picked value into its text, so it has to read as words.
    /// <para>
    /// A non-blank query keeps the entries whose label contains it, ignoring case.
    /// The load entry stays whatever was typed until the list is in, because a
    /// search over branches nobody has fetched has nothing else to offer.
    /// </para>
    /// </summary>
    public IReadOnlyList<SelectorOption> Suggestions(string? repositoryAlias, string? query)
    {
        var filter = string.IsNullOrWhiteSpace(query) ? null : query.Trim();

        return
        [
            .. Options(repositoryAlias)
                .Where(option => filter is null
                    || IsLoadRequest(option.Value)
                    || option.Label.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .Select(option => option with { Value = option.Label })
        ];
    }

    /// <summary>
    /// The source value that text typed or picked in the search field names, or
    /// null when it names nothing this repository has.
    /// <para>
    /// Blank text names nothing — an emptied field is no answer, and the default
    /// branch has its own words. <see cref="DefaultBranchLabel"/> is the default branch;
    /// <see cref="LocalFolderLabel"/> is the clone where there is one;
    /// <see cref="LoadBranchesLabel"/> is the load request until the list is in.
    /// Anything else is a branch: an exact match among the loaded and configured
    /// branches, else the one branch it matches ignoring case, in that branch's
    /// own spelling. Two branches differing only in case match neither.
    /// </para>
    /// </summary>
    public string? ValueFor(string? repositoryAlias, string? text)
    {
        if (Repository(repositoryAlias) is not { } repository) return null;

        var typed = text?.Trim() ?? string.Empty;
        if (typed.Length == 0) return null;
        if (string.Equals(typed, DefaultBranchLabel, StringComparison.Ordinal)) return string.Empty;

        var loaded = Loaded(repository.Alias);

        if (string.Equals(typed, LocalFolderLabel, StringComparison.Ordinal))
        {
            return string.IsNullOrWhiteSpace(repository.CloneDirectory) ? null : LocalFolderValue;
        }

        if (string.Equals(typed, LoadBranchesLabel, StringComparison.Ordinal))
        {
            return loaded.Count == 0 ? LoadBranchesValue : null;
        }

        List<string> known = repository.DevbookBranch is { } stored && !loaded.Contains(stored, StringComparer.Ordinal)
            ? [.. loaded, stored]
            : loaded;

        if (known.Contains(typed, StringComparer.Ordinal)) return typed;

        var matches = known.Where(branch => string.Equals(branch, typed, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>The sentence under the control, saying what is being read and
    /// whether it can be changed.</summary>
    public string Summary(string? repositoryAlias)
    {
        if (Repository(repositoryAlias) is not { } repository) return string.Empty;

        return repository.DevbookSource is DevbookSourceKind.LocalFolder
            ? "Reading the local clone. Devbook can be edited here."
            : $"Reading {repository.DevbookBranch ?? "the default branch"} as a read-only snapshot. "
              + "Add a local clone directory and select it to make changes.";
    }

    /// <summary>The short label the pane wears beside its tabs — the branch name,
    /// or the word for the clone. No sentence: it sits on a strip, not under a
    /// field.</summary>
    public string Label(string? repositoryAlias)
    {
        if (Repository(repositoryAlias) is not { } repository) return string.Empty;

        return repository.DevbookSource is DevbookSourceKind.LocalFolder
            ? LocalFolderLabel
            : repository.DevbookBranch ?? DefaultBranchLabel;
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

        return repositories.SetDevbookSource(repository.Alias, local ? null : value, local);
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

    /// <summary>
    /// Asks GitHub whether typed text names a branch the loaded list does not
    /// hold, and returns the branch when it does.
    /// <para>
    /// The list is fetched once and stops at a ceiling, so a name missing from it
    /// may be past the ceiling or newer than the fetch. Refusing it on the list's
    /// word alone would turn "not in what I fetched" into "does not exist". So one
    /// call settles it: a head means the branch is there, and it joins the list;
    /// GitHub's not-found means there is no such branch.
    /// </para>
    /// <para>
    /// Separate from <see cref="ValueFor"/>, which stays a pure lookup in what is
    /// already known; this is the one step that contacts the network, and only on
    /// an explicit commit.
    /// </para>
    /// </summary>
    /// <returns>The branch's name, or null when GitHub has no such branch or
    /// could not be asked.</returns>
    public async Task<string?> ResolveTypedBranchAsync(string? repositoryAlias, string? text, CancellationToken cancellationToken = default)
    {
        if (Repository(repositoryAlias) is not { } repository) return null;

        var typed = text?.Trim() ?? string.Empty;

        // A blank, or text holding a space — the words for the default branch, the
        // clone and the load request among them — can be no branch's name.
        if (typed.Length == 0 || typed.Contains(' ', StringComparison.Ordinal)) return null;

        try
        {
            var head = await branches.ResolveHeadAsync(repository, typed, cancellationToken).ConfigureAwait(false);
            if (head is null) return null;

            var loaded = _branches.TryGetValue(repository.Alias, out var list) ? list : _branches[repository.Alias] = [];
            if (!loaded.Contains(head.Branch, StringComparer.Ordinal)) loaded.Add(head.Branch);

            return head.Branch;
        }
        catch (Exception exception) when (exception is GitHubException or GitHubNotConfiguredException)
        {
            return null;
        }
    }

    private List<string> Loaded(string alias) =>
        _branches.TryGetValue(alias, out var loaded) ? loaded : [];
}
