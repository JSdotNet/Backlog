using Backlog.SharedKernel;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The query keys the Devbook surface accepts (local ADR 0042 §6), and how a
/// repository alias or the Brain is written into one.
/// </summary>
public static class DevbookSurfaceQuery
{
    /// <summary>The Devbook's id in the surface catalog.</summary>
    public const string SurfaceId = "devbook";

    /// <summary>The page or chapter to open, as authored: <c>path</c> or
    /// <c>path#anchor</c>.</summary>
    public const string Chapter = "chapter";

    /// <summary>What the surface reads: a repository alias, or <see cref="Brain"/>.
    /// Without it, the window's repository scope (local ADR 0042 §6).</summary>
    public const string Scope = "scope";

    /// <summary>The repository on the other side of the pane's <c>Brain | repo</c>
    /// switch, while the switch is offered (local ADR 0027 §4). The main window's
    /// shell sets it from its repository scope.</summary>
    public const string Switch = "switch";

    /// <summary>Which press asked for <see cref="Chapter"/>: a new value is a new
    /// press, so the same chapter asked twice opens it again, and a value already
    /// followed in this window is not followed again by the next mounted surface.</summary>
    public const string Request = "request";

    /// <summary>The <see cref="Scope"/> value that names the Brain.</summary>
    public const string Brain = "brain";

    /// <summary>Every key, in the order the surface registers them.</summary>
    public static IReadOnlyList<string> Keys { get; } = [Chapter, Scope, Switch, Request];

    /// <summary>The <see cref="Scope"/> value for a repository alias, or for the
    /// Brain when <paramref name="alias"/> is <see cref="BrainScope.Value"/>.</summary>
    public static string ScopeFor(string alias) => BrainScope.Is(alias) ? Brain : alias;

    /// <summary>The repository alias a <see cref="Scope"/> value names —
    /// <see cref="BrainScope.Value"/> for <see cref="Brain"/>.</summary>
    public static string AliasFor(string scope) =>
        string.Equals(scope, Brain, StringComparison.Ordinal) ? BrainScope.Value : scope;
}
