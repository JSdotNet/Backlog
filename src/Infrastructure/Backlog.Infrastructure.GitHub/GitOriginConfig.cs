using System.Text;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// What a checkout's own git config says its <c>origin</c> is, read in-process so
/// a status that Settings asks for on every render starts no git process.
/// <para>
/// The reader answers only where it can answer exactly as
/// <c>git remote get-url origin</c> would. Anything that could make git's answer
/// differ from the file's — an include, a url rewrite, a header it does not
/// parse — leaves the question unanswered, and the caller asks git.
/// </para>
/// </summary>
internal static class GitOriginConfig
{
    private const string GitDirectoryPrefix = "gitdir: ";

    /// <summary>
    /// The origin url when the config answers it, or <see cref="OriginConfigAnswer.Answered"/>
    /// false with the url the file holds, if any, as a best effort for when git
    /// cannot be started either.
    /// </summary>
    internal static OriginConfigAnswer Read(string checkout)
    {
        string? configPath = null;
        try
        {
            configPath = ResolveConfigPath(checkout);
            var answer = configPath is null || !File.Exists(configPath)
                ? OriginConfigAnswer.Unanswered(null)
                : ReadOrigin(File.ReadAllLines(configPath));
            return answer with { ConfigPath = configPath };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return OriginConfigAnswer.Unanswered(null) with { ConfigPath = configPath };
        }
    }

    /// <summary>
    /// The config git reads for the checkout: <c>.git/config</c> in a clone; in a
    /// linked worktree, the <c>.git</c> file's <c>gitdir:</c> leads to a folder
    /// whose <c>commondir</c> names the shared one holding it; a submodule's
    /// <c>gitdir:</c> folder has no <c>commondir</c> and holds its own. The same
    /// walk <c>DevelopmentWorkspace.MainCheckoutOf</c> makes, which this project
    /// cannot reference.
    /// </summary>
    private static string? ResolveConfigPath(string checkout)
    {
        var dotGit = Path.Combine(checkout, ".git");
        var gitDirectory = Directory.Exists(dotGit) ? dotGit : ReadGitDirectoryPointer(checkout, dotGit);
        if (gitDirectory is null || !Directory.Exists(gitDirectory)) return null;

        var commonDirectory = ReadCommonDirectory(gitDirectory) ?? gitDirectory;
        return Path.Combine(commonDirectory, "config");
    }

    private static string? ReadGitDirectoryPointer(string checkout, string dotGit)
    {
        if (!File.Exists(dotGit)) return null;

        // git itself accepts only "gitdir: " with the space; anything else it
        // refuses, so it is not resolved here either.
        var pointer = File.ReadLines(dotGit).FirstOrDefault();
        if (pointer is null || !pointer.StartsWith(GitDirectoryPrefix, StringComparison.Ordinal)) return null;

        var target = pointer[GitDirectoryPrefix.Length..].Trim();
        if (target.Length == 0) return null;

        return Path.IsPathRooted(target) ? Path.GetFullPath(target) : Path.GetFullPath(Path.Combine(checkout, target));
    }

    private static string? ReadCommonDirectory(string gitDirectory)
    {
        var pointer = Path.Combine(gitDirectory, "commondir");
        if (!File.Exists(pointer)) return null;

        var common = File.ReadLines(pointer).FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(common)) return null;

