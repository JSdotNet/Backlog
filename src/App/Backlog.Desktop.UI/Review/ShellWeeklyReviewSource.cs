using Backlog.Desktop.UI.Devbook;
using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Brain.Abstractions;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.UI;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.UI.Goals;
using Backlog.Modules.Progress.UI.WeeklyReview;
using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

namespace Backlog.Desktop.UI.Review;

/// <summary>
/// The window's answer to the weekly review's <see cref="IWeeklyReviewSource"/>
/// (<c>.devbook/domain/progress/features.md#the-weekly-review</c>): the window's Inbox
/// and task list, the Brain's notes, links and review dates, the roadmap's finished
/// project notes and the goals, read in Progress's words; and each line's action,
/// carried out in this window.
/// <para>
/// Every source but the task list is optional, so a host that composes no Brain, no
/// Inbox or no goals still draws the review, and the checklist says which part it left
/// out. One per window, with the window's view state.
/// </para>
/// </summary>
public sealed class ShellWeeklyReviewSource : IWeeklyReviewSource, IDisposable
{
    private readonly TasksDesktopState _tasks;
    private readonly InboxDesktopState? _inbox;
    private readonly IBrainFolderSource? _brain;
    private readonly IRoadmapBrainNotes? _brainNotes;
    private readonly IBrainTaskLinks? _taskLinks;
    private readonly IBrainReviewDates? _reviewDates;
    private readonly IBrainNoteLinkWriter? _linkWriter;
    private readonly IRoadmapProjectNoteArchive? _projectNotes;
    private readonly IGoals? _goals;
    private readonly ISurfaceNavigator? _navigator;
    private readonly TimeProvider _clock;

    /// <summary>The finished plans' project notes of the last reading, so a move to the
    /// archive repoints the plan's Knowledge Refs as the roadmap's own offer does.</summary>
    private IReadOnlyList<FinishedProjectNote> _finished = [];

    public ShellWeeklyReviewSource(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _tasks = (TasksDesktopState)(services.GetService(typeof(TasksDesktopState))
            ?? throw new InvalidOperationException("The weekly review reads the window's task list; register TasksDesktopState."));
        _inbox = services.GetService(typeof(InboxDesktopState)) as InboxDesktopState;
        _brain = services.GetService(typeof(IBrainFolderSource)) as IBrainFolderSource;
        _brainNotes = services.GetService(typeof(IRoadmapBrainNotes)) as IRoadmapBrainNotes;
        _taskLinks = services.GetService(typeof(IBrainTaskLinks)) as IBrainTaskLinks;
        _reviewDates = services.GetService(typeof(IBrainReviewDates)) as IBrainReviewDates;
        _linkWriter = services.GetService(typeof(IBrainNoteLinkWriter)) as IBrainNoteLinkWriter;
        _projectNotes = services.GetService(typeof(IRoadmapProjectNoteArchive)) as IRoadmapProjectNoteArchive;
        _goals = services.GetService(typeof(IGoals)) as IGoals;
        _navigator = services.GetService(typeof(ISurfaceNavigator)) as ISurfaceNavigator;
        _clock = services.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;

        if (_brain is not null) _brain.Changed += OnChanged;
    }

    public event Action? Changed;

    public async Task<WeeklyReviewInputs> ReadAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var planTags = _tasks.RoadmapTags;

        // Every entry, not the header's repository scope: the plan tags are the whole
        // roadmap's, and a plan's next action or an area's recent task in another
        // repository would otherwise read as missing.
        var entries = _tasks.Rows
            .Where(row => row.IsPersisted && !row.IsEmpty)
            .Select(row => Entry(row, planTags))
            .ToList();

