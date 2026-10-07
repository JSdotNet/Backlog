namespace Backlog.Desktop.UI.Tasks;

/// <summary>
/// How the Tasks pane lays out the entries its filter bar lets through.
/// <para>
/// Each layout reads the same filtered rows under the same filter bar, beside the
/// same detail panel; only the half below the bar changes. The shell picks one
/// from its view switch.
/// </para>
/// </summary>
public enum TasksLayout
{
    /// <summary>The ranked list.</summary>
    List,

    /// <summary>A month of entries placed by their due date, with the undated ones
    /// in a tray beside it.</summary>
    Calendar
}
