using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// How many story points the reader gets through in a day, as a small JSON file
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
/// effort ÷ this, rounded up — and it registers no estimate against anything. A
/// change here does not move a window already placed; only a re-import does that.
/// </para>
/// </summary>
public sealed class PlanningVelocitySettingsStore
{
    /// <summary>One story point a day — the figure the roadmap divides by when the
    /// reader has never said otherwise. It is deliberately not zero: a velocity of
    /// zero has no length to give, and a default nobody chose should place a plan
    /// rather than refuse to.</summary>
    public const decimal Default = 1m;

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

        _pace = new Pace(Read());
    }

    public event Action? Changed;

    /// <summary>Always a positive number, and always one <see cref="Format"/> can
    /// write without losing it: anything else was refused on the way in, and a file
    /// holding anything else reads as <see cref="Default"/>.</summary>
    public decimal StoryPointsPerDay => _pace.StoryPointsPerDay;

    public string SettingsPath => _path;

    /// <summary>
    /// Sets the pace. Returns <c>null</c> when it took and was saved, and a message
    /// to put beside the field otherwise — a refusal when the number is not one the
    /// setting can hold, in which case nothing changes, or a warning when it took
    /// but could not be written for next time.
    /// </summary>
    public string? Set(decimal storyPointsPerDay)
    {
        if (storyPointsPerDay <= 0)
        {
            return "Give a pace above zero — a day that gets through no points has no length to draw.";
        }

        // Rounded to what is actually storable before the value is published, so the
        // figure the roadmap divides by is the same one the file will hold. Rounding
        // at the write instead would let the store keep a pace its own file cannot
        // express, and lose it at the next start.
        var storable = Normalize(storyPointsPerDay);

        if (storable < Smallest)
        {
            return FormattableString.Invariant(
                $"Give a pace of at least {Smallest} - anything finer than that rounds away to nothing.");
        }

        if (StoryPointsPerDay == storable) return null;

        return Save(storable);
    }

    /// <summary>The same setter over what a text field hands back, so the screen
    /// does not carry a second copy of the parsing rule. Invariant on purpose: the
    /// <c>number</c> input reports its value with a dot whatever the machine's
    /// locale is.</summary>
    public string? Set(string? typed)
    {
        if (!decimal.TryParse(
                typed,
                PaceStyles,
                CultureInfo.InvariantCulture,
                out var storyPointsPerDay))
        {
            return "Give a pace as a number, like 1 or 2.5.";
        }

        return Set(storyPointsPerDay);
    }

    private string? Save(decimal storyPointsPerDay)
    {
        _pace = new Pace(storyPointsPerDay);

        string? error = null;

        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(new PlanningVelocityDto
            {
                StoryPointsPerDay = storyPointsPerDay
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
    public static string Format(decimal storyPointsPerDay) =>
        storyPointsPerDay.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>Puts a pace through <see cref="Format"/> and back, which both caps it
    /// at four decimals and drops the trailing zeroes a decimal carries in its scale,
    /// so <c>2.50</c> and <c>2.5</c> become one value rather than two.</summary>
    private static decimal Normalize(decimal storyPointsPerDay) =>
        decimal.Parse(Format(storyPointsPerDay), PaceStyles, CultureInfo.InvariantCulture);

    /// <summary>
    /// A missing file, an unreadable one, a value that is not a number, and a number
    /// that is not positive all read as <see cref="Default"/>. A corrupt or
    /// unreachable preference must never stop the app from opening, and a velocity
    /// the roadmap cannot divide by is worse than one nobody chose.
    /// </summary>
    private decimal Read()
    {
        try
        {
            if (!File.Exists(_path)) return Default;

            var dto = JsonSerializer.Deserialize<PlanningVelocityDto>(File.ReadAllText(_path), JsonOptions);

            if (dto?.StoryPointsPerDay is not { } storyPointsPerDay || storyPointsPerDay < Smallest)
            {
                return Default;
            }

            return Normalize(storyPointsPerDay);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Default;
        }
    }

    /// <summary>The published figure, as one object so that swapping it is atomic.
    /// See <see cref="_pace"/>.</summary>
    private sealed record Pace(decimal StoryPointsPerDay);

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
        public decimal? StoryPointsPerDay { get; init; }
    }
}