        return Path.IsPathRooted(common) ? Path.GetFullPath(common) : Path.GetFullPath(Path.Combine(gitDirectory, common));
    }

    private static OriginConfigAnswer ReadOrigin(IEnumerable<string> lines)
    {
        string? section = null;
        string? subsection = null;
        string? url = null;

        // The whole file is read even once it cannot answer, so the best-effort
        // url is the one in the file wherever the line that disqualified it sits.
        var answerable = true;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimStart();
            if (line.Length == 0 || line[0] is '#' or ';') continue;

            if (line[0] == '[')
            {
                if (!TryReadSectionHeader(line, out section, out subsection))
                {
                    answerable = false;
                    continue;
                }

                // An included file can define or precede the origin, wherever
                // the include sits, and reading it would mean following git's
                // own include rules; git does that better.
                if (string.Equals(section, "include", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(section, "includeIf", StringComparison.OrdinalIgnoreCase))
                {
                    answerable = false;
                }

                continue;
            }

            if (!TryReadEntry(line, out var key, out var value))
            {
                answerable = false;
                continue;
            }

            // A url rewrite changes what get-url prints, and a per-worktree config
            // could hold an origin of its own: either way the file is not the answer.
            if (string.Equals(key, "insteadOf", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(section, "extensions", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(key, "worktreeConfig", StringComparison.OrdinalIgnoreCase)))
            {
                answerable = false;
            }

            if (url is null
                && string.Equals(section, "remote", StringComparison.OrdinalIgnoreCase)
                && string.Equals(subsection, "origin", StringComparison.Ordinal)
                && string.Equals(key, "url", StringComparison.OrdinalIgnoreCase))
            {
                // A url key with no '=' is a boolean to git, and get-url refuses it.
                if (value is null) answerable = false;
                else url = value;
            }
        }

        return answerable && !string.IsNullOrEmpty(url)
            ? OriginConfigAnswer.Of(url)
            : OriginConfigAnswer.Unanswered(string.IsNullOrEmpty(url) ? null : url);
    }

    /// <summary>
    /// <c>[section]</c>, <c>[section "subsection"]</c>, or the legacy
    /// <c>[section.subsection]</c>, whose subsection git lowercases. Anything after
    /// the closing bracket other than a comment is refused rather than guessed at.
    /// </summary>
    private static bool TryReadSectionHeader(string line, out string? section, out string? subsection)
    {
        section = null;
        subsection = null;

        var index = 1;
        var name = new StringBuilder();
        while (index < line.Length && (char.IsAsciiLetterOrDigit(line[index]) || line[index] is '-' or '.'))
        {
            name.Append(line[index++]);
        }

        if (name.Length == 0 || index >= line.Length) return false;

        if (line[index] == ']')
        {
            var dot = name.ToString().IndexOf('.', StringComparison.Ordinal);
            section = dot < 0 ? name.ToString() : name.ToString(0, dot);
            subsection = dot < 0 ? null : name.ToString(dot + 1, name.Length - dot - 1).ToLowerInvariant();
            return section.Length > 0 && IsRestOfLineEmpty(line, index + 1);
        }

        if (name.ToString().Contains('.', StringComparison.Ordinal)) return false;

        while (index < line.Length && line[index] is ' ' or '\t') index++;
        if (index >= line.Length || line[index] != '"') return false;
        index++;

        var sub = new StringBuilder();
        while (index < line.Length && line[index] != '"')
        {
            if (line[index] == '\\')
            {
                index++;
                if (index >= line.Length) return false;
            }

            sub.Append(line[index++]);
        }

        if (index + 1 >= line.Length || line[index + 1] != ']') return false;

        section = name.ToString();
        subsection = sub.ToString();
        return IsRestOfLineEmpty(line, index + 2);
    }

    /// <summary>
    /// <c>key</c>, <c>key = value</c>, with git's value rules: whitespace around
    /// the value dropped, a comment ends it outside quotes, quotes removed, and the
    /// escapes git knows. A value continued onto the next line is refused.
    /// </summary>
    private static bool TryReadEntry(string line, out string key, out string? value)
    {
        value = null;

        var index = 0;
        while (index < line.Length && (char.IsAsciiLetterOrDigit(line[index]) || line[index] == '-')) index++;

        key = line[..index];
        if (key.Length == 0 || !char.IsAsciiLetter(key[0])) return false;

        while (index < line.Length && line[index] is ' ' or '\t') index++;
        if (index >= line.Length || line[index] is '#' or ';') return true;
        if (line[index] != '=') return false;

        return TryReadValue(line, index + 1, out value);
    }

    private static bool TryReadValue(string line, int index, out string? value)
    {
        value = null;

        var builder = new StringBuilder();
        var quoted = false;
        var pendingSpaces = 0;

        for (; index < line.Length; index++)
        {
            var character = line[index];

            if (!quoted && character is ' ' or '\t')
            {
                if (builder.Length > 0) pendingSpaces++;
                continue;
            }

            if (!quoted && character is '#' or ';') break;

            builder.Append(' ', pendingSpaces);
            pendingSpaces = 0;

            if (character == '\\')
            {
                if (++index >= line.Length) return false;

                var escaped = line[index] switch
                {
                    't' => '\t',
                    'b' => '\b',
                    'n' => '\n',
                    '\\' => '\\',
                    '"' => '"',
                    _ => (char?)null
                };

                if (escaped is null) return false;
                builder.Append(escaped.Value);
                continue;
            }

            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }

            builder.Append(character);
        }

        if (quoted) return false;

        value = builder.ToString();
        return true;
    }

    private static bool IsRestOfLineEmpty(string line, int index)
    {
        var rest = line[index..].TrimStart();
        return rest.Length == 0 || rest[0] is '#' or ';';
    }
}

/// <summary>
/// The reader's answer: <see cref="Answered"/> when <see cref="Url"/> is what git
/// would say, otherwise the url the file holds, if any, as a best effort.
/// <see cref="ConfigPath"/> is the config git would read, when it could be
/// resolved, so a caller can tell when the answer may have changed.
/// </summary>
internal readonly record struct OriginConfigAnswer(bool Answered, string? Url, string? ConfigPath = null)
{
    internal static OriginConfigAnswer Of(string url) => new(true, url);

    internal static OriginConfigAnswer Unanswered(string? bestEffort) => new(false, bestEffort);
}
