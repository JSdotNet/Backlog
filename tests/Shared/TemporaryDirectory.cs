namespace Backlog.Tests;

/// <summary>
/// A folder of the test's own under the temp path, created on construction and deleted
/// on <see cref="Dispose"/>.
///
/// <para>Test classes used to write this themselves — a <c>GetTempPath</c> root, a
/// <c>CreateDirectory</c>, and a <c>Dispose</c> or <c>finally</c> that deleted it, each
/// with its own idea of which errors to swallow. This is the one copy. A test that checks
/// behaviour on a missing or locked folder still builds that folder itself.</para>
///
/// <para>Linked into every test project by <c>tests/Directory.Build.props</c>; it depends
/// on nothing but the base class library.</para>
/// </summary>
internal sealed class TemporaryDirectory : IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>Creates a fresh, empty folder under <see cref="System.IO.Path.GetTempPath"/>.</summary>
    /// <param name="prefix">The start of the folder's name, so a leftover folder says
    /// which tests made it. The rest of the name is a new GUID.</param>
    public TemporaryDirectory(string prefix = "backlog-tests")
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    /// <summary>The folder's full path.</summary>
    public string Path { get; }

    /// <summary>A path inside the folder; nothing is created.</summary>
    public string Combine(params string[] parts) =>
        System.IO.Path.Combine([Path, .. parts]);

    /// <summary>Deletes the folder and everything in it, and tries once more when that
    /// fails: at once after clearing the read-only attributes when a file refused access
    /// (a git object), after a short pause when a file was still held. A folder that will
    /// not go is left behind rather than failing the test.</summary>
    public void Dispose()
    {
        var failure = TryDelete();
        if (failure is null)
        {
            return;
        }

        if (failure is UnauthorizedAccessException)
        {
            ClearReadOnly();
        }
        else
        {
            Thread.Sleep(RetryDelay);
        }

        TryDelete();
    }

    private void ClearReadOnly()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The retry below reports nothing either way.
        }
    }

    private Exception? TryDelete()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex;
        }
    }
}
