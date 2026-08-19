namespace Backlog.UI.Components.Tasks;

/// <summary>
/// How much of Tab a quick-edit field is taking off the browser. Two answers
/// rather than a bool because they are two different bargains, not the same one
/// switched off: what is being named is which keystrokes the component has
/// promised to handle itself, and a component that suppressed a key it then
/// ignored would leave the reader unable to move at all.
/// </summary>
internal enum TabGuard
{
    /// <summary>Every Tab, either way. A row's rename: forward hands the editor to
    /// the row below, Shift+Tab to the row above, and both are the list's answer
    /// rather than the browser's.</summary>
    Always,

    /// <summary>A forward Tab, and only while something has been typed. The add
    /// field is a permanent control sitting in the tab order, so Tab out of an
    /// empty one is the ordinary way past the list — the one keystroke it takes is
    /// the one that has a task to add.</summary>
    WhileFilled
}
