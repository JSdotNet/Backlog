namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// The parse cache the index reader and the knowledge stores keep between loads,
/// on its own: a file that has not changed is produced once, a file that changed
/// is produced again, and a file that cannot be stamped is never remembered.
/// <para>
/// What it buys the stores — a folder loaded twice parsed once — is asserted
/// beside them, in <c>DevbookStoreParseCacheTests</c> in the Desktop UI suite.
/// </para>
/// </summary>
public sealed class DevbookFileCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-devbook-cache-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public void A_file_that_has_not_changed_is_produced_once()
    {
        var path = Write("a.md", "# A");
        var cache = new DevbookFileCache<string>();
        var produced = 0;

        var first = cache.GetOrAdd(path, () => { produced++; return "one"; });
        var second = cache.GetOrAdd(path, () => { produced++; return "two"; });

        Assert.Equal("one", first);
        Assert.Same(first, second);
        Assert.Equal(1, produced);
        Assert.True(cache.Holds(path, path));
    }

    [Fact]
    public void A_file_that_changed_is_produced_again_and_clearing_forgets_everything()
    {
        var path = Write("a.md", "# A");
        var cache = new DevbookFileCache<string>();

        Assert.Equal("one", cache.GetOrAdd(path, () => "one"));

        // A different length is a different stamp whatever the clock did, which
        // is what keeps this test off the filesystem's timestamp resolution.
        Write("a.md", "# A, edited");
        Assert.Equal("two", cache.GetOrAdd(path, () => "two"));
        Assert.Equal("two", cache.GetOrAdd(path, () => "three"));

        cache.Clear();
        Assert.Equal("three", cache.GetOrAdd(path, () => "three"));
    }

    /// <summary>Nothing to stamp means nothing to remember: the caller sees the
    /// produce it always saw, exception and all.</summary>
    [Fact]
    public void A_file_that_is_not_there_is_produced_and_not_remembered()
    {
        var path = Path.Combine(_root, "missing.md");
        var cache = new DevbookFileCache<string>();

        Assert.Equal("one", cache.GetOrAdd(path, () => "one"));
        Assert.Equal("two", cache.GetOrAdd(path, () => "two"));
        Assert.False(cache.Holds(path, path));
        Assert.ThrowsAny<IOException>(() => cache.GetOrAdd(path, () => File.ReadAllText(path)));
    }

    private string Write(string relativePath, string content)
    {
        var fullPath = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
