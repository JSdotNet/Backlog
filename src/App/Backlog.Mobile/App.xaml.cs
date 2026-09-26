using Backlog.Mobile.UI.Services;
using Microsoft.Maui.Networking;

namespace Backlog.Mobile;

public partial class App : Application
{
	private readonly AppLifecycle _lifecycle;

	public App(AppLifecycle lifecycle)
	{
		InitializeComponent();

		_lifecycle = lifecycle;

		// The network coming back is the moment a queued capture can go. Only a
		// change *to* internet counts: losing it has nothing to send.
		Connectivity.Current.ConnectivityChanged += (_, args) =>
		{
			if (args.NetworkAccess == NetworkAccess.Internet) _lifecycle.Resume();
		};
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new MainPage()) { Title = "Backlog.Mobile" };

		// Back from the background: flush the outbox and refresh the list. The
		// WebView's own visibilitychange may say the same; a second flush finds
		// nothing due.
		window.Resumed += (_, _) => _lifecycle.Resume();

		return window;
	}
}
