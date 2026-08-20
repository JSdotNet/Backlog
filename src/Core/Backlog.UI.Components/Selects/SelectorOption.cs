namespace Backlog.UI.Components.Selects;

/// <param name="Value">The value committed when the option is picked.</param>
/// <param name="Label">What the option and its chip read as.</param>
/// <param name="Hint">An optional aside shown only in the open list, never on the
/// chip — where an option comes from, or what taking it will do. Null draws
/// nothing, so an option with no hint looks exactly as it did before hints
/// existed.</param>
public sealed record SelectorOption(string Value, string Label, string? Hint = null);

