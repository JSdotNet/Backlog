using System.Text.Json;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The VS Code extension's reach is a product decision, not a version to keep
/// current.
///
/// <para><c>@types/vscode</c> and <c>engines.vscode</c> are one number: the types
/// package a dependency bot would happily raise is what decides the oldest editor
/// the extension can be installed into, and 1.90 to 1.134 is forty-four releases
/// of editors that would stop being able to install it. The extension calls no
/// API newer than 1.90, so raising it buys nothing and costs reach.</para>
///
/// <para>A rule rather than a comment because <c>package.json</c> is strict JSON
/// and a comment there is a key nobody has to read. This is what makes the next
/// person raising the version state a reason.</para>
/// </summary>
public class VsCodeExtensionReachTests
{
    private static readonly string[] Manifest = ["src", "App", "Backlog.Ide.VsCode", "package.json"];

    private const string HeldVersion = "^1.90.0";

    [Fact]
    public void The_extension_still_reaches_every_editor_back_to_1_90()
    {
        var manifest = Package();

        Assert.Equal(HeldVersion, manifest.GetProperty("engines").GetProperty("vscode").GetString());
        Assert.Equal(HeldVersion, manifest.GetProperty("devDependencies").GetProperty("@types/vscode").GetString());
    }

    /// <summary>The version being held is only a decision while the reason for
    /// holding it is written down beside it.</summary>
    [Fact]
    public void The_manifest_says_why_that_version_is_held()
    {
        var notes = Package()
            .EnumerateObject()
            .Where(property => property.Name.StartsWith("//", StringComparison.Ordinal))
            .Select(property => property.Value.GetString() ?? "")
            .ToList();

        Assert.True(
            notes.Any(note => note.Contains("@types/vscode", StringComparison.Ordinal)
                              && note.Contains("engines.vscode", StringComparison.Ordinal)),
            "package.json holds @types/vscode below the current release without saying why. Record the "
            + "reason in a top-level \"//\" key — inside devDependencies npm would read it as a package to "
            + "install — naming both @types/vscode and engines.vscode, so the next bump is a decision "
            + "rather than a routine.");
    }

    private static JsonElement Package() =>
        JsonDocument.Parse(File.ReadAllText(RepositoryRoot.File(Manifest))).RootElement;
}
