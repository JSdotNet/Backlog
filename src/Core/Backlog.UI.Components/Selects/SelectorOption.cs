namespace Backlog.UI.Components.Selects;

/// <param name="Value">The value committed when the option is picked.</param>
/// <param name="Label">What the option and its chip read as.</param>
/// <param name="Hint">An optional aside shown only in the open list, never on the
/// chip — where an option comes from, or what taking it will do. Null draws
/// nothing, so an option with no hint looks exactly as it did before hints
/// existed.</param>
/// <param name="Group">The section this option belongs to in an open list, only
/// ever drawn there and never on the chip. Null puts the option in no section at
/// all, so a list whose options carry no group looks exactly as it did before
/// groups existed. The host owns the order — sections come out in the order the
/// options arrive in, and the control never sorts.</param>
public sealed record SelectorOption(string Value, string Label, string? Hint = null, string? Group = null);

