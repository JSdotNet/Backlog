namespace Backlog.Mobile.UI.Services;

/// <summary>
/// The Inbox's unsent capture text, held outside the page.
/// <para>
/// The Router remounts a page on every navigation, so a draft kept in the Inbox's
/// own fields would be gone after a trip to another tab and back — typed text
/// lost to a thumb that only meant to look at something. Scoped, so it lives as
/// long as the app's circuit or web view and no longer.
/// </para>
/// </summary>
public sealed class CaptureDraft
{
    public string Text { get; set; } = string.Empty;
}
