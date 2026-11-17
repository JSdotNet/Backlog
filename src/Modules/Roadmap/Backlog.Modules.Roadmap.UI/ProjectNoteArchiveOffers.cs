using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;
using Backlog.Modules.Roadmap.Abstractions.Services;
using Backlog.UI.Components.Feedback;

namespace Backlog.Modules.Roadmap.UI;

/// <summary>
/// Offers, once per plan, to move a finished plan's project note to the Brain's archive
/// (local ADR 0052 §5,
/// <c>.devbook/domain/roadmap/features.md#archiving-a-finished-plans-project-note</c>).
/// <para>
/// It watches the plan and the work it gathers. When a plan becomes finished while the
/// app is open — its last backlog entry done — and it gathers a project note still in
/// the projects area, it raises a toast whose one button is
/// <see cref="ActionLabel"/>. The press moves the note and repoints the plan's Knowledge
/// Ref; dismissing the toast, or letting it time out, writes nothing. Either way the
/// plan is marked offered on this device and never offered again.
/// </para>
/// <para>
/// A plan already finished when the watch starts is not offered: the app cannot tell
/// when it finished, and a launch that raised a toast for every plan finished long ago
/// would bury the one that just did. The weekly review lists those notes
/// (<c>list_brain_reviews</c>), so none is lost.
/// </para>
/// </summary>
public sealed class ProjectNoteArchiveOffers : IDisposable
{
    /// <summary>The words on the toast's button.</summary>
    public const string ActionLabel = "Move the project note to the archive";

    /// <summary>The offer's test id; its button's is this with <c>-action</c> after it.</summary>
    public const string TestId = "project-note-archive-offer";

    /// <summary>How long the offer stays up: the longer dwell, as an error's, because
    /// it asks the person for something rather than only reporting.</summary>
    public const int Duration = ToastMessage.ErrorDuration;

    private readonly IRoadmapProjectNoteArchive _archive;
    private readonly IRoadmapPlanning _planning;
    private readonly IRoadmapWorkChanges? _work;
    private readonly IToastChannel _toasts;
    private readonly HashSet<Guid> _finished = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _started;
    private bool _baselined;
    private bool _disposed;

    public ProjectNoteArchiveOffers(
        IRoadmapProjectNoteArchive archive,
        IRoadmapPlanning planning,
        IToastChannel toasts,
        IRoadmapWorkChanges? work = null)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(planning);
        ArgumentNullException.ThrowIfNull(toasts);
        _archive = archive;
        _planning = planning;
        _toasts = toasts;
        _work = work;
    }

    /// <summary>Reads which plans are finished already, offering none of them, then
    /// listens for the plan and its work to change. A second call does nothing.</summary>
    /// <remarks>It listens first and reads second, so a first read that fails — the
    /// workspace not ready yet — is only retried on the next change, which takes the
    /// starting set then and offers nothing.</remarks>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started || _disposed) return;
        _started = true;

        _planning.Changed += OnChanged;
        if (_work is not null) _work.Changed += OnChanged;

        await CheckAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Offers the move for each plan that has become finished since the last look and
    /// was never offered it on this device — one toast per plan, naming its first
    /// project note when it gathers several. The first look offers nothing: it only
    /// learns which plans are finished already.
    /// </summary>
    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var finished = await _archive.FinishedPlansAsync(cancellationToken).ConfigureAwait(false);

            if (_baselined)
            {
                foreach (var plan in finished.Notes.GroupBy(note => note.ItemId))
                {
                    if (_finished.Contains(plan.Key) || _archive.WasOffered(plan.Key)) continue;

                    _archive.MarkOffered(plan.Key);
                    _toasts.Publish(Offer(plan.First()));
                }
            }

            // Every finished plan, with a project note or not: one that finished while
            // the Brain had no archive area is not new when the area appears. A plan
            // reopened and finished again is new again, but WasOffered keeps it from
            // being offered twice.
            _finished.Clear();
            _finished.UnionWith(finished.Plans);
            _baselined = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The toast that offers <paramref name="note"/>'s move.</summary>
    public ToastMessage Offer(FinishedProjectNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        return new ToastMessage
        {
            Title = $"{note.PlanTitle} is done",
            Message = $"Its project note {note.NoteTitle} is still in the projects area.",
            Severity = ToastSeverity.Info,
            TestId = TestId,
            DurationMilliseconds = Duration,
            ActionLabel = ActionLabel,
            Action = () => ArchiveAsync(note),
        };
    }

    /// <summary>The press: moves the note and reports how that went.</summary>
    public async Task ArchiveAsync(FinishedProjectNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        try
        {
            var archived = await _archive.ArchiveAsync(note).ConfigureAwait(false);
            _toasts.Publish(archived.IsSuccess
                ? ToastMessage.Info($"Moved {note.NoteTitle} to {archived.Value}.", TestId + "-moved")
                : ToastMessage.Error(archived.Error.Message, TestId + "-failed"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The press is a button in a toast: a failure is said in an error toast,
            // never thrown into the page's error boundary.
            _toasts.Publish(ToastMessage.Error($"Could not move {note.NoteTitle}: {exception.Message}", TestId + "-failed"));
        }
    }

    private void OnChanged() => _ = CheckQuietlyAsync();

    // A change arrives from whichever thread raised it, and a failed look is retried on
    // the next one; nothing here may take the publisher down.
    private async Task CheckQuietlyAsync()
    {
        try
        {
            await CheckAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Deliberately swallowed: the offer is a convenience the weekly review backs.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _planning.Changed -= OnChanged;
        if (_work is not null) _work.Changed -= OnChanged;
    }
}
