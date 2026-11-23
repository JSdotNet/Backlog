using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.Modules.Inbox.Features.TranscribeRecordings;
using Backlog.Modules.Inbox.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Modules.Inbox.UnitTests;

/// <summary>
/// The background step of local ADR 0049 §1: once it has handled a note it says so on the
/// Inbox change signal, because what it wrote lands after the pull or the Retry that asked
/// for it has already reloaded an open pane.
/// </summary>
public sealed class InboxRecordingTranscriptionWorkerTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_handled_note_raises_the_inbox_change_signal_so_an_open_pane_reloads()
    {
        var noteId = Guid.NewGuid();
        var handler = new CountingHandler();
        var signal = new InboxChangeSignal();
        var raised = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.Changed += () => raised.TrySetResult();

        await using var services = new ServiceCollection()
            .AddSingleton<IInboxItemRepository>(new InMemoryInboxStore())
            .AddSingleton<ICommandHandler<TranscribeRecordingsCommand, Result<int>>>(handler)
            .AddSingleton<IInboxChangeSignal>(signal)
            .BuildServiceProvider();
        using var worker = new InboxRecordingTranscriptionWorker(services);

        worker.Start();
        worker.Request(noteId);

        await raised.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        Assert.Equal([noteId], handler.Handled);
    }

    private sealed class CountingHandler : ICommandHandler<TranscribeRecordingsCommand, Result<int>>
    {
        public List<Guid> Handled { get; } = [];

        public Task<Result<int>> Handle(TranscribeRecordingsCommand command, CancellationToken cancellationToken = default)
        {
            Handled.Add(command.NoteId);
            return Task.FromResult(Result.Success(1));
        }
    }
}
