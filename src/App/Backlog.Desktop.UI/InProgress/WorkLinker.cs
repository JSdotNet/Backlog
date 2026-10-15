using System.Globalization;
using Backlog.Desktop.UI.Tasks;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.UI.Components.Feedback;
using Backlog.UI.Components.Integrations;
using Backlog.UI.Components.Tasks;

namespace Backlog.Desktop.UI.InProgress;

/// <summary>
/// Links one loose session or pull request to one entry, the act behind both "Link
/// to a task…" on the In progress view and "Link a session or pull request…" on a
/// task's side panel — one act, written once.
/// <para>
/// A session is recorded under the repository it is placed in, else under the
/// entry's own; a pull request under the repository it was opened in. Both go
/// through <see cref="TasksDesktopState.LinkWorkAsync"/>, which writes through the
/// use case the Backlog server's <c>link_session</c> and <c>link_change</c> use and
/// says its own refusals. What is said here is the outcome nobody else would say:
/// the link made, or a session or pull request with no repository to record it under.
/// A pull request typed by number or link (<see cref="LinkPullRequestAsync"/>) is
/// linked whatever its state, once it has been read by number.
/// </para>
/// </summary>
public static class WorkLinker
{
    public const string LinkedTestId = "in-progress-linked";
    public const string RefusedTestId = "in-progress-link-refused";

    /// <returns>Null when the link was made; else why not.</returns>
    public static async Task<string?> LinkAsync(
        TasksDesktopState state,
        IToastChannel? toasts,
        InProgressProjection projection,
        EntryRow row,
        InProgressLoose loose)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(loose);

        string? failure;

        if (loose.Session is { } session)
        {
            // Recorded under a repository registered here, as link_session records it:
            // where the session was placed, else what it recorded, else the task's own.
            var recorded = projection.SessionById(session.Id);
            var repository = new[] { recorded?.ResolvedRepository, recorded?.Repository }
                .Concat(row.PreviewRepoIds)
                .Select(state.RegisteredRepository)
                .FirstOrDefault(found => found is not null);

            if (repository is null)
            {
                failure = "Neither this session nor the task names a repository registered in Settings, so there is nothing to record the link under.";
                toasts?.Publish(ToastMessage.Warning(failure, RefusedTestId));
                return failure;
            }

            failure = await state.LinkWorkAsync(row, repository, session.Id, EntryProjectionDto.SessionTargetType);
        }
        else if (loose.PullRequest is { } pull)
        {
            // Under the repository it was opened in. One that merged or closed since the
            // list was read is still the work it was: it is linked whatever its state,
            // as a pull request named by number is.
            var repository = projection.PullRequestByKey(pull.Key)?.RepositoryFullName
                ?? (TryParse(pull.Key, out var keyed, out _) ? keyed : null);
            if (string.IsNullOrWhiteSpace(repository))
            {
                failure = "That pull request names no repository, so there is nothing to record the link under.";
                toasts?.Publish(ToastMessage.Warning(failure, RefusedTestId));
                return failure;
            }

            failure = await state.LinkWorkAsync(
                row,
                state.RegisteredRepository(repository) ?? repository,
                pull.Number.ToString(CultureInfo.InvariantCulture),
                EntryProjectionDto.PullRequestTargetType);
        }
        else
        {
            return "There is nothing to link.";
        }

        if (failure is null)
        {
            var title = string.IsNullOrWhiteSpace(row.PreviewTitle) ? "the task" : row.PreviewTitle;
            toasts?.Publish(ToastMessage.Info($"Linked to {title}.", LinkedTestId));
        }

