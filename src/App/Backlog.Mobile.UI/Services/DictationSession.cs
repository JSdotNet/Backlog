namespace Backlog.Mobile.UI.Services;

/// <summary>
/// A page's side of dictation, the one the capture sheet and the note page share: it runs a
/// turn through <see cref="ISpeechTranscriber"/>, puts each stage on the page's status line
/// while the turn is live, and ends a turn the way leaving the page should.
/// </summary>
/// <remarks>
/// Leaving ends a turn by what it is doing. A turn on the device's recogniser is stopped, so
/// the words it heard still arrive — nothing of it leaves the phone, and the note page keeps
/// them. Any other turn is cancelled: a recording is never sent once the person has walked
/// away from it (local ADR 0045 §7), and a turn no engine has taken yet starts none.
/// </remarks>
public sealed class DictationSession(ISpeechTranscriber speech)
{
    private Turn? _turn;

    /// <summary>
    /// Runs one turn and returns what it heard, or why nothing — a transcriber that throws
    /// included, so the page's microphone is never left looking on.
    /// </summary>
    /// <param name="dispatch">The page's <c>InvokeAsync</c>: a stage can begin off the
    /// render thread.</param>
    /// <param name="showStatus">Puts a stage's line on the page; called on the render thread,
    /// and only while the turn is live and the page not left.</param>
    public async Task<SpeechTranscript> ListenAsync(Func<Action, Task> dispatch, Action<string> showStatus)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(showStatus);

        using var turn = new Turn();
        Interlocked.Exchange(ref _turn, turn);

        try
        {
            return await speech.ListenAsync(
                stage =>
                {
                    turn.Reached(stage);
                    _ = dispatch(() =>
                    {
                        if (turn.IsLive) showStatus(stage.Describe());
                    });
                },
                turn.Token);
        }
        catch (Exception ex)
        {
            // The transcribers report failures as results; anything still thrown would
            // otherwise leave the mic stuck on.
            return SpeechTranscript.Failed(ex.Message);
        }
        finally
        {
            turn.End();
            Interlocked.CompareExchange(ref _turn, null, turn);
        }
    }

    /// <summary>The person pressed stop: the words arrive on <see cref="ListenAsync"/>.</summary>
    public ValueTask StopAsync() => speech.StopAsync();

    /// <summary>The page is going: ends the live turn, if there is one, as the remarks say.</summary>
    public ValueTask LeaveAsync()
    {
        if (Volatile.Read(ref _turn) is not { } turn) return ValueTask.CompletedTask;

        return turn.Leave() == SpeechTurnStage.Listening ? speech.StopAsync() : ValueTask.CompletedTask;
    }

    /// <summary>One turn: its cancellation, the last stage it reached, and whether it has
    /// ended or been left.</summary>
    private sealed class Turn : IDisposable
    {
        private readonly Lock _gate = new();
        private readonly CancellationTokenSource _cancel = new();

        private SpeechTurnStage? _stage;
        private bool _ended;
        private bool _left;

        public Turn() => Token = _cancel.Token;

        public CancellationToken Token { get; }

        public bool IsLive
        {
            get
            {
                lock (_gate) return !_ended && !_left;
            }
        }

        public void Reached(SpeechTurnStage stage)
        {
            lock (_gate) _stage = stage;
        }

        /// <summary>Marks the turn left and returns the stage it was at; a turn past the
        /// device's recogniser, or not yet at any engine, is cancelled here.</summary>
        public SpeechTurnStage? Leave()
        {
            lock (_gate)
            {
                _left = true;
                if (!_ended && _stage != SpeechTurnStage.Listening) _cancel.Cancel();
                return _stage;
            }
        }

        public void End()
        {
            lock (_gate) _ended = true;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _ended = true;
                _cancel.Dispose();
            }
        }
    }
}
