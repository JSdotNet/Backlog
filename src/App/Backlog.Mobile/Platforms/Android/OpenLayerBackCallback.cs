using Android.OS;
using Android.Webkit;

using AndroidX.Activity;

using AWebView = Android.Webkit.WebView;

namespace Backlog.Mobile;

/// <summary>
/// Android's Back, offered first to whatever sheet or dialog is open over the page.
/// <para>
/// A sheet or a dialog opens without a navigation, so the WebView's history does not
/// know it is there. The BlazorWebView's own Back handling walks that history: from
/// the Inbox with a capture open, Back went to whichever tab came before and took the
/// sheet down with the page. This callback is added after the BlazorWebView's, and
/// the dispatcher runs the most recently added enabled callback first, so it asks
/// the page before the history is touched. <c>window.backlogBack</c> in
/// components.js hands the topmost open layer Escape and answers true; with nothing
/// open it answers false, and Back is passed on to the callbacks behind this one —
/// the BlazorWebView's history, then the activity — exactly as before.
/// </para>
/// <para>
/// A press is never lost: a page that has not answered within <see cref="AnswerTimeout"/>
/// — busy, or not loaded — is treated as having nothing open, and whichever of the
/// answer and the timeout comes first decides the press; the other finds it decided.
/// </para>
/// </summary>
internal sealed class OpenLayerBackCallback : OnBackPressedCallback
{
    /// <summary>True only when the page closed, or kept open, a layer of its own. A
    /// page whose script has not loaded yet has no <c>backlogBack</c> and answers false.</summary>
    private const string Script = "(window.backlogBack && window.backlogBack()) === true";

    /// <summary>How long Back waits for the page before it walks the history anyway.
    /// The script is a DOM query; anything slower is a page that cannot answer.</summary>
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromMilliseconds(300);

    private readonly WeakReference<AWebView> _webView;
    private readonly OnBackPressedDispatcher _dispatcher;
    private readonly Handler _mainThread = new(Looper.MainLooper!);

    private OpenLayerBackCallback(AWebView webView, OnBackPressedDispatcher dispatcher)
        : base(enabled: true)
    {
        _webView = new WeakReference<AWebView>(webView);
        _dispatcher = dispatcher;
    }

    /// <summary>Adds the callback to <paramref name="activity"/>'s dispatcher, tied to
    /// its lifecycle. Call it after the BlazorWebView's handler has connected, so this
    /// one is asked first.</summary>
    public static OpenLayerBackCallback Register(ComponentActivity activity, AWebView webView)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(webView);

        var callback = new OpenLayerBackCallback(webView, activity.OnBackPressedDispatcher);
        activity.OnBackPressedDispatcher.AddCallback(activity, callback);
        return callback;
    }

    /// <summary>
    /// The press is taken now and decided once the page answers or the wait runs
    /// out: the script runs asynchronously, and the dispatcher wants an answer before
    /// it returns. A page that closed something keeps the press; anything else hands
    /// it on. With no WebView left the press is handed on too — posted rather than
    /// dispatched from inside this dispatch.
    /// </summary>
    public override void HandleOnBackPressed()
    {
        var press = new Press(this);

        if (!_webView.TryGetTarget(out var webView))
        {
            _mainThread.Post(press.PassOn);
            return;
        }

        _mainThread.PostDelayed(press.Timeout, (long)AnswerTimeout.TotalMilliseconds);
        webView.EvaluateJavascript(Script, new Answer(press));
    }

    /// <summary>Hands the press to the callbacks behind this one: switched off for
    /// the one dispatch, so the dispatcher skips it, and on again for the next press.</summary>
    private void PassOn()
    {
        Enabled = false;

        try
        {
            _dispatcher.OnBackPressed();
        }
        finally
        {
            Enabled = true;
        }
    }

    /// <summary>One Back press, decided once. The answer and the timeout both run on
    /// the main thread, so the flag needs no lock; it only has to be read first.</summary>
    private sealed class Press
    {
        private readonly OpenLayerBackCallback _owner;
        private bool _decided;

        public Press(OpenLayerBackCallback owner)
        {
            _owner = owner;
            Timeout = new Java.Lang.Runnable(PassOn);
        }

        /// <summary>The wait's end, held as one runnable so the answer can take it
        /// back off the queue.</summary>
        public Java.Lang.IRunnable Timeout { get; }

        /// <summary>The page closed, or kept open, a layer: the press is spent.</summary>
        public void Keep() => Decide(passOn: false);

        public void PassOn() => Decide(passOn: true);

        private void Decide(bool passOn)
        {
            if (_decided) return;

            _decided = true;
            _owner._mainThread.RemoveCallbacks(Timeout);
            if (passOn) _owner.PassOn();
        }
    }

    /// <summary>The page's answer, on the main thread: the JSON of the script's value.</summary>
    private sealed class Answer(Press press) : Java.Lang.Object, IValueCallback
    {
        public void OnReceiveValue(Java.Lang.Object? value)
        {
            if (value?.ToString() == "true") press.Keep();
            else press.PassOn();
        }
    }
}
