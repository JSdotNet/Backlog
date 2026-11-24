namespace Backlog.SharedKernel.Changes;

/// <summary>
/// The change set the current asynchronous flow writes under (local ADR 0061 §2).
/// <para>
/// Ambient, on an <see cref="AsyncLocal{T}"/>, because the writes it names reach the
/// store through ports that carry no actor: each entry point opens a scope, and the
/// store reads <see cref="Current"/> inside the same flow. A scope opened in an async
/// method ends with that method, and its <see cref="Scope.Dispose"/> puts back the
/// change set that was current before, so nested scopes unwind in order.
/// </para>
/// <para>
/// The trap is the one the Tasks pane's self-write mark already has: work started
/// inside a scope — a <see cref="Task.Run(Func{Task})"/>, a timer created on the
/// writer's call — inherits it and would file its later writes under the gesture
/// that happened to start it. Start such work under <see cref="Detach"/>.
/// </para>
/// </summary>
public static class ChangeContext
{
    private static readonly AsyncLocal<ChangeSet?> s_current = new();

    /// <summary>The change set this flow writes under, or null when no entry point
    /// named one.</summary>
    public static ChangeSet? Current => s_current.Value;

    /// <summary>Opens a new change set for <paramref name="actor"/>, replacing any
    /// current one until the scope ends. For entry points whose actor is fixed
    /// whatever started them: a sync page, a background pass.</summary>
    public static Scope Begin(ChangeActor actor, string? label = null, TimeProvider? time = null) =>
        Enter(ChangeSet.Start(actor, label, time));

    /// <summary>Opens a new change set only when none is current, so a write inside a
    /// wider gesture joins that gesture's change set.</summary>
    public static Scope BeginIfNone(ChangeActor actor, string? label = null, TimeProvider? time = null) =>
        s_current.Value is null ? Begin(actor, label, time) : default;

    /// <summary>Makes <paramref name="changeSet"/> current until the scope ends —
    /// how a later write joins a change set an earlier one opened.</summary>
    public static Scope Enter(ChangeSet changeSet)
    {
        ArgumentNullException.ThrowIfNull(changeSet);

        var previous = s_current.Value;
        s_current.Value = changeSet;
        return new Scope(previous);
    }

    /// <summary>
    /// Stops the execution context flowing to work started before the returned value
    /// is disposed, so that work starts with no change set. Dispose it on the same
    /// thread, straight after starting the work.
    /// <para>
    /// Suppressed only when it is not already: suppressing twice throws, and undoing
    /// a suppression this did not make would hand the caller its flow back.
    /// </para>
    /// </summary>
    public static Detachment Detach() =>
        ExecutionContext.IsFlowSuppressed() ? default : new Detachment(ExecutionContext.SuppressFlow());

    /// <summary>Ends a change set scope, putting back the one current before it.
    /// The default value is a scope that changed nothing.</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly ChangeSet? _previous;
        private readonly bool _active;

        internal Scope(ChangeSet? previous)
        {
            _previous = previous;
            _active = true;
        }

        public void Dispose()
        {
            if (_active) s_current.Value = _previous;
        }
    }

    /// <summary>Restores the execution context's flow <see cref="Detach"/>
    /// suppressed. The default value restores nothing.</summary>
    public readonly struct Detachment : IDisposable
    {
        private readonly AsyncFlowControl? _control;

        internal Detachment(AsyncFlowControl control) => _control = control;

        public void Dispose() => _control?.Undo();
    }
}
