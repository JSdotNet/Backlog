using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Backlog.ArchitectureTests;

/// <summary>
/// Rules for the app icon and splash the executable heads ship.
///
/// <para>These assets are the only part of the product a user sees before the
/// product runs, and they are the part nothing else checks. Both heads shipped
/// the .NET template's purple square and ".NET" wordmark for the whole life of
/// the repository without a single test going red, because the icon is not code
/// and never gets read — it goes straight from the folder into a signed MSIX and
/// a signed APK by way of <c>MauiIcon</c>. The release workflows publish
/// whatever is in <c>Resources</c>; nothing between the folder and the download
/// has an opinion about it.</para>
///
/// <para>So the rules here are the ones a reviewer cannot see in a diff: that the
/// two heads have not drifted apart, that no colour arrived from outside the
/// palette, and that the mark still fits the circle Android is allowed to crop
/// it to — which is geometry, and is the kind of thing that looks right in an
/// SVG preview and is wrong on a phone.</para>
/// </summary>
public class BrandAssetTests
{
    /// <summary>The executable heads that package an icon.</summary>
    private static readonly string[] Heads = ["Backlog.Desktop", "Backlog.Mobile"];

    /// <summary>Every brand asset, relative to a head's project folder.</summary>
    private static readonly string[] BrandAssets =
    [
        Path.Combine("Resources", "AppIcon", "appicon.svg"),
        Path.Combine("Resources", "AppIcon", "appiconfg.svg"),
        Path.Combine("Resources", "Splash", "splash.svg")
    ];

    /// <summary>The .NET MAUI template's own colours. Not "colours we dislike" —
    /// colours that mean the file was never replaced.</summary>
    private static readonly string[] TemplateColors = ["#512bd4", "#2b0b98"];

    /// <summary>
    /// Android composites an adaptive icon on a 108dp canvas but guarantees only
    /// the central 66dp circle survives: everything outside it is at the mercy of
    /// whichever mask the launcher applies. On the 456-unit canvas these assets
    /// are drawn at, that is a radius of 139.3 from the centre.
    /// </summary>
    private const double SafeRadius = 456.0 * 66.0 / 108.0 / 2.0;

    private const double CanvasCentre = 456.0 / 2.0;

    [Fact]
    public void No_head_still_ships_the_dotnet_template_branding()
    {
        foreach (var head in Heads)
        {
            foreach (var asset in BrandAssets)
            {
                var content = File.ReadAllText(RepositoryRoot.File("src", "App", head, asset))
                    .ToLowerInvariant();

                var found = TemplateColors.Where(content.Contains).ToList();

                Assert.True(
                    found.Count == 0,
                    $"{head}/{asset} still contains the .NET template colour(s) {string.Join(", ", found)}. "
                    + "This asset ships inside the signed release package, so the template branding would "
                    + "be what a user installs.");
            }

            Assert.False(
                File.Exists(RepositoryRoot.Combine("src", "App", head, "Resources", "Images", "dotnet_bot.svg")),
                $"{head} still carries the template's dotnet_bot.svg. Nothing renders it, but MauiImage "
                + "packages it, so it is .NET branding riding along inside the release artifact.");
        }
    }

    [Fact]
    public void The_heads_ship_the_same_brand_assets()
    {
        var reference = Heads[0];

        foreach (var asset in BrandAssets)
        {
            var expected = File.ReadAllText(RepositoryRoot.File("src", "App", reference, asset));

            foreach (var head in Heads.Skip(1))
            {
                var actual = File.ReadAllText(RepositoryRoot.File("src", "App", head, asset));

                Assert.True(
                    Normalize(expected) == Normalize(actual),
                    $"{head}/{asset} differs from {reference}/{asset}. Desktop and mobile are one product "
                    + "wearing one mark; the two copies exist only because MAUI resolves resources per "
                    + "project, not because the heads are allowed to look different.");
            }
        }
    }

    [Fact]
    public void Every_colour_in_the_brand_assets_is_a_palette_token()
    {
        var sanctioned = DesignPalette.SpecifiedColors().Values.ToHashSet();

        foreach (var head in Heads)
        {
            foreach (var asset in BrandAssets)
            {
                var svg = File.ReadAllText(RepositoryRoot.File("src", "App", head, asset));

                var unsanctioned = Regex.Matches(svg, @"#[0-9a-fA-F]{3,8}\b")
                    .Select(match => DesignPalette.Normalized(match.Value))
                    .Distinct()
                    .Where(colour => !sanctioned.Contains(colour))
                    .ToList();

                Assert.True(
                    unsanctioned.Count == 0,
                    $"{head}/{asset} paints {string.Join(", ", unsanctioned)}, which "
                    + ".design/color-scheme.md does not declare. The icon is the product's most-seen "
                    + "surface and cannot be the one place a colour enters the brand without being "
                    + "written down.");
            }
        }
    }

