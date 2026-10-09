using System.Globalization;
using Backlog.Desktop.UI.Tasks;
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
/// the link made, or a session with no repository to record it under.
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
        else if (loose.PullRequest is { } pull && projection.PullRequestByKey(pull.Key) is { } open)
        {
            failure = await state.LinkWorkAsync(
                row,
                state.RegisteredRepository(open.RepositoryFullName) ?? open.RepositoryFullName,
                open.Number.ToString(CultureInfo.InvariantCulture),
                EntryProjectionDto.PullRequestTargetType);
        }
        else
        {
            failure = "That pull request is no longer open, so there is nothing to link.";
            toasts?.Publish(ToastMessage.Warning(failure, RefusedTestId));
            return failure;
        }

        if (failure is null)
        {
            var title = string.IsNullOrWhiteSpace(row.PreviewTitle) ? "the task" : row.PreviewTitle;
            toasts?.Publish(ToastMessage.Info($"Linked to {title}.", LinkedTestId));
        }

        return failure;
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
