using System.Runtime.CompilerServices;

using Backlog.Infrastructure.Devbook;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Points <see cref="DevbookDatabaseLocation"/> at a storage folder of this
/// run's own before any test asks it anything — what the composition root does
/// for the app (local ADR 0015). Unconfigured, every repository has no database.
///
/// <para>Each test uses its own repository root and the key is the root's path,
/// so one shared folder keeps their databases apart. No observer is attached, so
/// nothing here builds a database in the background.</para>
/// </summary>
internal static class TestDevbookStorage
{
    public static string CacheDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "backlog-devbook-storage", Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Configure() => DevbookDatabaseLocation.Configure(() => CacheDirectory);
}
