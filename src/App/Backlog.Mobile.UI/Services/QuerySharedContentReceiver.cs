using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Backlog.Mobile.UI.Services;

/// <summary>
/// <see cref="ISharedContentReceiver"/> over the address bar, for the mobile web
/// harness.
/// </summary>
/// <remarks>
/// <para>
/// An Android intent cannot be delivered to a browser, so the harness takes the
/// payload the one way a browser can be handed one: the query string.
/// <c>/?shared=https%3A%2F%2Fyoutu.be%2Fabc123&amp;subject=A%20title</c> is the
/// harness equivalent of tapping Share in YouTube. That is what keeps the UI half
/// of this feature drivable under Playwright, which is how it is reviewed without
/// an Android device in the room.
/// </para>
/// <para>
/// It is deliberately not registered in the MAUI head: a WebView URL is not how a
/// share reaches a MAUI app, and a shared registration would look wired up and
/// then never fire on a device.
/// </para>
/// <para>
/// The current address is read on construction rather than on subscribe, so the
/// payload is already buffered by the time the Inbox component asks for it — the
/// same ordering the Android head has for a real intent, and the reason a screen
/// needs no harness-specific code.
/// </para>
/// </remarks>
public sealed class QuerySharedContentReceiver : BufferedSharedContentReceiver, IDisposable
{
    /// <summary>The shared text, usually a link.</summary>
    public const string TextParameter = "shared";

    /// <summary>The sending app's title for it, standing in for <c>EXTRA_SUBJECT</c>.</summary>
    public const string SubjectParameter = "subject";

    private readonly NavigationManager _navigation;

    public QuerySharedContentReceiver(NavigationManager navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);

        _navigation = navigation;

        // A second share into a running app arrives as a navigation here, the way
        // it arrives as OnNewIntent on Android.
        _navigation.LocationChanged += OnLocationChanged;

        PublishFrom(navigation.Uri);
    }

    /// <inheritdoc />
    public void Dispose() => _navigation.LocationChanged -= OnLocationChanged;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => PublishFrom(e.Location);

    private void PublishFrom(string location)
    {
        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri)) return;

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        // No parameters at all is the ordinary case — someone opening the harness
        // to look at the inbox — and it has to leave the screen untouched.
        Publish(SharedContent.From(query[TextParameter], query[SubjectParameter]));
    }
}
