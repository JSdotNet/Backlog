using Backlog.UI.Components.Shell;

namespace Backlog.Mobile.UI.Services;

/// <summary>
/// The <see cref="ISpeechTranscriber"/> the screens inject: it hands each turn to the engine
/// the Dictation setting names, the device's recogniser or <see cref="CloudSpeechTranscriber"/>
/// (local ADR 0045 §6).
/// </summary>
/// <remarks>
/// <para>
/// The choice is made at the start of every turn, so a change in the Settings sheet applies
/// from the next press of the mic, as the language does. The cloud takes a turn only when
/// it is chosen and every one of these holds; otherwise the device takes it, with no notice:
/// </para>
/// <list type="bullet">
///   <item>the phone is paired and not offline (<see cref="ISyncStatusSource"/>) — a phone
///   that cannot reach the service listens at once rather than recording into nothing;</item>
///   <item>the cloud has not said, earlier in this app run, that it cannot transcribe here
///   (<see cref="CloudDictationAvailability"/>) — the turn that found out said so itself;</item>
///   <item>the phone can record.</item>
/// </list>
/// <para>
/// A stop belongs to its turn. Choosing can take a round trip to the browser, and a stop
/// pressed before an engine has the turn ends it there, with nothing heard and no engine
/// started; a cancelled turn likewise starts neither. The stage the screen shows first is
/// the chosen engine's own, so a cloud turn never reads "Listening…" on its way to
/// "Recording…".
/// </para>
/// <para>
/// Both engines are the host's scoped registrations; this one owns neither and disposes
/// neither.
/// </para>
/// </remarks>
public sealed class SelectingSpeechTranscriber(
    ISpeechTranscriber onDevice,
    CloudSpeechTranscriber cloud,
    ISpeechSettingsStore settings,
    CloudDictationAvailability availability,
    ISyncStatusSource status) : ISpeechTranscriber
{
    private Turn? _turn;

    /// <inheritdoc />
    /// <remarks>With the cloud chosen, dictation is offered where either engine can take a
    /// turn, because a turn the cloud cannot take goes to the device.</remarks>
    public async ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
    {
        if (settings.Engine == DictationEngine.Cloud && await cloud.IsSupportedAsync(cancellationToken)) return true;

        return await onDevice.IsSupportedAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<SpeechTranscript> ListenAsync(CancellationToken cancellationToken = default) =>
        ListenAsync(onStage: null, cancellationToken);

    /// <inheritdoc />
    public async Task<SpeechTranscript> ListenAsync(Action<SpeechTurnStage>? onStage, CancellationToken cancellationToken = default)
    {
        var turn = new Turn();
        Interlocked.Exchange(ref _turn, turn);

        var engine = await ChooseAsync(cancellationToken);

        // Published before the stop is read, as StopAsync marks before it reads the engine:
        // whichever runs second sees the other, so a stop is never lost between the two.
        if (!turn.TakenBy(engine)) return SpeechTranscript.Heard(string.Empty);
        if (cancellationToken.IsCancellationRequested) return SpeechTranscript.Failed("Listening was cancelled.");

        return await engine.ListenAsync(onStage, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask StopAsync() =>
        Volatile.Read(ref _turn)?.Stop() is { } active ? active.StopAsync() : ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Volatile.Write(ref _turn, null);
        return ValueTask.CompletedTask;
    }

    private async ValueTask<ISpeechTranscriber> ChooseAsync(CancellationToken cancellationToken)
    {
        if (settings.Engine != DictationEngine.Cloud) return onDevice;
        if (!availability.IsAvailable) return onDevice;
        if (status.Current.Kind != SyncStatusKind.Synced) return onDevice;

        return await cloud.IsSupportedAsync(cancellationToken) ? cloud : onDevice;
    }

    /// <summary>One turn: the engine that took it, once one has, and whether it was stopped.</summary>
    private sealed class Turn
    {
        private ISpeechTranscriber? _engine;
        private int _stopped;

        /// <summary>Hands the turn to <paramref name="engine"/>; <c>false</c> when it was
        /// stopped first and no engine should start.</summary>
        public bool TakenBy(ISpeechTranscriber engine)
        {
            Interlocked.Exchange(ref _engine, engine);
            return Volatile.Read(ref _stopped) == 0;
        }

        /// <summary>Marks the turn stopped and returns the engine to pass the stop to, if one
        /// has the turn yet.</summary>
        public ISpeechTranscriber? Stop()
        {
            Interlocked.Exchange(ref _stopped, 1);
            return Volatile.Read(ref _engine);
        }
    }
}
