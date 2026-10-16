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
/// <para>
/// With a <see cref="DevbookBranchOverview"/> composed, the branches come from it:
/// the pane loads it once on opening a repository, and the list then holds only
/// the branches that change the devbook, each row carrying what it changes
/// (<see cref="Row"/>). Without one — a host that composed none — the list is
/// fetched on request, as it always was.
/// </para>
/// </summary>
public sealed class DevbookSourceSelection(
    GitHubSettingsStore repositories,
    IGitHubBranchCatalog branches,
    DevbookBranchOverview? overview = null)
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

    /// <inheritdoc cref="DefaultBranchLabel"/>
    public const string RefreshBranchesLabel = "Refresh branches";

    /// <summary>The value of the entry the pane's list shows while the overview
    /// loads. It names no source and asks for nothing; see
    /// <see cref="IsLoadingEntry"/>.</summary>
    public const string LoadingBranchesValue = "@loading";

    /// <inheritdoc cref="DefaultBranchLabel"/>
    public const string LoadingBranchesLabel = "Loading branches…";

    /// <summary>Whether a value is the inert "Loading branches…" entry, which a
    /// pick or Enter leaves exactly where it was.</summary>
    public static bool IsLoadingEntry(string? value) => string.Equals(value, LoadingBranchesValue, StringComparison.Ordinal);

    /// <summary>Branch lists already fetched, per repository alias. Fetched once
    /// and kept for the session: the list changes far more slowly than the panes
    /// that ask for it, and nothing here is a decision anybody makes twice. With
    /// an overview, only the branches typed past it.</summary>
    private readonly Dictionary<string, List<string>> _branches = new(StringComparer.Ordinal);

    /// <summary>How many loads were asked of this selection, per alias, for
    /// <see cref="ListGeneration"/> when no overview is composed.</summary>
    private readonly Dictionary<string, int> _generations = new(StringComparer.Ordinal);

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
    /// What the control offers: the default branch, the branches, the local clone
    /// where there is one, and the entry that fetches the list.
    /// <para>
    /// The branches are those loaded, or — with an overview — those changing the
    /// devbook, then any typed past it. The configured branch is listed even when
    /// nothing has been fetched or it changes nothing, so a pane opened offline
    /// shows the choice somebody made rather than quietly falling back to the
    /// default entry and looking like it was never made.
    /// </para>
    /// <para>
    /// The entry that fetches the list is "Load branches…" until the list is in.
    /// With an overview it is "Refresh branches", which asks again, and it is left
    /// out while a load is running.
    /// </para>
    /// </summary>
    public IReadOnlyList<SelectorOption> Options(string? repositoryAlias)
    {
        if (Repository(repositoryAlias) is not { } repository) return [];

        var options = new List<SelectorOption>
        {
            new(string.Empty, DefaultBranchLabel, "Whatever GitHub calls this repository's default branch")
        };

        var listed = Listed(repository.Alias);

        foreach (var branch in listed)
        {
            options.Add(new SelectorOption(branch, branch, MayChangeHint(repository.Alias, branch), "Branches"));
        }

        if (repository.DevbookBranch is { } stored && !listed.Contains(stored, StringComparer.Ordinal))
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

        if (overview is not null)
        {
            if (!overview.IsLoading(repository.Alias))
            {
                options.Add(new SelectorOption(LoadBranchesValue, RefreshBranchesLabel, "Asks GitHub again which branches change the devbook"));
            }
        }
        // Offered rather than done on render, because listing branches is a
        // network call and opening a pane must never wait on GitHub. It stops
        // being offered once the list is in.
        else if (listed.Count == 0)
        {
            options.Add(new SelectorOption(LoadBranchesValue, LoadBranchesLabel, "Asks GitHub which branches this repository has"));
        }

        return options;
    }

    /// <summary>
    /// What a row of the list shows beside its name: the devbook sections a
    /// branch changes and how far ahead and behind the default branch it is, or
    /// the clone's counts against its upstream. Null for a row with nothing to
    /// show — the default branch, a branch not compared, a clone git could not
    /// place — so a count nobody read is never shown as 0.
    /// </summary>
    /// <param name="label">The row's label, which is what the search field's
    /// suggestions carry as their value.</param>
    public DevbookSourceRow? Row(string? repositoryAlias, string? label)
    {
        if (overview is null || Repository(repositoryAlias) is not { } repository) return null;
        if (overview.Current(repository.Alias) is not { } list) return null;

        if (string.Equals(label, LocalFolderLabel, StringComparison.Ordinal))
        {
            // Only a currency measured against an upstream has counts: a clone
            // tracking nothing, or on no branch, reports 0 and 0 for "no answer".
            return list.Local is
            {
                Currency: LocalGitRepositoryCurrency.UpToDate
                    or LocalGitRepositoryCurrency.Behind
                    or LocalGitRepositoryCurrency.Ahead
                    or LocalGitRepositoryCurrency.Diverged
            } local
                ? new DevbookSourceRow([], local.Ahead, local.Behind, false)
                : null;
        }

        return list.Find(label)?.Change is { } change
            ? new DevbookSourceRow(change.Areas, change.AheadBy, change.BehindBy, change.MayChange)
            : null;
    }

    /// <summary>
    /// What the pane's search field suggests: the entries of <see cref="Options"/>,
    /// in the same order, each carrying its label as its value — the field writes a
    /// picked value into its text, so it has to read as words.
    /// <para>
    /// A non-blank query keeps the entries whose label contains it, ignoring case.
    /// The load entry stays whatever was typed until the list is in, because a
    /// search over branches nobody has fetched has nothing else to offer; once it
    /// is in, the refresh entry is filtered like the rest.
    /// </para>
    /// </summary>
    public IReadOnlyList<SelectorOption> Suggestions(string? repositoryAlias, string? query)
    {
        var filter = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        var listIn = HasBranches(repositoryAlias);

        List<SelectorOption> suggestions =
        [
            .. Options(repositoryAlias)
                .Where(option => filter is null
                    || (IsLoadRequest(option.Value) && !listIn)
                    || option.Label.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .Select(option => option with { Value = option.Label })
        ];

        // Where the refresh entry will be, while the load runs: the pane's sign
        // that the list is still coming. Whatever was typed, since it says
        // something about the list rather than being an entry of it. Not in
        // Options: the settings screen's select would offer it as a choice.
        if (IsLoading(repositoryAlias))
        {
            suggestions.Add(new SelectorOption(LoadingBranchesLabel, LoadingBranchesLabel, "GitHub is being asked which branches change the devbook"));
        }

        return suggestions;
    }

    /// <summary>
    /// The source value that text typed or picked in the search field names, or
    /// null when it names nothing this repository has.
    /// <para>
    /// Blank text names nothing — an emptied field is no answer, and the default
    /// branch has its own words. <see cref="DefaultBranchLabel"/> is the default branch;
    /// <see cref="LocalFolderLabel"/> is the clone where there is one;
    /// <see cref="LoadBranchesLabel"/> — or, with an overview,
    /// <see cref="RefreshBranchesLabel"/> — is the load request while it is offered.
    /// Anything else is a branch: an exact match among the branches known and the
    /// configured one, else the one branch it matches ignoring case, in that
    /// branch's own spelling. Two branches differing only in case match neither.
    /// Known means every branch the overview listed, not only those it suggests:
    /// the filter decides what is suggested, not what can be typed.
    /// </para>
    /// </summary>
    public string? ValueFor(string? repositoryAlias, string? text)
    {
        if (Repository(repositoryAlias) is not { } repository) return null;

        var typed = text?.Trim() ?? string.Empty;
        if (typed.Length == 0) return null;
        if (string.Equals(typed, DefaultBranchLabel, StringComparison.Ordinal)) return string.Empty;

        if (string.Equals(typed, LocalFolderLabel, StringComparison.Ordinal))
        {
            return string.IsNullOrWhiteSpace(repository.CloneDirectory) ? null : LocalFolderValue;
        }

        if (string.Equals(typed, LoadingBranchesLabel, StringComparison.Ordinal))
        {
            return IsLoading(repository.Alias) ? LoadingBranchesValue : null;
        }

        if (string.Equals(typed, overview is null ? LoadBranchesLabel : RefreshBranchesLabel, StringComparison.Ordinal))
        {
            return Options(repository.Alias).Any(option => IsLoadRequest(option.Value)) ? LoadBranchesValue : null;
        }

        var known = Known(repository.Alias);
        if (repository.DevbookBranch is { } stored && !known.Contains(stored, StringComparer.Ordinal)) known.Add(stored);

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

    /// <summary>Whether the branch overview is loading this repository's list
    /// now. Never, without an overview: the fetch on request is awaited where it
    /// is asked for.</summary>
    public bool IsLoading(string? repositoryAlias) =>
        overview is not null && Repository(repositoryAlias) is { } repository && overview.IsLoading(repository.Alias);

    /// <summary>
    /// A number that moves every time a branch list lands for this repository,
    /// refused or not — the overview's load count, or the loads asked of this
    /// selection without one. The settings screen keys its select on it, so the
    /// control is rebuilt after each load and never keeps showing the entry that
    /// asked for it; a refresh changes nothing else that screen could key on.
    /// </summary>
    public int ListGeneration(string? repositoryAlias)
    {
        if (Repository(repositoryAlias) is not { } repository) return 0;

        return overview is null
            ? _generations.GetValueOrDefault(repository.Alias)
            : overview.Generation(repository.Alias);
    }

    /// <summary>Whether the branch list for this repository is already in — with
    /// an overview, whether one was read, even if asking again was refused since.</summary>
    public bool HasBranches(string? repositoryAlias)
    {
        if (Repository(repositoryAlias) is not { } repository) return false;

        return overview is null
            ? Loaded(repository.Alias).Count > 0
            : overview.Current(repository.Alias) is { DefaultBranch: not null };
    }

    /// <summary>
    /// Switches the source. The branch is passed even when the clone was chosen,
    /// so switching away and back remembers which branch was picked.
    /// </summary>
    /// <returns>An error to show, or null.</returns>
    public string? Select(string? repositoryAlias, string? value)
    {
        if (Repository(repositoryAlias) is not { } repository) return null;
        if (IsLoadRequest(value) || IsLoadingEntry(value)) return null;

        var local = string.Equals(value, LocalFolderValue, StringComparison.Ordinal);

        return repositories.SetDevbookSource(repository.Alias, local ? null : value, local);
    }

    /// <summary>
    /// Fetches the branch list — with an overview, loads it again. Contacts the
    /// network, so it runs when somebody asks.
    /// </summary>
    /// <returns>An error to show, or null.</returns>
    public async Task<string?> LoadBranchesAsync(string? repositoryAlias, CancellationToken cancellationToken = default)
    {
        if (Repository(repositoryAlias) is not { } repository) return null;

        if (overview is not null)
        {
            var list = await overview.RefreshAsync(repository.Alias, cancellationToken).ConfigureAwait(false);
            return list.Error;
        }

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
        finally
        {
            _generations[repository.Alias] = _generations.GetValueOrDefault(repository.Alias) + 1;
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

    /// <summary>The branches the list shows: those loaded, or — with an overview
    /// — those changing the devbook, then any typed past it.</summary>
    private List<string> Listed(string alias)
    {
        if (overview is null) return Loaded(alias);

        var listed = overview.Current(alias)?.DevbookBranches.Select(row => row.Branch).ToList() ?? [];
        listed.AddRange(Loaded(alias).Where(branch => !listed.Contains(branch, StringComparer.Ordinal)));
        return listed;
    }

    /// <summary>Every branch this repository is known to have: those loaded, or —
    /// with an overview — the default branch and every branch it listed, then any
    /// typed past it.</summary>
    private List<string> Known(string alias)
    {
        if (overview is null) return [.. Loaded(alias)];

        var known = new List<string>();
        if (overview.Current(alias) is { } list)
        {
            if (list.DefaultBranch is { } defaultBranch) known.Add(defaultBranch);
            known.AddRange(list.Branches.Select(row => row.Branch));
        }

        known.AddRange(Loaded(alias).Where(branch => !known.Contains(branch, StringComparer.Ordinal)));
        return known;
    }

    /// <summary>The hint on a branch whose file list GitHub cut short before any
    /// devbook file showed, so it is listed on a maybe.</summary>
    private string? MayChangeHint(string alias, string branch) =>
        overview?.Current(alias)?.Find(branch)?.Change is { MayChange: true }
            ? "May change the devbook: GitHub listed only part of what it changes"
            : null;
}

/// <summary>What one row of the source list shows beside its name.</summary>
/// <param name="Areas">The devbook sections the branch changes, in tab order;
/// empty for the clone.</param>
/// <param name="AheadBy">Commits ahead of the default branch, or of the clone's
/// upstream.</param>
/// <param name="BehindBy">Commits behind it.</param>
/// <param name="MayChange">Listed because GitHub's file list was cut short, not
/// because a devbook file was seen.</param>
public sealed record DevbookSourceRow(IReadOnlyList<string> Areas, int? AheadBy, int? BehindBy, bool MayChange);
