using Microsoft.JSInterop;

namespace Backlog.UI.Components.Tasks;

/// <summary>
/// The two browser things a quick-edit field needs, in one place because two
/// components need them: a row renaming itself, and the list's add row for a task
/// that does not exist yet. Both are the same field doing the same job, so the
/// second one having its own copy of this would be a second copy to get wrong.
/// </summary>
/// <remarks>
/// Nothing here is worth an error. Without scripting the field is still there and
/// still typeable — the reader clicks into it and quick edit degrades to a rename
/// whose Tab leaves the field, which is what Tab does everywhere else anyway. The
/// row that a Tab was going to open is opened from C# regardless, because the
/// chain is state and not a focus trick.
/// </remarks>
internal static class QuickEditField
{
    /// <summary>
    /// The caret in the field and, unless told otherwise, its value selected with
    /// it, so the first keystroke replaces what is there rather than being appended
    /// to the end of it. That is what renaming means everywhere else, and it is the
    /// difference between arriving in the field and arriving ready to type.
    /// </summary>
    /// <param name="select">Off for anything that is not a field. Restoring the
    /// focus ring to the pencil a rename was dismissed from is the same call to the
    /// same helper, and a button has no text to select.</param>
    public static Task FocusAsync(IJSRuntime js, string id, bool select = true) =>
        CallAsync(js, "backlogFocus", id, select);

    /// <summary>
    /// Stops the browser doing its own thing with Tab while this field is the one
    /// being typed in. Asked for as the field opens rather than as the key is
    /// pressed, because by the time a keydown reaches C# the browser has already
    /// decided what to do with it.
    /// </summary>
    /// <param name="guard">How much of Tab the field is taking. A rename takes all
    /// of it; the add field takes only the forward Tab that has something to add,
    /// because Tab out of an empty one is how a reader leaves the list.</param>
    public static Task GuardTabAsync(IJSRuntime js, string id, TabGuard guard = TabGuard.Always) =>
        guard is TabGuard.WhileFilled
            ? CallAsync(js, "backlogGuardTab", id, "filled")
            : CallAsync(js, "backlogGuardTab", id);

    /// <summary>One call naming the field by id — always first, because every one
    /// of these functions starts by finding the element.</summary>
    private static async Task CallAsync(IJSRuntime js, string function, params object?[] arguments)
    {
        try
        {
            await js.InvokeVoidAsync(function, arguments);
        }
        catch (JSException)
        {
            // The field is there and typeable; the browser just did not oblige.
        }
        catch (JSDisconnectedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}