        return new WeeklyReviewInputs(
            await InboxAsync(),
            await BrainAsync(today, cancellationToken),
            entries,
            [.. planTags],
            await GoalsAsync(cancellationToken));
    }

    public async Task<Result> ActAsync(WeeklyReviewAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        switch (action.Kind)
        {
            case WeeklyReviewActionKind.OpenInbox:
                return Show(InboxSurfaceRegistration.SurfaceId);
            case WeeklyReviewActionKind.OpenGoals:
                return Show(GoalsSurfaceQuery.SurfaceId);
            case WeeklyReviewActionKind.OpenTask when action.Target is { Length: > 0 } id:
                return Show(TasksSurfaceQuery.SurfaceId, TasksSurfaceQuery.ForEntry(id));
            case WeeklyReviewActionKind.OpenPlan when action.Target is { Length: > 0 } tag:
                return Show(TasksSurfaceQuery.SurfaceId, TasksSurfaceQuery.ForPlan(tag));
            case WeeklyReviewActionKind.OpenNote when action.Target is { Length: > 0 } path:
                return Show(DevbookSurfaceQuery.SurfaceId, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [DevbookSurfaceQuery.Chapter] = path,
                    [DevbookSurfaceQuery.Scope] = DevbookSurfaceQuery.Brain,
                });
            case WeeklyReviewActionKind.MarkNextAction when action.Target is { Length: > 0 } id:
                return await MarkNextActionAsync(id);
            case WeeklyReviewActionKind.ArchiveNote when action.Target is { Length: > 0 } path:
                return await ArchiveAsync(path, cancellationToken);
            default:
                return Result.Failure(Error.Validation("weekly_review.action_incomplete", $"\"{action.Label}\" names nothing to act on."));
        }
    }

    private Result Show(string surfaceId, IReadOnlyDictionary<string, string>? query = null)
    {
        if (_navigator is null)
        {
            return Result.Failure(Error.NotFound("weekly_review.no_navigator", "This window cannot open another surface."));
        }

        _navigator.Show(surfaceId, query);
        return Result.Success();
    }

    /// <summary>Makes the plan's draft step ready, through the window's own list, so the
    /// row on screen and the store agree.</summary>
    private async Task<Result> MarkNextActionAsync(string id)
    {
        var row = _tasks.Rows.FirstOrDefault(candidate => string.Equals(candidate.TaskId, id, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            return Result.Failure(Error.NotFound("weekly_review.entry_missing", "That entry is no longer in the task list."));
        }

        if (row.PreviewStatus != EntryStatus.Draft) return Result.Success();

        await _tasks.ChangeStatusAsync(row, EntryStatus.Ready);
        return Result.Success();
    }

    /// <summary>Moves a note to the Brain's archive area: a finished plan's project note
    /// through the roadmap, which also repoints the plan's Knowledge Refs, any other
    /// through the Brain's link writer.</summary>
    private async Task<Result> ArchiveAsync(string path, CancellationToken cancellationToken)
    {
        var finished = _finished.FirstOrDefault(note => string.Equals(note.NotePath, path, StringComparison.OrdinalIgnoreCase));
        if (finished is not null && _projectNotes is not null)
        {
            var moved = await _projectNotes.ArchiveAsync(finished, cancellationToken);
            return moved.IsSuccess ? Result.Success() : Result.Failure(moved.Error);
        }

        if (_linkWriter is null)
        {
            return Result.Failure(Error.NotFound("weekly_review.brain_unavailable", "No Brain is readable on this device."));
        }

        var write = await _linkWriter.ArchiveAsync(path, cancellationToken);
        return write.Succeeded
            ? Result.Success()
            : Result.Failure(Error.Validation("weekly_review.archive_refused", write.Error ?? $"{path} was not moved."));
    }

    private async Task<IReadOnlyList<ReviewInboxItem>> InboxAsync()
    {
        if (_inbox is null) return [];

        if (!_inbox.Loaded) await _inbox.InitializeAsync();

        return
        [
            .. _inbox.Items
                .Where(item => item.Routing is null && item.ListId is null && item.Status is InboxStatus.Unprocessed or InboxStatus.Triaged)
                .Select(item => new ReviewInboxItem(item.Id, item.Title)),
            .. _inbox.Items
                .Where(item => item.Status == InboxStatus.Deferred)
                .Select(item => new ReviewInboxItem(item.Id, item.Title, Deferred: true, item.DeferredUntil)),
        ];
    }

    private async Task<ReviewBrain?> BrainAsync(DateOnly today, CancellationToken cancellationToken)
    {
        _finished = [];
        if (_brain is null || _brainNotes is null || string.IsNullOrWhiteSpace(_brain.BrainRoot) || !_brain.BrainFolderPresent())
        {
            return null;
        }

        var read = await _brainNotes.ReadAsync(cancellationToken);
        if (read is null) return null;

        var tasksByNote = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);
        if (_taskLinks is not null)
        {
            foreach (var (taskId, notes) in await _taskLinks.NotesByTaskAsync(cancellationToken))
            {
                if (!Guid.TryParse(taskId, out var id)) continue;
                foreach (var note in notes)
                {
                    if (!tasksByNote.TryGetValue(note.NotePath, out var ids)) tasksByNote[note.NotePath] = ids = [];
                    ids.Add(id);
                }
            }
        }

        var notesRead = read
            .Where(note => note.Slug is null)
            .Select(note => new ReviewNote(
                note.Path,
                note.Title,
                note.Roadmap,
                tasksByNote.TryGetValue(note.Path, out var ids) ? ids : []))
            .ToList();

        var due = _reviewDates is null ? BrainReviewsDue.None : await _reviewDates.DueAsync(today, cancellationToken);
        _finished = _projectNotes is null ? [] : await _projectNotes.FinishedAsync(cancellationToken);

        return new ReviewBrain(
            Areas(_brain.Areas()),
            notesRead,
            [.. due.Due.Select(note => new ReviewDueNote(note.NotePath, note.Title, note.Review.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)))],
            [.. due.Unreadable.Select(note => new ReviewDueNote(note.NotePath, note.Title, note.Value))],
            [.. _finished.Select(note => new ReviewNote(note.NotePath, note.NoteTitle))]);
    }

    /// <summary>The areas the review looks in, by the name or the folder the PARA default
    /// gives them, as the Brain's link writer finds the archive.</summary>
    internal static ReviewBrainAreas Areas(IReadOnlyList<BrainArea> areas)
    {
        string? Folder(string name, string folder) => areas.FirstOrDefault(area =>
            string.Equals(area.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(area.Folder, folder, StringComparison.OrdinalIgnoreCase))?.Folder;

        return new ReviewBrainAreas(
            Folder("Inbox", "_inbox"),
            Folder("Projects", "projects"),
            Folder("Someday", "someday"),
            Folder("Areas", "areas"),
            Folder("Archive", "archive") is not null);
    }

    private async Task<IReadOnlyList<ReviewGoal>?> GoalsAsync(CancellationToken cancellationToken)
    {
        if (_goals is null) return null;

        var listed = await _goals.ListAsync(cancellationToken: cancellationToken);
        if (listed.IsFailure) return null;

        return [.. listed.Value.Select(summary => new ReviewGoal(
            summary.Goal.Id,
            summary.Goal.Title,
            summary.Goal.DueDate,
            Math.Max(0, summary.Progress.Total - summary.Progress.Done),
            summary.Progress.Blocked))];
    }

    private ReviewEntry Entry(EntryRow row, IReadOnlyList<string> planTags)
    {
        var status = row.IsPreviewCompleted
            ? ReviewEntryStatus.Finished
            : row.PreviewStatus switch
            {
                EntryStatus.Draft => ReviewEntryStatus.Draft,
                EntryStatus.Ready => ReviewEntryStatus.Ready,
                EntryStatus.InProgress => ReviewEntryStatus.InProgress,
                _ => ReviewEntryStatus.Finished,
            };

        var plan = PlanTagMatch.SlugsCarried(row.PreviewTags)
            .FirstOrDefault(slug => planTags.Any(planned => PlanTagMatch.Carries(planned, slug)));

        DateOnly? created = row.CreatedAt is { } at ? DateOnly.FromDateTime(at.LocalDateTime) : null;
        var activity = new[] { created, row.PreviewStartedOn, row.PreviewCompletedOn }.Max();

        return new ReviewEntry(
            Guid.TryParse(row.TaskId, out var id) ? id : row.Key,
            row.PreviewTitle,
            status,
            plan,
            row.PreviewWaitingFor?.Who,
            row.PreviewWaitingFor?.FollowUpOn,
            row.PreviewDueOn,
            row.IsPreviewBlocked,
            _tasks.IsNotWaiting(row),
            activity);
    }

    private void OnChanged() => Changed?.Invoke();

    public void Dispose()
    {
        if (_brain is not null) _brain.Changed -= OnChanged;
    }
}
