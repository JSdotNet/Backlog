namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// A popped-out window a person chose to remember, to reopen on the next start (local ADR 0043 §5):
/// the key that names it across runs, the surface and query it showed last, and its own
/// last bounds — null when none were kept, so it opens where the system puts a new one.
/// </summary>
public sealed record RememberedShellWindow(
    string Key,
    string SurfaceId,
    IReadOnlyDictionary<string, string> Query,
    ShellWindowBounds? Bounds);
