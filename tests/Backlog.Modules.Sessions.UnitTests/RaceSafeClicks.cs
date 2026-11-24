using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>
/// A click for a pane that is still busy when the test acts.
/// <para>
/// The Sessions pane keeps landing renders from its sources' background raises after the
/// render a test waited for, and each can redraw a button under a new event-handler id. A
/// button found on the test thread can then name a handler that is gone by the time the
/// click reaches the renderer: bUnit throws <c>UnknownEventHandlerIdException</c>, or the
/// click lands on nothing and the assertion after it reads what was there before. So the
/// button is found and clicked in one turn of the renderer, and the handler awaited, as
/// <c>RaceSafeTestHelpers</c> does for the desktop's panes.
/// </para>
/// </summary>
internal static class RaceSafeClicks
{
    /// <summary>Clicks the first element matching <paramref name="selector"/>; when this
    /// returns, the click has been handled and its render has landed.</summary>
    public static Task ClickAsync<TComponent>(this IRenderedComponent<TComponent> component, string selector)
        where TComponent : IComponent =>
        component.InvokeAsync(() => component.Find(selector).ClickAsync(new MouseEventArgs()));
}
