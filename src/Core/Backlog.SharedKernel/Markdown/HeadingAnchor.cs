namespace Backlog.SharedKernel.Markdown;

/// <summary>
/// The anchor a Markdown heading is addressed by: GitHub's rule as the devbook
/// generator spells it (<c>slugify</c> in the devbook plugin's <c>metadata.mjs</c>).
/// Lower-case, trim, drop everything but letters, digits, underscores, whitespace and
/// hyphens, then turn each remaining whitespace character into a hyphen — without
/// collapsing runs. <c>Screen Reader / Announcements</c> is
/// <c>screen-reader--announcements</c>.
/// <para>
/// The one heading slug every reader uses, so a chapter key the database stores, a
/// fingerprint's anchor, a scenario part and a link a panel follows are spelled the
/// same way. Whitespace is JavaScript's (<see cref="EcmaScriptWhitespace"/>), because
/// the generator's rows are what the builder is compared against.
/// </para>
/// </summary>
public static class HeadingAnchor
{
    /// <summary>The anchor of <paramref name="heading"/>; empty for null or
    /// blank.</summary>
    public static string Of(string? heading)
    {
        if (string.IsNullOrEmpty(heading)) return string.Empty;

        var lowered = EcmaScriptWhitespace.Trim(heading.ToLowerInvariant());
        var slug = new char[lowered.Length];
        var length = 0;

        foreach (var ch in lowered)
        {
            if (EcmaScriptWhitespace.Is(ch)) slug[length++] = '-';
            else if (ch is '_' or '-' || IsLetterOrNumber(ch)) slug[length++] = ch;
        }

        return new string(slug, 0, length);
    }

    /// <summary>JavaScript's <c>\p{L}</c> and <c>\p{N}</c>, which take every number
    /// category where <see cref="char.IsLetterOrDigit(char)"/> takes decimal digits
    /// alone.</summary>
    private static bool IsLetterOrNumber(char ch) =>
        char.GetUnicodeCategory(ch) is
            System.Globalization.UnicodeCategory.UppercaseLetter
            or System.Globalization.UnicodeCategory.LowercaseLetter
            or System.Globalization.UnicodeCategory.TitlecaseLetter
            or System.Globalization.UnicodeCategory.ModifierLetter
            or System.Globalization.UnicodeCategory.OtherLetter
            or System.Globalization.UnicodeCategory.DecimalDigitNumber
            or System.Globalization.UnicodeCategory.LetterNumber
            or System.Globalization.UnicodeCategory.OtherNumber;
}

/// <summary>
/// JavaScript's <c>\s</c> and <c>String.prototype.trim</c> set: the ECMAScript
/// WhiteSpace and LineTerminator characters. It differs from .NET's in both
/// directions — U+FEFF is in it, U+0085 is not — and the devbook readers that are
/// ports of <c>metadata.mjs</c> take JavaScript's answer, because that is the side
/// their output is compared against.
/// </summary>
public static class EcmaScriptWhitespace
{
    /// <summary>The set as the body of a regular-expression character class.</summary>
    public const string CharacterClass = "\\t\\n\\v\\f\\r \\u00a0\\u1680\\u2000-\\u200a\\u2028\\u2029\\u202f\\u205f\\u3000\\ufeff";

    /// <summary>Whether <paramref name="c"/> is in the set.</summary>
    public static bool Is(char c) =>
        (int)c is 0x09 or 0x0a or 0x0b or 0x0c or 0x0d or 0x20 or 0xa0 or 0x1680
            or (>= 0x2000 and <= 0x200a)
            or 0x2028 or 0x2029 or 0x202f or 0x205f or 0x3000 or 0xfeff;

    /// <summary><c>String.prototype.trim</c>.</summary>
    public static string Trim(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && Is(value[start])) start++;
        while (end > start && Is(value[end - 1])) end--;
        return start == 0 && end == value.Length ? value : value[start..end];
    }
}
