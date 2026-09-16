using System.Xml;
using System.Xml.Linq;

namespace Backlog.Desktop.UI.AppUpdate;

/// <summary>
/// Reads the one fact the update window wants from an App Installer manifest: the
/// version of the package it currently points at. The MSIX availability API only
/// answers "yes, there is an update", so the version a person would install comes
/// from the <c>.appinstaller</c> file published at the app's update source —
/// <c>MainPackage/@Version</c> (or <c>MainBundle/@Version</c> for a bundle), which
/// the release workflow keeps identical to the signed package.
/// </summary>
public static class AppInstallerManifest
{
    /// <summary>
    /// The package version the manifest advertises, or null when the text is not a
    /// well-formed App Installer manifest or names no package version. Never throws:
    /// a bad manifest means "version unknown", not a failed update check.
    /// </summary>
    public static string? ReadPackageVersion(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;

        try
        {
            using var reader = XmlReader.Create(
                new StringReader(xml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var root = XDocument.Load(reader).Root;
            if (root is null || root.Name.LocalName != "AppInstaller") return null;

            // The namespace carries the schema year (2017, 2018, 2021...), so match on
            // local names rather than pinning one.
            var package = root.Elements().FirstOrDefault(e => e.Name.LocalName is "MainPackage" or "MainBundle");
            var version = package?.Attribute("Version")?.Value;

            return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        }
        catch (XmlException)
        {
            return null;
        }
    }
}
