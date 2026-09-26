using System.Runtime.CompilerServices;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// Points <see cref="DevbookDatabaseLocation"/> at a storage folder of this
/// run's own before any test asks it anything.
///
/// <para>The resolver is static because its callers are, and unconfigured it
/// answers "no database" for every repository — correct for the app, useless for
/// a suite that builds databases to read. Every test uses its own repository
/// root, and the key is the root's path, so one shared folder keeps every test's
/// database apart without any of them configuring anything. No observer is
/// attached: nothing in this suite builds in the background unless a test
/// constructs its own <see cref="DevbookDatabaseRefresher"/>.</para>
/// </summary>
internal static class TestDevbookStorage
{
    public static string CacheDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "backlog-devbook-storage", Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Configure() => DevbookDatabaseLocation.Configure(() => CacheDirectory);
}
