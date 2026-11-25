using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace Backlog.Infrastructure.AzureMaps;

/// <summary>
/// The ISO 3166-1 alpha-2 code a typed country names, for Get Geocoding's <c>countryRegion</c>,
/// which takes only that code (local ADR 0061 §1). The address's country is free text
/// (<c>domain.md#address</c>), so "Belgium", "België", "be" and "BEL" all read as <c>BE</c>.
/// <para>
/// An alpha-2 or alpha-3 code is taken as the region it is; a name is matched against each
/// region's English and native names as the platform's cultures give them, ignoring case,
/// accents, surrounding space and a leading "the". Anything else names no country and reads as
/// null, so the adapter leaves the parameter out rather than send a value the service cannot read.
/// </para>
/// </summary>
internal static class AzureMapsCountry
{
    private static readonly Lazy<FrozenDictionary<string, string>> Codes = new(Build);

    /// <summary>The alpha-2 code <paramref name="country"/> names, upper case, or null.</summary>
    internal static string? CodeOf(string? country)
    {
        if (string.IsNullOrWhiteSpace(country)) return null;

        var key = Key(country);
        if (key.StartsWith("the ", StringComparison.Ordinal)) key = key[4..];

        return Codes.Value.TryGetValue(key, out var code) ? code : null;
    }

    private static FrozenDictionary<string, string> Build()
    {
        var codes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
        {
            RegionInfo region;
            try
            {
                region = new RegionInfo(culture.Name);
            }
            catch (ArgumentException)
            {
                continue;
            }

            var code = region.TwoLetterISORegionName;
            if (code.Length != 2 || !char.IsAsciiLetter(code[0]) || !char.IsAsciiLetter(code[1])) continue;
            code = code.ToUpperInvariant();

            // The first culture to name a key keeps it: a code or a name means one region.
            foreach (var name in new[] { code, region.ThreeLetterISORegionName, region.EnglishName, region.NativeName })
            {
                if (!string.IsNullOrWhiteSpace(name)) codes.TryAdd(Key(name), code);
            }
        }

        return codes.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Lower case, accents dropped, inner space collapsed: "  België " and "belgie" meet.</summary>
    private static string Key(string text)
    {
        var decomposed = text.Trim().Normalize(NormalizationForm.FormD);
        var key = new StringBuilder(decomposed.Length);
        var space = false;

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsWhiteSpace(c))
            {
                space = true;
                continue;
            }

            if (space && key.Length > 0) key.Append(' ');
            space = false;
            key.Append(char.ToLowerInvariant(c));
        }

        return key.ToString().Normalize(NormalizationForm.FormC);
    }
}
