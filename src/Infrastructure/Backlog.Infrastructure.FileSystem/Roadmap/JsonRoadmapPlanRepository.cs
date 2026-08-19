using System.Text;
using System.Text.Json;

using Backlog.Modules.Roadmap;
using Backlog.Modules.Roadmap.DomainModels;

namespace Backlog.Infrastructure.FileSystem.Roadmap;

/// <summary>
/// Local-first <see cref="IRoadmapPlanRepository"/> that keeps the whole plan in one
/// JSON document under the storage root: <c>_roadmap/plan.json</c>. Fully offline;
/// no cloud dependency.
/// <para>
/// The plan lives beside <c>_backlog</c> and <c>_inbox</c> in the same folder,
/// rather than in a fixed per-user location, so pointing the app at a different
/// storage folder takes the plan with it. That is the whole reason the folder is a
/// setting.
/// </para>
/// <para>
/// One document, so the write has to be atomic: the plan is serialized to a
/// temporary file in the same folder and then moved over the previous one. A
/// same-volume move is a single filesystem operation, so a reader sees the old plan
/// or the new one and never a half-written file — which matters far more here than
/// for a per-item store, where a torn write would cost one entry instead of
/// everything.
/// </para>
/// </summary>
public sealed class JsonRoadmapPlanRepository : IRoadmapPlanRepository
{
    /// <summary>The folder inside the storage root. Underscore-prefixed to sort
    /// with <c>_backlog</c> and <c>_inbox</c> rather than among a person's own
    /// folders.</summary>
    public const string RoadmapFolderName = "_roadmap";

    public const string PlanFileName = "plan.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // A plan is meant to be openable in an editor. Dropping nulls keeps an item
        // that named no lane, no entry and no notes down to the four lines that
        // actually say something.
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// UTF-8 with no byte-order mark.
    /// <para>
    /// Explicitly not <see cref="Encoding.UTF8"/>, which emits one. A BOM is
    /// invisible to this app — reading with <see cref="Encoding.UTF8"/> strips it, and
    /// the round trip never noticed — but it is three bytes of garbage in front of the
    /// opening brace as far as everything else is concerned: <c>jq</c>, Python's
    /// <c>json.load</c> and plenty of editors' JSON tooling fail outright on it. This
    /// file is meant to be opened and edited by hand, so it has to be a JSON file
    /// other tools will accept.
    /// </para>
    /// <para>
    /// Only the write side is fixed. Reads stay on <see cref="Encoding.UTF8"/> so a
    /// plan written by an earlier build, or by an editor that adds one, still loads.
    /// </para>
    /// </summary>
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _planPath;

    /// <summary>Creates a repository rooted at the given folder, or the default
    /// per-user app-data folder (<c>%LOCALAPPDATA%\Backlog</c>) when null.</summary>
    public JsonRoadmapPlanRepository(string? rootDir = null)
    {
        RootDirectory = rootDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog");

        _planPath = Path.Combine(RootDirectory, RoadmapFolderName, PlanFileName);
        EnsureRoadmapFolder(RootDirectory);
    }

    /// <summary>The storage root this repository is pointed at.</summary>
    public string RootDirectory { get; }

    /// <summary>Where the plan file itself is written — shown on the settings page
    /// so the folder can be found in a file manager.</summary>
    public string PlanPath => _planPath;

    public static void EnsureRoadmapFolder(string rootDir) =>
        Directory.CreateDirectory(Path.Combine(rootDir, RoadmapFolderName));

    public async Task<RoadmapPlan> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_planPath)) return RoadmapPlan.Empty();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = await File.ReadAllTextAsync(_planPath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json)) return RoadmapPlan.Empty();

            var document = JsonSerializer.Deserialize<RoadmapPlanDocument>(json, JsonOptions);
            return document?.ToPlan() ?? RoadmapPlan.Empty();
        }
        catch (JsonException)
        {
            // A plan file that is not valid JSON must never stop the app from
            // opening. It is deliberately not deleted or rewritten either: an empty
            // plan is shown, the file stays exactly as it is, and whoever broke it
            // while editing can fix it. Overwriting here would turn a typo into
            // data loss.
            return RoadmapPlan.Empty();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(RoadmapPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var json = JsonSerializer.Serialize(RoadmapPlanDocument.From(plan), JsonOptions);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureRoadmapFolder(RootDirectory);

            // Same folder, so the move stays on one volume and stays atomic. A temp
            // file in %TEMP% would degrade to copy-then-delete across volumes, which
            // is exactly the torn write this avoids.
            var temporaryPath = _planPath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, json, Utf8WithoutBom, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, _planPath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
