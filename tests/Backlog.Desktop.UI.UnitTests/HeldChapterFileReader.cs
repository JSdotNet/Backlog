namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The disk, except for one chapter whose read is held until the test lets it go.
/// <para>
/// A panel starts a chapter read on every parameter set, so two reads overlap
/// whenever the selection moves while a file is still coming off disk — and on a
/// fast machine they almost never do. Holding the first read is what lets a test
/// make the second one finish first on purpose, which is the order the stale
/// chapter needs.
/// </para>
/// </summary>
internal sealed class HeldChapterFileReader(string heldFileName) : DevbookChapterFileReader
{
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _returned = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once the held chapter's read has been asked for.</summary>
    internal Task Started => _started.Task;

    /// <summary>Completes once the held chapter's text has been handed back, after
    /// <see cref="Release"/>. The panel still has to take it up on its dispatcher,
    /// which is the render a test waits for next.</summary>
    internal Task Returned => _returned.Task;

    /// <summary>Whether a read of the held chapter waits. Off lets a panel open on
    /// the chapter first, so the read that is held is a later one.</summary>
    internal bool Holding { get; set; } = true;

    internal void Release() => _release.TrySetResult();

    internal override async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
    {
        if (!Holding || !string.Equals(Path.GetFileName(path), heldFileName, StringComparison.OrdinalIgnoreCase))
        {
            return await base.ReadAllTextAsync(path, cancellationToken);
        }

        _started.TrySetResult();
        await _release.Task.WaitAsync(cancellationToken);

        var text = await base.ReadAllTextAsync(path, cancellationToken);
        _returned.TrySetResult();
        return text;
    }
}
