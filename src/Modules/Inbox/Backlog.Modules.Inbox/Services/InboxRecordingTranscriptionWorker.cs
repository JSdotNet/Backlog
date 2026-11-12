using System.Threading.Channels;
using Backlog.Modules.Inbox.Features.TranscribeRecordings;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Modules.Inbox.Services;

/// <summary>Asks for a note's recordings to be transcribed in the background. The note
/// pull calls it once a note's files have come down.</summary>
public interface IInboxRecordingQueue
{
    void Request(Guid noteId);
}

/// <summary>
/// The background step of local ADR 0049 §1: one note at a time, off the sync loop, so an
/// hour of audio never holds up a pull. Nobody presses a button — a note pull that brought
/// a recording down requests its note, and on start every open note with a recording is
/// requested once, which is the retry for a transcription that failed last time.
/// <para>
/// A singleton the host starts, like the sync workers; a host that never starts it still
/// accepts requests, and they wait for a start that does not come.
/// </para>
/// </summary>
public sealed partial class InboxRecordingTranscriptionWorker(
    IServiceProvider services,
    ILogger<InboxRecordingTranscriptionWorker>? logger = null) : IInboxRecordingQueue, IDisposable
{
    private readonly ILogger _logger = logger ?? NullLogger<InboxRecordingTranscriptionWorker>.Instance;

    private readonly Channel<Guid> _requests = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private Task? _loop;
    private bool _disposed;

    /// <summary>The loop, for a test to wait on; null before <see cref="Start"/>.</summary>
    internal Task? Loop => _loop;

    public void Request(Guid noteId) => _requests.Writer.TryWrite(noteId);

    /// <summary>Starts the loop once; later calls do nothing.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _loop ??= Task.Run(() => RunAsync(_stop.Token));
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RequestOpenNotesAsync(cancellationToken).ConfigureAwait(false);

            await foreach (var noteId in _requests.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await HandleAsync(noteId, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task RequestOpenNotesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var notes = await services.GetRequiredService<IInboxItemRepository>().ListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var note in notes.Where(item => item.IsNote && item.Attachments.Any(attachment => attachment.IsRecording)))
            {
                Request(note.Id);
            }
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            LogScanFailed(_logger, failure);
        }
    }

    private async Task HandleAsync(Guid noteId, CancellationToken cancellationToken)
    {
        try
        {
            var handler = services.GetRequiredService<ICommandHandler<TranscribeRecordingsCommand, Result<int>>>();
            await handler.Handle(new TranscribeRecordingsCommand(noteId), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
        {
            // The loop outlives any one note, a timeout included: a store that threw is tried again on the next
            // request for that note, or the next start.
            LogNoteFailed(_logger, failure, noteId);
        }
    }

    /// <summary>Idempotent: the container holds this worker under two services and disposes
    /// it once for each.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _requests.Writer.TryComplete();
        _stop.Cancel();
        _stop.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not list the notes with recordings to transcribe on start.")]
    private static partial void LogScanFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Transcribing the recordings of note {NoteId} failed; it is tried again later.")]
    private static partial void LogNoteFailed(ILogger logger, Exception exception, Guid noteId);
}
