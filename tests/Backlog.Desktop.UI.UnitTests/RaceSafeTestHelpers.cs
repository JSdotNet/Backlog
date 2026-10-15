using AngleSharp.Dom;

using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Gestures and file reads for a test whose component is still busy when the test acts.
/// <para>
/// A pane or a calendar that loads on the thread pool keeps landing renders after the
/// first one a test waited for. Each render can redraw a control under a new event-handler
/// id. A control found on the test thread can then name a handler that is gone by the time a
/// separate event reaches the renderer, and bUnit throws <c>UnknownEventHandlerIdException</c>
/// or swallows the event. The synchronous <c>Click</c>, <c>Input</c> and <c>Change</c> also
/// return without the event's render, so a <c>Find</c> straight after one can read the markup
/// from before it.
/// </para>
/// <para>
/// The gestures here therefore find the control and dispatch to it in one turn of the
/// renderer, and await the handler. When one returns, the event has been handled and its
/// render has landed. This follows commits 2538f244 and 090eeff3.
/// </para>
/// <para>
/// A file a component writes is read through <see cref="ReadOrNull"/> inside a
/// <c>Polling.WaitUntilAsync</c>, not inside a <c>WaitForAssertion</c>. bUnit re-checks a
/// <c>WaitForAssertion</c> only when the component renders, and a write need not be followed
/// by a render.
/// </para>
/// </summary>
internal static class RaceSafeTestHelpers
{
    /// <summary>Clicks the first element matching <paramref name="selector"/>.</summary>
    public static Task ClickAsync<TComponent>(this IRenderedComponent<TComponent> component, string selector)
        where TComponent : IComponent =>
        component.InvokeAsync(() => Find(component, selector).ClickAsync(new MouseEventArgs()));

    /// <summary>Types <paramref name="text"/> into the first element matching <paramref name="selector"/>.</summary>
    public static Task InputAsync<TComponent>(this IRenderedComponent<TComponent> component, string selector, string text)
        where TComponent : IComponent =>
        component.InvokeAsync(() => Find(component, selector).InputAsync(new ChangeEventArgs { Value = text }));

    /// <summary>Commits <paramref name="value"/> to the first element matching <paramref name="selector"/>.</summary>
    public static Task ChangeAsync<TComponent>(this IRenderedComponent<TComponent> component, string selector, object value)
        where TComponent : IComponent =>
        component.InvokeAsync(() => Find(component, selector).ChangeAsync(new ChangeEventArgs { Value = value }));

    /// <summary>Leaves the first element matching <paramref name="selector"/>.</summary>
    public static Task BlurAsync<TComponent>(this IRenderedComponent<TComponent> component, string selector)
        where TComponent : IComponent =>
        component.InvokeAsync(() => Find(component, selector).BlurAsync(new FocusEventArgs()));

    /// <summary>The file's text, or null while it is missing or held by the write that is
    /// replacing it. A poll reads null as "not yet".</summary>
    public static string? ReadOrNull(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IElement Find<TComponent>(IRenderedComponent<TComponent> component, string selector)
        where TComponent : IComponent =>
        component.Find(selector);
}
