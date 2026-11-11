namespace Backlog.UI.Components.Inputs;

/// <summary>
/// An audio file an <see cref="AudioPlayer"/> can play: its media type and a way to
/// read it. The host owns the bytes and where they live; the player only asks for them
/// once, when it is first drawn.
/// </summary>
/// <param name="ContentType">What the bytes are: "audio/aac", "audio/mp4".</param>
/// <param name="Open">Opens the bytes, or answers null when they are no longer
/// there — a file delivered and removed since the host last looked. It throws an
/// <see cref="IOException"/> when the bytes are there but cannot be read, which the
/// player says differently.</param>
public sealed record AudioSource(
    string ContentType,
    Func<CancellationToken, Task<Stream?>> Open);
