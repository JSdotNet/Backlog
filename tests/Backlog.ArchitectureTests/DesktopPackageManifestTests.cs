using System.Xml.Linq;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The packaged desktop app writes where it says it writes.
/// <para>
/// A packaged Windows app has its <c>%LocalAppData%</c> writes redirected into
/// <c>Packages\&lt;family&gt;\LocalCache\Local</c> unless its manifest opts out.
/// Ours did not, so the Storage settings page named
/// <c>C:\Users\...\AppData\Local\Backlog</c> while the backlog sat in a folder
/// Explorer showed no sign of — and the person who went looking for the file
/// to back up found nothing. The opt-out is one element in a manifest nothing
/// else reads, which is exactly the kind of line a template refresh drops
/// without anybody noticing; this is what notices.
/// </para>
/// </summary>
public class DesktopPackageManifestTests
{
    private static readonly XNamespace Desktop6 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/6";

    private static XDocument Manifest() =>
        XDocument.Load(Path.Combine(
            Repository.Root.FullName,
            "src", "App", "Backlog.Desktop", "Platforms", "Windows", "Package.appxmanifest"));

    [Fact]
    public void File_system_write_virtualization_is_disabled()
    {
        var properties = Manifest().Root?.Elements().Single(e => e.Name.LocalName == "Properties");
        var element = properties?.Element(Desktop6 + "FileSystemWriteVirtualization");

        Assert.NotNull(element);
        Assert.Equal("disabled", element.Value.Trim());
    }

    /// <summary>The opt-out is refused by the packaging tool without this
    /// capability beside it, which the Release publish is the first thing to
    /// say — PR builds run unpackaged and never reach it.</summary>
    [Fact]
    public void The_unvirtualized_resources_capability_is_declared()
    {
        var capabilities = Manifest().Root?.Elements().Single(e => e.Name.LocalName == "Capabilities");
        var names = capabilities?.Elements()
            .Where(e => e.Name.LocalName == "Capability")
            .Select(e => e.Attribute("Name")?.Value)
            .ToList() ?? [];

        Assert.Contains("unvirtualizedResources", names);
    }

    /// <summary>The element is in a namespace older Windows versions do not
    /// know. Marked ignorable, they install the package and keep redirecting;
    /// not marked, they refuse the package outright.</summary>
    [Fact]
    public void The_desktop6_namespace_is_ignorable()
    {
        var ignorable = Manifest().Root?.Attribute("IgnorableNamespaces")?.Value ?? string.Empty;

        Assert.Contains("desktop6", ignorable.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
