namespace Backlog.UI.Components.Feedback;

/// <summary>
/// Where a <see cref="FlowStep"/> stands. Five tones rather than a status string:
/// the host knows its own vocabulary and maps it here, and the list draws a tone,
/// never a word it would have to guess the meaning of.
/// </summary>
public enum FlowStepTone
{
    /// <summary>Not reached yet.</summary>
    Pending,

    /// <summary>The step the flow is on.</summary>
    Active,

    /// <summary>Finished.</summary>
    Done,

    /// <summary>Stopped on something that needs a person.</summary>
    Blocked,

    /// <summary>Passed over on purpose.</summary>
    Skipped
}

/// <summary>
/// One line under a step: who or what did the work, and a qualifier beside it —
/// an agent and the model it ran on.
/// </summary>
/// <param name="Label">The thing itself, drawn in the mono face.</param>
/// <param name="Detail">What qualifies it, drawn quieter after it.</param>
public sealed record FlowStepNote(string Label, string? Detail = null);

/// <summary>
/// One step of a <see cref="FlowSteps"/> diagram.
/// </summary>
/// <param name="Title">The step's name.</param>
/// <param name="Tone">Where it stands, which the node is drawn in.</param>
/// <param name="Status">The same fact in words — the tone is a colour and a glyph,
/// and neither carries anything on its own.</param>
/// <param name="Facts">What follows the status on its line, already joined: a
/// duration, a repeat count.</param>
/// <param name="Notes">Lines under the status, in the order given.</param>
public sealed record FlowStep(
    string Title,
    FlowStepTone Tone,
    string Status,
    string? Facts = null,
    IReadOnlyList<FlowStepNote>? Notes = null);
