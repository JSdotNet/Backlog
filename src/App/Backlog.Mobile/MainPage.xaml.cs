#if ANDROID
using Microsoft.AspNetCore.Components.WebView;
#endif

namespace Backlog.Mobile;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();

#if ANDROID
		blazorWebView.BlazorWebViewInitialized += OnBlazorWebViewInitialized;
#endif
	}

#if ANDROID
	private OpenLayerBackCallback? _back;

	/// <summary>
	/// Back is offered to an open sheet or dialog before the WebView's history
	/// (<see cref="OpenLayerBackCallback"/>). Registered here because the handler has
	/// connected by now, and with it added the BlazorWebView's own Back callback:
	/// this one, added after it, is asked first. A WebView built again replaces the
	/// callback rather than stacking a second.
	/// </summary>
	private void OnBlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
	{
		_back?.Remove();
		_back = null;

		if ((ActivityOf(e.WebView.Context) ?? Platform.CurrentActivity) is AndroidX.Activity.ComponentActivity activity)
		{
			_back = OpenLayerBackCallback.Register(activity, e.WebView);
		}
	}

	/// <summary>The activity the WebView was built in: its context, or the one a
	/// context wrapper holds. The current activity is only the fallback — it is
	/// whichever activity is in front, which need not be this WebView's.</summary>
	private static AndroidX.Activity.ComponentActivity? ActivityOf(Android.Content.Context? context)
	{
		for (var current = context; current is not null; current = (current as Android.Content.ContextWrapper)?.BaseContext)
		{
			if (current is AndroidX.Activity.ComponentActivity activity) return activity;
			if (current is not Android.Content.ContextWrapper) return null;
		}

		return null;
	}
#endif
}
