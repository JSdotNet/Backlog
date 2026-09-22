namespace Backlog.UI.Components.Feedback;

/// <summary>
/// One step of a <see cref="SetupSteps"/> checklist: what to do, whether it has
/// been done, and where to go to do it.
/// </summary>
/// <param name="Title">What the step asks for, as an instruction.</param>
/// <param name="Done">Whether the field or action behind the step is satisfied.
/// The host derives it from its own state; the list draws it and nothing more.</param>
/// <param name="Detail">A sentence under the title saying how, or why.</param>
/// <param name="Href">A place to go for this step - a console page, a docs page.
/// Rendered after the detail as a link that opens beside the app.</param>
/// <param name="LinkLabel">The link's text. Required when <paramref name="Href"/>
/// is set; a bare URL is not a label.</param>
/// <param name="Optional">A step the setup works without. Drawn as such, and never
/// the reason a checklist reads as unfinished.</param>
public sealed record SetupStep(
    string Title,
    bool Done,
    string? Detail = null,
    string? Href = null,
    string? LinkLabel = null,
    bool Optional = false);
