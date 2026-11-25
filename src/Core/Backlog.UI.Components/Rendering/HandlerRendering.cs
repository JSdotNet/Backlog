namespace Backlog.UI.Components.Rendering;

/// <summary>
/// Runs a component's work from outside Blazor's event dispatch — a lifecycle method, a
/// timer, a script, a service's event, a host's resume — with the renders Blazor gives an
/// event handler: one as soon as the work first waits, so a busy state paints, and one
/// when it ends.
/// <para>
/// A method that is also bound as a handler leaves rendering to Blazor, which renders
/// around it already (BL0012), so it calls no <c>StateHasChanged</c> of its own. Its other
/// callers come through here, and see the same two renders a click does.
/// </para>
/// </summary>
public static class HandlerRendering
{
    /// <summary>Runs <paramref name="work"/> and renders through <paramref name="render"/>
    /// the way <c>ComponentBase</c> renders around a handler: once when the work returns,
    /// and once more when it completes, if it had to wait.</summary>
    public static async Task RunAsync(Func<Task> work, Action render)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(render);

        var task = work();
        var waited = !task.IsCompleted;

        render();
        await task;

        if (waited) render();
    }
}
