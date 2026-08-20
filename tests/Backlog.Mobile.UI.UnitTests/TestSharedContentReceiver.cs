namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// A share source the test triggers by hand, standing in for an Android intent
/// or a harness query string.
/// </summary>
/// <remarks>
/// It derives from <see cref="BufferedSharedContentReceiver"/> rather than
/// implementing <see cref="ISharedContentReceiver"/> from scratch, so the
/// subscribe-and-replay behaviour under test is the same code both hosts run.
/// Only the trigger is faked.
/// </remarks>
internal sealed class TestSharedContentReceiver : BufferedSharedContentReceiver
{
    public void Share(string? text, string? subject = null) =>
        Publish(SharedContent.From(text, subject));
}