    [Fact]
    public void The_declared_icon_colour_is_the_products_base_surface()
    {
        var background = DesignPalette.Value("color-background");

        foreach (var head in Heads)
        {
            var project = XDocument.Load(RepositoryRoot.File("src", "App", head, $"{head}.csproj"));

            foreach (var element in new[] { "MauiIcon", "MauiSplashScreen" })
            {
                var declared = project.Descendants(element)
                    .Select(item => (string?)item.Attribute("Color"))
                    .SingleOrDefault();

                Assert.True(
                    declared is not null && DesignPalette.Normalized(declared) == background,
                    $"{head}.csproj declares {element} Color=\"{declared}\", not color-background "
                    + $"({background}). That attribute is the background the platform composites the "
                    + "foreground over when it does not use appicon.svg, so a different value shows up "
                    + "as a halo around the mark on exactly one platform.");
            }

            // And the background layer has to agree with it, or the Android
            // adaptive icon and the Windows tile disagree about the same icon.
            var backgroundLayer = XDocument
                .Load(RepositoryRoot.File("src", "App", head, "Resources", "AppIcon", "appicon.svg"))
                .Descendants().Where(node => node.Name.LocalName == "rect")
                .Select(rect => (string?)rect.Attribute("fill"))
                .SingleOrDefault();

            Assert.True(
                backgroundLayer is not null && DesignPalette.Normalized(backgroundLayer) == background,
                $"{head}/Resources/AppIcon/appicon.svg fills {backgroundLayer}, but the csproj declares "
                + $"the icon background as {background}. They are the same surface and have to match.");
        }
    }

    [Fact]
    public void The_mark_stays_inside_the_android_adaptive_icon_safe_circle()
    {
        foreach (var head in Heads)
        {
            var shapes = XDocument
                .Load(RepositoryRoot.File("src", "App", head, "Resources", "AppIcon", "appiconfg.svg"))
                .Descendants().Where(node => node.Name.LocalName == "rect")
                .ToList();

            Assert.True(
                shapes.Count > 0,
                $"{head}/appiconfg.svg draws no <rect>, so this rule cannot see the mark. If the mark is "
                + "now built from paths, measure those instead of letting the rule pass on nothing.");

            foreach (var shape in shapes)
            {
                var reach = FarthestInkRadius(shape);

                Assert.True(
                    reach <= SafeRadius,
                    $"A shape in {head}/appiconfg.svg reaches {reach:F1} from the canvas centre, past the "
                    + $"{SafeRadius:F1} Android guarantees. A launcher mask is allowed to cut everything "
                    + "beyond that, so this part of the mark would be missing on some phones and present "
                    + "on others.");
            }
        }
    }

    /// <summary>
    /// How far a rounded rectangle's ink reaches from the canvas centre. The
    /// farthest point sits on one of the four corner arcs, so it is the most
    /// distant arc centre plus that arc's radius.
    /// </summary>
    private static double FarthestInkRadius(XElement rect)
    {
        var x = Number(rect, "x");
        var y = Number(rect, "y");
        var width = Number(rect, "width");
        var height = Number(rect, "height");
        var radiusX = rect.Attribute("rx") is null ? 0 : Number(rect, "rx");
        var radiusY = rect.Attribute("ry") is null ? radiusX : Number(rect, "ry");

        double[] centresX = [x + radiusX, x + width - radiusX];
        double[] centresY = [y + radiusY, y + height - radiusY];

        return centresX
            .SelectMany(_ => centresY, (centreX, centreY) => (centreX, centreY))
            .Max(corner =>
                Math.Sqrt(Math.Pow(corner.centreX - CanvasCentre, 2)
                          + Math.Pow(corner.centreY - CanvasCentre, 2))
                + Math.Max(radiusX, radiusY));
    }

    private static double Number(XElement element, string name) =>
        double.Parse((string)element.Attribute(name)!, CultureInfo.InvariantCulture);

    /// <summary>Line endings are how the file was checked out, not what it draws.</summary>
    private static string Normalize(string svg) => svg.Replace("\r\n", "\n");
}
