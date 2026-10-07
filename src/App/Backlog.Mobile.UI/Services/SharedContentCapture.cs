using Backlog.Mobile.UI.Outbox;

using Microsoft.AspNetCore.Components;

namespace Backlog.Mobile.UI.Services;

/// <summary>
/// Turns a share from another app into a capture, the moment it arrives.
/// </summary>
/// <remarks>
/// <para>
/// A share is the person saying "keep this" from somewhere else — a YouTube video,
/// a browser tab — and they are usually on their way back there. Leaving it in
/// the quick-capture field for a Capture press they may never make lost it, so
/// it goes into the device outbox exactly as a pressed Capture does, and the
/// app lands on the Inbox where the new row is.
/// </para>
/// <para>
/// It lives outside any page because a share can arrive on any tab, and only
/// the Inbox was ever listening: on the Note or Tasks tab the payload sat in the
/// receiver's buffer until the person happened to go back. The layout is the
/// one component that is always mounted, so it starts this once per scope.
/// </para>
/// <para>
/// It never touches <see cref="CaptureDraft"/>. Whatever the person was typing
/// before they left to share something is still theirs to finish.
/// </para>
/// </remarks>
public sealed class SharedContentCapture : IDisposable
{
    /// <summary>The Inbox tab's address, relative to the base. The app opens on
    /// Today, so a share lands here rather than on the root.</summary>
    private const string InboxRoute = "inbox";

    private readonly ISharedContentReceiver _receiver;
    private readonly DeviceOutbox _outbox;
    private readonly NavigationManager _navigation;

    private Func<Func<Task>, Task>? _dispatch;
    private IDisposable? _subscription;

    public SharedContentCapture(ISharedContentReceiver receiver, DeviceOutbox outbox, NavigationManager navigation)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(navigation);

        _receiver = receiver;
        _outbox = outbox;
        _navigation = navigation;
    }

    /// <summary>
    /// What became of the last share, in words the Inbox shows; <c>null</c> when
    /// there is nothing to say.
    /// </summary>
    public string? Status { get; private set; }

    /// <summary>Raised on the renderer's dispatcher whenever <see cref="Status"/>
    /// changes.</summary>
    public event Action? Changed;

    /// <summary>
    /// Starts listening for shares, beginning with one that is already waiting.
    /// </summary>
    /// <remarks>
    /// The Android receiver publishes on the platform's main thread and the
    /// harness's on the circuit, so every share is handed to
    /// <paramref name="dispatch"/> — the starting component's <c>InvokeAsync</c> —
    /// before it navigates or changes state. A second call is a no-op: two
    /// subscriptions would put every share in the outbox twice.
    /// </remarks>
    /// <param name="dispatch">Runs work on the renderer's dispatcher.</param>
    public void Start(Func<Func<Task>, Task> dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);

        if (_subscription is not null) return;

        _dispatch = dispatch;
        _subscription = _receiver.Subscribe(OnShared);
    }

    /// <summary>Clears the status line, once the person has moved on to a
    /// capture of their own.</summary>
    public void ClearStatus()
    {
        if (Status is null) return;

        Status = null;
        Changed?.Invoke();
    }

    /// <summary>Stops listening. A share that arrives afterwards waits in the
    /// receiver's buffer for the next <see cref="Start"/>.</summary>
    public void Stop()
    {
        _subscription?.Dispose();
        _subscription = null;
        _dispatch = null;
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    private void OnShared(SharedContent content)
    {
        var dispatch = _dispatch;
        if (dispatch is null) return;

        // Not awaited: the receiver calls back synchronously from the platform
        // thread, which must not wait on the renderer. CaptureAsync reports its
        // own failures as the status, so the task carries nothing to observe.
        _ = dispatch(() => CaptureAsync(content));
    }

    private async Task CaptureAsync(SharedContent content)
    {
        var title = content.Draft;
        if (title.Length == 0) return;

        try
        {
            // The same three calls the Inbox's Capture button makes, so a shared
            // capture is indistinguishable from a typed one once it is queued.
            var capture = CaptureOutboxKind.Create(title);
            await _outbox.EnqueueAsync(CaptureOutboxKind.Token, capture.Id!.Value, CaptureOutboxKind.Write(capture));

            Status = "Captured from another app.";
        }
        catch (Exception ex)
        {
            // The outbox writes to the device before anything else; if that
            // failed, the share is gone unless the person is told, and sharing it
            // again is the way to retry.
            Status = $"Couldn't capture what was shared: {ex.Message}";
        }

        ShowInbox();
        Changed?.Invoke();
    }

    /// <summary>
    /// Lands on the Inbox, where the new row is. The address is replaced rather
    /// than pushed: in the harness the share arrived as <c>?shared=</c>, and an
    /// address left holding it would capture it again on a reload or a Back.
    /// </summary>
    private void ShowInbox()
    {
        if (string.Equals(_navigation.ToBaseRelativePath(_navigation.Uri), InboxRoute, StringComparison.Ordinal)) return;

        _navigation.NavigateTo(InboxRoute, replace: true);
    }
}
