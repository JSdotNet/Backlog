namespace Backlog.UI.Components.Buttons;

/// <summary>
/// The one act a component has in flight, so the button that started it can show
/// <see cref="AppButton.Busy"/> and a second press is ignored rather than run twice.
///
/// <para>A component that raises an <c>EventCallback</c> from a button owns this
/// rather than its host because only the component sees the press and the end of
/// the awaited callback together. Left to the host, every surface would need a
/// flag of its own, and the ones without one would leave the button looking idle
/// while a slow act runs (<c>.devbook/design/interaction-guidelines.md#busy-actions</c>).</para>
///
/// <para>One act at a time per component, not one per button: a card whose Accept
/// is running has no business also running Discard, and the owner shows the
/// others as blocked through <see cref="IsAnyRunning"/>. The flag is set before
/// the callback is called and cleared in a <c>finally</c>, and the owner is told
/// to render when the act settles. That last part is not redundant:
/// <c>ComponentBase</c> renders after an awaited event handler only when it
/// completes, so an act that faults after its first await would otherwise leave
/// the button drawn busy — and refusing presses — until something unrelated
/// re-rendered it.</para>
///
/// <para>The busy state first renders at the callback's first real await; a host
/// that does synchronous work before it still freezes, which is the host's to fix.</para>
/// </summary>
internal sealed class PendingAct(Action onSettled)
{
    private readonly Action _onSettled = onSettled ?? throw new ArgumentNullException(nameof(onSettled));

    /// <summary>The key of the act in flight, or null when none is.</summary>
    public string? Running { get; private set; }

    /// <summary>Whether the act named <paramref name="key"/> is the one in flight.</summary>
    public bool IsRunning(string key) => Running == key;

    /// <summary>Whether any act is in flight — the cue for the owner to show its
    /// other acts as blocked rather than let a press on them look lost.</summary>
    public bool IsAnyRunning => Running is not null;

    /// <summary>Runs <paramref name="act"/> under <paramref name="key"/>, or does
    /// nothing while another act is still running. A failure goes on to the
    /// caller after the owner has been told to render.</summary>
    public async Task RunAsync(string key, Func<Task> act)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(act);

        if (Running is not null) return;

        Running = key;
        try
        {
            await act();
        }
        finally
        {
            Running = null;
            _onSettled();
        }
    }
}
