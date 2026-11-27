using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// Code that needs the current time asks an injected <see cref="TimeProvider"/>, and tests
/// hand it a <c>FakeTimeProvider</c> (app-defaults ADR 0005). A direct read of the static
/// clock cannot be tested at a chosen moment, and twice at the same one, which is what
/// "today", an age or an expiry needs.
///
/// <para><c>DateTime.Now</c> and <c>DateTime.UtcNow</c> are the reads the decision names, and
/// <c>src/</c> holds none: those are banned outright. <c>DateTime.Today</c> and
/// <c>DateTimeOffset.Now</c> / <c>UtcNow</c> are the same read in other clothes; the files that
/// still make one predate this rule and are listed in <see cref="PredatesTheRule"/>, so a new
/// file cannot add one and the list only shrinks.</para>
///
/// <para>A source scan rather than an analyzer because the rule covers the MAUI heads and the
/// Razor components too, which no analyzer test project builds.</para>
/// </summary>
public partial class ClockReadTests
{
    /// <summary>
    /// The files that still read <c>DateTime.Today</c> or <c>DateTimeOffset.Now</c> /
    /// <c>UtcNow</c> directly, each with the reason it cannot reasonably take a
    /// <see cref="TimeProvider"/> yet. Moving one onto an injected clock takes it off this
    /// list; nothing is ever added to it.
    /// </summary>
    private static readonly string[] PredatesTheRule =
    [
        // An Android Service the OS builds, compiled only under the Android workload the slnf leaves out.
        "src/App/Backlog.Mobile/Platforms/Android/RecordingService.cs",
        // Aggregate stamped by 3 Touch() calls; a clock through every mutator is a domain redesign.
        "src/Modules/Inbox/Backlog.Modules.Inbox/DomainModels/InboxGroup.cs",
        // Aggregate stamped by 11 Touch() calls; a clock through every mutator is a domain redesign.
        "src/Modules/Inbox/Backlog.Modules.Inbox/DomainModels/InboxItem.cs",
        // Aggregate stamped by 4 Touch() calls; a clock through every mutator is a domain redesign.
        "src/Modules/Inbox/Backlog.Modules.Inbox/DomainModels/InboxList.cs",
        // Aggregate stamped by 40 Touch() calls and ~250 test constructions: a domain redesign.
        "src/Modules/Tasks/Backlog.Modules.Tasks/DomainModels/TaskItem.cs",
    ];

    [Fact]
    public void No_source_file_reads_DateTime_Now_or_UtcNow()
    {
        var offenders = Reads(DateTimeNowRead()).ToList();

        Assert.True(
            offenders.Count == 0,
            "These lines read the static clock, which app-defaults ADR 0005 rules out. Inject a "
            + "TimeProvider and read GetUtcNow() or GetLocalNow() from it; a test passes a FakeTimeProvider:\n"
            + string.Join('\n', offenders));
    }

    [Fact]
    public void No_new_file_reads_the_clock_directly()
    {
        var offenders = Reads(AnyClockRead())
            .Where(read => !PredatesTheRule.Contains(read.File, StringComparer.Ordinal))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These lines read DateTime.Today or DateTimeOffset.Now/UtcNow directly. Inject a "
            + "TimeProvider instead (app-defaults ADR 0005); the predating list only shrinks:\n"
            + string.Join('\n', offenders));
    }

    /// <summary>An entry that no longer reads the clock is one that was fixed: it comes off the
    /// list in the same change, or the list stops saying what is left.</summary>
    [Fact]
    public void Every_predating_file_still_reads_the_clock()
    {
        var reading = Reads(AnyClockRead()).Select(read => read.File).ToHashSet(StringComparer.Ordinal);

        var stale = PredatesTheRule.Where(file => !reading.Contains(file)).ToList();

        Assert.True(
            stale.Count == 0,
            "These files no longer read the clock directly; take them off PredatesTheRule: "
            + string.Join(", ", stale));
    }

    private static IEnumerable<Read> Reads(Regex pattern)
    {
        var src = new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src"));
        var files = Repository.SourceFilesUnder(src, ".cs", ".razor").ToList();

        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file.FullName);

            for (var index = 0; index < lines.Length; index++)
            {
                if (pattern.IsMatch(CodeOf(lines[index])))
                {
                    yield return new Read(Repository.RelativePath(file), index + 1);
                }
            }
        }
    }

    /// <summary>The line with a trailing <c>//</c> comment cut off, so a comment that names
    /// the clock is not a read of it.</summary>
    private static string CodeOf(string line)
    {
        var comment = line.IndexOf("//", StringComparison.Ordinal);
        return comment < 0 ? line : line[..comment];
    }

    private sealed record Read(string File, int Line)
    {
        public override string ToString() => $"{File}:{Line}";
    }

    [GeneratedRegex(@"\bDateTime\.(?:Now|UtcNow)\b")]
    private static partial Regex DateTimeNowRead();

    [GeneratedRegex(@"\bDateTime(?:Offset)?\.(?:Now|UtcNow|Today)\b")]
    private static partial Regex AnyClockRead();
}
