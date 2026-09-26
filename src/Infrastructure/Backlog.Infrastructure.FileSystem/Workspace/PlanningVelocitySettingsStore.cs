using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backlog.Modules.Roadmap.Abstractions;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// How many story points the reader gets through in a week, as a small JSON file
/// beside the working week — <see cref="WorkingHoursSettingsStore"/>'s shape, for
/// the same reason: it is the person's own pace, not the workspace's, and the
/// roadmap has to be able to read it before anything has asked for it.
/// <para>
/// Its own file rather than a section of <c>settings.json</c>, because that file
/// is the pointer to the workspace and a pointer rewritten every time somebody
/// nudges their reading pace is a pointer rewritten far more often than it moves.
/// A file that is not there reads as <see cref="Default"/>, so the setting is
/// additive in the sense ADR 0006 means it: an install that predates it needs no
/// migration, and one that has never opened the field has nothing written.
/// </para>
/// <para>
/// This is a <em>reading preference</em> (ADR 0013, ruling 4). It decides how long
/// an imported plan's bar is drawn when the plan states no due date — gathered
/// effort ÷ this, in calendar days, rounded up — and it registers no estimate
/// against anything. A change is re-drawn by the roadmap, which re-lengthens every
/// window the importer still owns (ADR 0013, ruling 5 as amended).
/// </para>
/// <para>
/// A file from before the pace was a week holds <c>storyPointsPerDay</c> and no
/// <c>storyPointsPerWeek</c>. It reads as seven times that, which draws every bar
/// exactly as long as it was, and is written in the week's spelling at the next
/// change.
/// </para>
/// <para>
/// It also keeps which pace the roadmap places by (<see cref="Source"/>): the typed
/// one, or one the roadmap measures from finished work. A file written before that
/// choice existed has no <c>source</c> and reads as <see cref="PaceSource.Manual"/>,
/// which is what it meant.
/// </para>
/// <para>
/// Both are kept per repository too, under <c>repositories</c>, keyed by the alias
/// Settings gives the repository, because productivity differs from one project to
/// the next (ADR 0013, ruling 4 as amended on 2026-09-26). A repository with no entry
/// reads the global pace and choice, so a file written before there were entries —
/// which has no <c>repositories</c> at all — reads exactly as it did, and nobody
/// sees a bar move until they set a pace for one repository. The first change made
/// for a repository writes its entry, and copies the half not being changed from
/// what the repository read at that moment: choosing a source for it keeps the
/// typed pace the reader was looking at. Aliases are compared without regard to
/// case and written lower-cased, the way Settings keeps them.
/// </para>
/// </summary>
public sealed class PlanningVelocitySettingsStore
{
    /// <summary>Seven story points a week — one a calendar day, what the roadmap
    /// divided by when the pace was a day, so a reader who never set one sees no bar
    /// change length. It is deliberately not zero: a velocity of
    /// zero has no length to give, and a default nobody chose should place a plan
    /// rather than refuse to.</summary>
    public const decimal Default = 7m;

    /// <summary>The finest pace the file and the field can hold, and therefore the
    /// smallest one the store will accept. <see cref="Format"/> keeps four decimals,
    /// so anything under this would be written as <c>0</c> — a value this same store
    /// then refuses on the next read, silently putting the reader back on the
    /// default. It is refused on the way in instead, where there is somebody to tell.
    /// </summary>
    public const decimal Smallest = 0.0001m;

    /// <summary>
    /// What a pace may be spelled as: a sign, a dot, and surrounding space.
    /// <para>
    /// Deliberately <em>not</em> <see cref="NumberStyles.Number"/>, which allows
    /// group separators: under it the invariant parser reads <c>2,5</c> as
    /// <c>25</c>, so a reader whose habits put a comma where the dot goes would get
    /// a pace ten times the one they meant, silently — every imported plan a tenth
    /// the length, and nothing on screen looking wrong. A comma is refused instead,
    /// and the message says what to type.
    /// </para>
    /// <para>
    /// A leading sign is allowed so that <c>-2</c> reaches the range check and is
    /// refused for being negative, rather than being refused for not being a
    /// number — which is true but is not the reader's mistake.
    /// </para>
    /// </summary>
    private const NumberStyles PaceStyles =
        NumberStyles.AllowLeadingWhite
        | NumberStyles.AllowTrailingWhite
        | NumberStyles.AllowLeadingSign
        | NumberStyles.AllowDecimalPoint;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    /// <summary>
    /// The published figure, held behind a reference so that replacing it is a
    /// single atomic store — the guarantee both sibling stores get for free by
    /// publishing an immutable object. A bare <see cref="decimal"/> field would not
    /// have it: it is sixteen bytes, .NET promises no atomicity for it, and this
    /// value is read through a singleton port from whatever thread a placement runs
    /// on. A torn read of a divisor is a wrong bar length with nothing on screen to
    /// say so, which is the one failure this setting exists to avoid.
    /// </summary>
    private Pace _pace;

    public PlanningVelocitySettingsStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Backlog",
                "planning-velocity.json"))
    {
    }

    /// <summary>Names the settings file separately from the per-user location.
    /// Public rather than internal because it is the only way to give a test — or
    /// the web harness, which scopes its settings to its content root — a store
    /// that does not fight over the real per-user file.</summary>
    public PlanningVelocitySettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;

        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _pace = Read();
    }

    public event Action? Changed;

    /// <summary>The global pace. Always a positive number, and always one
    /// <see cref="Format"/> can write without losing it: anything else was refused on
    /// the way in, and a file holding anything else reads as <see cref="Default"/>.</summary>
    public decimal StoryPointsPerWeek => _pace.StoryPointsPerWeek;

    /// <summary>Which pace the roadmap places by, globally. Only ever a defined
    /// member: an unknown name in the file reads as <see cref="PaceSource.Manual"/>.</summary>
    public PaceSource Source => _pace.Source;

    /// <summary>The typed pace for <paramref name="repository"/>, or the global one
    /// for <c>null</c> and for a repository that has none of its own.</summary>
    public decimal StoryPointsPerWeekFor(string? repository) => Effective(_pace, repository).StoryPointsPerWeek;

    /// <summary>The chosen pace for <paramref name="repository"/>, or the global one
    /// for <c>null</c> and for a repository that has none of its own.</summary>
    public PaceSource SourceFor(string? repository) => Effective(_pace, repository).Source;

    /// <summary>Whether <paramref name="repository"/> keeps a pace of its own, rather
    /// than reading the global one.</summary>
    public bool KeepsOwnPace(string? repository) =>
        Key(repository) is { } key && _pace.Repositories.ContainsKey(key);

    public string SettingsPath => _path;

    /// <summary>
    /// Sets the pace — for <paramref name="repository"/>, or globally for <c>null</c>.
    /// Returns <c>null</c> when it took and was saved, and a message to put beside the
    /// field otherwise — a refusal when the number is not one the setting can hold, in
    /// which case nothing changes, or a warning when it took but could not be written
    /// for next time.
    /// </summary>
    public string? Set(decimal storyPointsPerWeek, string? repository = null)
    {
        if (storyPointsPerWeek <= 0)
        {
            return "Give a pace above zero — a week that gets through no points has no length to draw.";
        }

        // Rounded to what is actually storable before the value is published, so the
        // figure the roadmap divides by is the same one the file will hold. Rounding
        // at the write instead would let the store keep a pace its own file cannot
        // express, and lose it at the next start.
        var storable = Normalize(storyPointsPerWeek);

        if (storable < Smallest)
        {
            return FormattableString.Invariant(
                $"Give a pace of at least {Smallest} - anything finer than that rounds away to nothing.");
        }

        // Unchanged is nothing to write — and, for a repository still reading the
        // global pace, no reason to give it one of its own.
        var current = Effective(_pace, repository);
        if (current.StoryPointsPerWeek == storable) return null;

        return Save(With(_pace, repository, current with { StoryPointsPerWeek = storable }));
    }

    /// <summary>Chooses the pace the roadmap places by — for
    /// <paramref name="repository"/>, or globally for <c>null</c>. Returns
    /// <c>null</c> when it took and was saved, a refusal for a value that is not a
    /// pace source, and a warning when it took but could not be written for next
    /// time.</summary>
    public string? Choose(PaceSource source, string? repository = null)
    {
        if (!Enum.IsDefined(source)) return "That is not a pace the roadmap offers.";

        var current = Effective(_pace, repository);
        if (current.Source == source) return null;

        return Save(With(_pace, repository, current with { Source = source }));
    }

    /// <summary>The same setter over what a text field hands back, so the screen
    /// does not carry a second copy of the parsing rule. Invariant on purpose: the
    /// <c>number</c> input reports its value with a dot whatever the machine's
    /// locale is.</summary>
    public string? Set(string? typed, string? repository = null)
    {
        if (!decimal.TryParse(
                typed,
                PaceStyles,
                CultureInfo.InvariantCulture,
                out var storyPointsPerWeek))
        {
            return "Give a pace as a number, like 5 or 7.5.";
        }

        return Set(storyPointsPerWeek, repository);
    }

    private string? Save(Pace pace)
    {
        _pace = pace;

        string? error = null;

        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(new PlanningVelocityDto
            {
                StoryPointsPerWeek = pace.StoryPointsPerWeek,
                Source = pace.Source.ToString(),
                // Left out while no repository has a pace of its own, so a reader who
                // never set one keeps the file's old shape.
                Repositories = pace.Repositories.Count == 0
                    ? null
                    : pace.Repositories.ToDictionary(
                        entry => entry.Key,
                        entry => (RepositoryPaceDto?)new RepositoryPaceDto
                        {
                            StoryPointsPerWeek = entry.Value.StoryPointsPerWeek,
                            Source = entry.Value.Source?.ToString()
                        },
                        StringComparer.Ordinal)
            }, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "Changed, but the pace couldn't be saved for next time.";
        }

        Changed?.Invoke();

        return error;
    }

    /// <summary>What the field shows — four decimals, no trailing zeroes, invariant,
    /// so a value committed and re-read is spelled the way it was stored.</summary>
    public static string Format(decimal storyPointsPerWeek) =>
        storyPointsPerWeek.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>Puts a pace through <see cref="Format"/> and back, which both caps it
    /// at four decimals and drops the trailing zeroes a decimal carries in its scale,
    /// so <c>2.50</c> and <c>2.5</c> become one value rather than two.</summary>
    private static decimal Normalize(decimal storyPointsPerWeek) =>
        decimal.Parse(Format(storyPointsPerWeek), PaceStyles, CultureInfo.InvariantCulture);

    /// <summary>The key a repository is kept under — trimmed and lower-cased, the way
    /// Settings keeps an alias — or <c>null</c> for the global pace.</summary>
    private static string? Key(string? repository) =>
        string.IsNullOrWhiteSpace(repository) ? null : repository.Trim().ToLowerInvariant();

    /// <summary>What <paramref name="repository"/> reads: its own entry, each half of
    /// it falling back to the global one where it holds none.</summary>
    private static Setting Effective(Pace pace, string? repository)
    {
        var global = new Setting(pace.StoryPointsPerWeek, pace.Source);

        return Key(repository) is { } key && pace.Repositories.TryGetValue(key, out var own)
            ? new Setting(own.StoryPointsPerWeek ?? global.StoryPointsPerWeek, own.Source ?? global.Source)
            : global;
    }

    /// <summary>The published figure with <paramref name="setting"/> as the global pace
    /// or as <paramref name="repository"/>'s — a new object, never an edit, so a reader
    /// holding the old one still sees the whole of it.</summary>
    private static Pace With(Pace pace, string? repository, Setting setting)
    {
        if (Key(repository) is not { } key)
        {
            return pace with { StoryPointsPerWeek = setting.StoryPointsPerWeek, Source = setting.Source };
        }

        var repositories = new Dictionary<string, RepositoryPace>(pace.Repositories, StringComparer.OrdinalIgnoreCase)
        {
            [key] = new RepositoryPace(setting.StoryPointsPerWeek, setting.Source)
        };

        return pace with { Repositories = repositories };
    }

    /// <summary>A pace as the file spells it, or <c>null</c> for one the setting
    /// cannot hold.</summary>
    private static decimal? PaceOf(decimal? perWeek) =>
        perWeek is { } typed && typed >= Smallest ? Normalize(typed) : null;

    /// <summary>A source as the file spells it, or <c>null</c> for one it does not
    /// name. Parsed by name and checked against the members, so a number spelled in
    /// the file or a name from a later version is not taken for a choice.</summary>
    private static PaceSource? SourceOf(string? spelled) =>
        Enum.TryParse<PaceSource>(spelled, ignoreCase: true, out var chosen)
        && Enum.IsDefined(chosen)
        && !int.TryParse(spelled, out _)
            ? chosen
            : null;

    /// <summary>
    /// A missing file, an unreadable one, a value that is not a number, and a number
    /// that is not positive all read as <see cref="Default"/>. A corrupt or
    /// unreachable preference must never stop the app from opening, and a velocity
    /// the roadmap cannot divide by is worse than one nobody chose. The pace and the
    /// source are read independently: a bad one does not cost the reader the other.
    /// </summary>
    private Pace Read()
    {
        try
        {
            if (!File.Exists(_path)) return Untouched();

            var dto = JsonSerializer.Deserialize<PlanningVelocityDto>(File.ReadAllText(_path), JsonOptions);

            // The week's spelling wins; a file only ever written in the day's is that
            // figure over seven days.
            var storyPointsPerWeek = PaceOf(dto?.StoryPointsPerWeek ?? dto?.StoryPointsPerDay * 7) ?? Default;

            // A number spelled in the file or a name from a later version reads as the
            // typed pace.
            var source = SourceOf(dto?.Source) ?? PaceSource.Manual;

            return new Pace(storyPointsPerWeek, source, RepositoriesOf(dto?.Repositories));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Untouched();
        }
    }

    /// <summary>
    /// The repositories' own paces, as the file holds them. A half that is missing or
    /// cannot be read is left empty, so it reads the global one; an entry left with
    /// nothing of its own, or under a blank alias, is dropped, since it would read
    /// exactly as no entry does. Two keys differing only in case are one repository,
    /// and the first wins.
    /// </summary>
    private static Dictionary<string, RepositoryPace> RepositoriesOf(Dictionary<string, RepositoryPaceDto?>? written)
    {
        var repositories = new Dictionary<string, RepositoryPace>(StringComparer.OrdinalIgnoreCase);

        foreach (var (alias, entry) in written ?? [])
        {
            if (Key(alias) is not { } key || entry is null) continue;

            var own = new RepositoryPace(PaceOf(entry.StoryPointsPerWeek), SourceOf(entry.Source));
            if (own.StoryPointsPerWeek is null && own.Source is null) continue;

            repositories.TryAdd(key, own);
        }

        return repositories;
    }

    private static Pace Untouched() =>
        new(Default, PaceSource.Manual, new Dictionary<string, RepositoryPace>(StringComparer.OrdinalIgnoreCase));

    /// <summary>The published figure, as one object so that swapping it is atomic.
    /// See <see cref="_pace"/>. <see cref="Pace.Repositories"/> is never edited once
    /// published: a change builds a new dictionary.</summary>
    private sealed record Pace(
        decimal StoryPointsPerWeek,
        PaceSource Source,
        IReadOnlyDictionary<string, RepositoryPace> Repositories);

    /// <summary>A repository's own pace. A half that is <c>null</c> reads the global
    /// one — only ever the case for a hand-edited file, since a change writes both.</summary>
    private sealed record RepositoryPace(decimal? StoryPointsPerWeek, PaceSource? Source);

    /// <summary>A pace and a choice with nothing left to inherit: what a scope
    /// reads.</summary>
    private sealed record Setting(decimal StoryPointsPerWeek, PaceSource Source);

    /// <summary>
    /// Written as a JSON number, which is culture-free by the format's own rules —
    /// a dot, on every machine. Read as one too, and <see cref="JsonNumberHandling.AllowReadingFromString"/>
    /// also accepts it quoted, because this file is meant to be hand-editable and
    /// <c>"2.5"</c> is as reasonable a thing to type as <c>2.5</c>. A quoted
    /// <c>"2,5"</c> is still refused — it is not a JSON number in either spelling —
    /// and lands on the default rather than being read as <c>25</c>.
    /// </summary>
    private sealed class PlanningVelocityDto
    {
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? StoryPointsPerWeek { get; init; }

        /// <summary>The pace as it was kept before it was a week. Read, never
        /// written: nothing sets it, and a null is left out of the file.</summary>
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? StoryPointsPerDay { get; init; }

        /// <summary>A <see cref="PaceSource"/> name. A string rather than the enum,
        /// so a name this build does not know reads as the typed pace instead of
        /// failing the whole file and losing the pace with it.</summary>
        public string? Source { get; init; }

        /// <summary>Per repository alias, its own pace. Absent from a file written
        /// before repositories had one, and from any file where none has.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, RepositoryPaceDto?>? Repositories { get; init; }
    }

    /// <summary>One repository's entry, spelled as the global pace is and read as
    /// forgivingly.</summary>
    private sealed class RepositoryPaceDto
    {
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public decimal? StoryPointsPerWeek { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Source { get; init; }
    }
}