        return failure;
    }

    /// <summary>
    /// Links the pull request a person named by number or link — "Link a session or pull
    /// request…" on a task's side panel — whatever its state: open, draft, merged or
    /// closed. Work that merged before its task was linked is still the task's work, and
    /// a sprint member counts as done by its pull requests only once they are linked.
    /// <para>
    /// The pull request must be in a repository registered here: a link or
    /// <c>owner/name#123</c> names it, a bare number is the task's own repository's. It
    /// is read by number through <see cref="WorkInProgressReader.ReadPullRequestAsync"/>
    /// before anything is recorded, so a number GitHub does not know is refused rather
    /// than linked, and the link carries the state that read found.
    /// </para>
    /// </summary>
    /// <returns>Null when the link was made; else why not.</returns>
    public static async Task<string?> LinkPullRequestAsync(
        TasksDesktopState state,
        IToastChannel? toasts,
        WorkInProgressReader reader,
        EntryRow row,
        string? reference)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(row);

        var failure = Resolve(state, row, reference, out var named);
        if (failure is null)
        {
            var (pull, refused) = await reader.ReadPullRequestAsync(named!);
            failure = refused
                ?? await state.LinkWorkAsync(
                    row,
                    named!.RepositoryFullName,
                    pull!.Number.ToString(CultureInfo.InvariantCulture),
                    EntryProjectionDto.PullRequestTargetType);

        }

        // A refusal before the write is the caller's to say — the picker draws it under the
        // field the reference was typed into; LinkWorkAsync says its own.

        if (failure is null)
        {
            var title = string.IsNullOrWhiteSpace(row.PreviewTitle) ? "the task" : row.PreviewTitle;
            toasts?.Publish(ToastMessage.Info($"Linked to {title}.", LinkedTestId));
        }

        return failure;
    }

    /// <summary>The registered repository and number a typed reference names; else
    /// why it names none.</summary>
    private static string? Resolve(TasksDesktopState state, EntryRow row, string? reference, out GitHubPullRequestReference? named)
    {
        named = null;

        if (!TryParse(reference, out var repository, out var number))
        {
            return "Type a pull request's number, or paste its link.";
        }

        string? registered;
        if (repository is null)
        {
            var own = row.PreviewRepoIds
                .Select(state.RegisteredRepository)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            switch (own.Count)
            {
                case 0:
                    return "This task names no repository registered in Settings, so paste the pull request's link rather than its number.";
                case > 1:
                    return "This task names more than one repository, so paste the pull request's link, or type it as owner/name#number.";
            }

            registered = own[0];
        }
        else
        {
            registered = state.RegisteredRepository(repository);
            if (registered is null)
            {
                return $"{repository} is not a repository registered in Settings, so there is nothing to record the link under.";
            }
        }

        named = new GitHubPullRequestReference(registered, number);
        return null;
    }

    /// <summary>
    /// A pull request as a person types or pastes it: <c>123</c>, <c>#123</c>,
    /// <c>owner/name#123</c>, or its GitHub link — <c>https://github.com/owner/name/pull/123</c>,
    /// with or without the scheme and with anything after the number, such as
    /// <c>/files</c>. The repository is null for a bare number.
    /// </summary>
    public static bool TryParse(string? reference, out string? repository, out int number)
    {
        repository = null;
        number = 0;

        var text = (reference ?? string.Empty).Trim();
        if (text.Length == 0) return false;

        if (PositiveNumber(text.StartsWith('#') ? text[1..] : text, out number)) return true;

        var hash = text.LastIndexOf('#');
        if (hash > 0 && !text.Contains("://", StringComparison.Ordinal) && !text.Contains("/pull/", StringComparison.OrdinalIgnoreCase))
        {
            var owner = text[..hash].Trim();
            if (owner.Split('/') is not [{ Length: > 0 }, { Length: > 0 }]
                || owner.Any(char.IsWhiteSpace)
                || !PositiveNumber(text[(hash + 1)..], out number))
            {
                number = 0;
                return false;
            }

            repository = owner;
            return true;
        }

        var path = text;
        var scheme = path.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) path = path[(scheme + 3)..];

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts is [var host, var owner2, var name, var pull, var digits, ..]
            && (string.Equals(host, "github.com", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "www.github.com", StringComparison.OrdinalIgnoreCase))
            && string.Equals(pull, "pull", StringComparison.OrdinalIgnoreCase)
            && PositiveNumber(digits.Split('?', '#')[0], out number))
        {
            repository = $"{owner2}/{name}";
            return true;
        }

        number = 0;
        return false;

        static bool PositiveNumber(string candidate, out int value) =>
            int.TryParse(candidate.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
    }

    public const string UnlinkedTestId = "in-progress-unlinked";

    /// <summary>
    /// Takes one session or pull request off an entry — "Unlink" beside a card on the
    /// In progress view and in a task's side panel. Under the repository the link was
    /// recorded with, through <see cref="TasksDesktopState.UnlinkWorkAsync"/>, which
    /// says its own refusals.
    /// </summary>
    /// <returns>Null when the link was taken off; else why not.</returns>
    public static async Task<string?> UnlinkAsync(
        TasksDesktopState state,
        IToastChannel? toasts,
        EntryRow row,
        WorkSession? session,
        WorkPullRequest? pull)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(row);

        string? failure;
        string what;

        if (session is not null
            && row.SessionLinks.FirstOrDefault(link => string.Equals(link.SessionId, session.Id, StringComparison.OrdinalIgnoreCase)) is { } sessionLink)
        {
            what = "The session";
            failure = await state.UnlinkWorkAsync(row, sessionLink.Repository, sessionLink.SessionId, EntryProjectionDto.SessionTargetType);
        }
        else if (pull is not null
            && row.PullRequestLinks.FirstOrDefault(link => string.Equals(
                InProgressProjection.Key(link.Repository, link.Number), pull.Key, StringComparison.OrdinalIgnoreCase)) is { } pullLink)
        {
            what = $"Pull request #{pullLink.Number}";
            failure = await state.UnlinkWorkAsync(
                row,
                pullLink.Repository,
                pullLink.Number.ToString(CultureInfo.InvariantCulture),
                EntryProjectionDto.PullRequestTargetType);
        }
        else
        {
            // Already gone — an agent or another window took it off first. The card
            // redraws from the entry on the next change, so there is nothing to say.
            return null;
        }

        if (failure is null)
        {
            var title = string.IsNullOrWhiteSpace(row.PreviewTitle) ? "the task" : row.PreviewTitle;
            toasts?.Publish(ToastMessage.Info($"{what} is no longer linked to {title}.", UnlinkedTestId));
        }

        return failure;
    }
}
