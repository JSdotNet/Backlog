using Backlog.Modules.Sync.Abstractions;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace Backlog.Mobile.UI.Services;

/// <summary>
/// <see cref="IAttachmentPicker"/> over <c>&lt;InputFile&gt;</c>, for the mobile
/// web harness.
/// <para>
/// A browser only opens its file dialog for an input element in the page, so
/// the page renders <see cref="Components.WebAttachmentInputs"/> — three hidden
/// inputs, one per button — and they report to this service. A pick clicks the
/// matching input from script and waits for its change, or for the browser's
/// <c>cancel</c> when the person backs out. Playwright skips the dialog and
/// sets files on the input directly, which lands in the same place.
/// </para>
/// <para>
/// Registered by the harness only. The Android System WebView's file input
/// works, but the MAUI head has the platform pickers and a real camera, which a
/// web view's <c>capture</c> attribute only approximates.
/// </para>
/// </summary>
public sealed class WebAttachmentPicker(IJSRuntime js) : IAttachmentPicker, IAsyncDisposable
{
    private const string ModulePath = "./_content/Backlog.Mobile.UI/attachments.js";

    private readonly Dictionary<WebAttachmentSource, ElementReference> _inputs = [];

    private IJSObjectReference? _module;
    private DotNetObjectReference<WebAttachmentPicker>? _self;
    private TaskCompletionSource<AttachmentPick>? _pending;

    /// <summary>The accept list of the files input: the allowlist's extensions,
    /// so the dialog offers what the service will take.</summary>
    public const string FilesAccept = "image/*,.pdf,.txt,.md,.csv,.docx,.xlsx,.pptx";

    /// <summary>A browser always has a file input with a camera hint; a desktop
    /// browser just opens the dialog.</summary>
    public ValueTask<bool> CanTakePhotoAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);

    public Task<AttachmentPick> TakePhotoAsync(CancellationToken cancellationToken = default) =>
        OpenAsync(WebAttachmentSource.Camera, cancellationToken);

    public Task<AttachmentPick> PickPicturesAsync(CancellationToken cancellationToken = default) =>
        OpenAsync(WebAttachmentSource.Pictures, cancellationToken);

    public Task<AttachmentPick> PickFilesAsync(CancellationToken cancellationToken = default) =>
        OpenAsync(WebAttachmentSource.Files, cancellationToken);

    /// <summary>An input rendered: it is what <paramref name="source"/> opens.</summary>
    public void Register(WebAttachmentSource source, ElementReference input) => _inputs[source] = input;

    /// <summary>
    /// An input changed. Settles the pick that clicked it — or, when Playwright
    /// set the files with no pick waiting, raises <see cref="Unrequested"/> so
    /// the page takes them all the same.
    /// </summary>
    public void OnChanged(InputFileChangeEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var files = change.GetMultipleFiles(maximumFileCount: 100)
            .Select(file => new PickedFile(
                file.Name,
                file.ContentType,
                file.Size,
                // The cap plus one, so an over-cap file that slipped past the size
                // check fails its copy rather than being silently truncated.
                ct => Task.FromResult(file.OpenReadStream(SyncAttachmentLimits.DefaultMaxBytes + 1, ct))))
            .ToList();

        var pick = AttachmentPick.Of(files);

        if (Interlocked.Exchange(ref _pending, null) is { } pending)
        {
            pending.TrySetResult(pick);
        }
        else
        {
            Unrequested?.Invoke(pick);
        }
    }

    /// <summary>Files arrived with no pick waiting for them.</summary>
    public event Func<AttachmentPick, Task>? Unrequested;

    /// <summary>Called from <c>attachments.js</c> when the dialog closed with
    /// nothing chosen.</summary>
    [JSInvokable]
    public Task OnCancelled()
    {
        Interlocked.Exchange(ref _pending, null)?.TrySetResult(AttachmentPick.None);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _pending, null)?.TrySetResult(AttachmentPick.None);

        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (Exception ex) when (IsBrowserGone(ex))
            {
            }

            _module = null;
        }

        _self?.Dispose();
        _self = null;
    }

    private async Task<AttachmentPick> OpenAsync(WebAttachmentSource source, CancellationToken cancellationToken)
    {
        if (!_inputs.TryGetValue(source, out var input))
        {
            return AttachmentPick.Failed("Choosing files is not available on this screen.");
        }

        var pending = new TaskCompletionSource<AttachmentPick>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _pending, pending)?.TrySetResult(AttachmentPick.None);

        try
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath);
            _self ??= DotNetObjectReference.Create(this);
            await _module.InvokeVoidAsync("open", cancellationToken, input, _self);
        }
        catch (JSException ex)
        {
            Interlocked.Exchange(ref _pending, null);
            return AttachmentPick.Failed($"The file dialog did not open: {ex.Message}");
        }
        catch (Exception ex) when (IsBrowserGone(ex))
        {
            Interlocked.Exchange(ref _pending, null);
            return AttachmentPick.None;
        }

        await using var registration = cancellationToken.Register(() => pending.TrySetResult(AttachmentPick.None));
        return await pending.Task;
    }

    private static bool IsBrowserGone(Exception ex) =>
        ex is JSDisconnectedException or InvalidOperationException or OperationCanceledException or ObjectDisposedException;
}

/// <summary>Which of the three inputs a pick opens.</summary>
public enum WebAttachmentSource
{
    Camera,
    Pictures,
    Files
}
