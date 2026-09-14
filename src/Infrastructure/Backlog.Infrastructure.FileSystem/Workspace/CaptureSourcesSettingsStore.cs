using System.Text.Json;

using Backlog.Modules.Capture.Abstractions;
using Backlog.Modules.Capture.Abstractions.DataTransferObjects;
using Backlog.Modules.Capture.Abstractions.Services;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// Keeps which capture sources are watched, and what they are pointed at, in a
/// JSON file next to the app's other per-user settings.
/// <para>
/// Its own file rather than a section of <c>settings.json</c>, for the reason
/// the refresh, feature and working-week choices each have one: that file is
/// the pointer to the workspace, and a pointer that has to be rewritten to add
/// a YouTube channel is a pointer rewritten far more often than it is moved.
/// </para>
/// <para>
/// Targets are URLs and identifiers only — a channel, a page, a sender. Never a
/// credential: this file is plain text meant to be read and hand-edited, and a
/// mailbox password does not belong in it. Kinds are written as the domain's
/// slugs (<c>youtube</c>, not <c>1</c>) for the same reason.
/// </para>
/// </summary>
public sealed class CaptureSourcesSettingsStore : ICaptureSourceSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public CaptureSourcesSettingsStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Backlog",
                "capture-sources.json"))
    {
    }

    /// <summary>Names the settings file separately from the per-user location.
    /// Public rather than internal because it is the only way to give a test — or
    /// the web harness, which scopes its settings to its content root — a store
    /// that does not fight over the real per-user file.</summary>
    public CaptureSourcesSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Current = Read();
    }

    public event Action? Changed;

    public CaptureSourceSettings Current { get; private set; }

    public string SettingsPath => _path;

    public string? SetEnabled(CaptureSourceKind kind, bool enabled)
    {
        var stored = Current.For(kind);
        if (stored.Enabled == enabled) return null;

        return Save(With(kind, stored with { Enabled = enabled }));
    }

    public string? SetTargets(CaptureSourceKind kind, IReadOnlyList<string> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        var stored = Current.For(kind);
        var cleaned = CleanTargets(targets);
        if (cleaned.SequenceEqual(stored.Targets, StringComparer.Ordinal)) return null;

        return Save(With(kind, stored with { Targets = cleaned }));
    }

    private CaptureSourceSettings With(CaptureSourceKind kind, MonitoredSource replacement) =>
        Current with
        {
            Sources = [.. Current.Sources.Select(source => source.Kind == kind ? replacement : source)]
        };

    private string? Save(CaptureSourceSettings settings)
    {
        Current = Normalize(settings);

        string? error = null;
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(new CaptureSourcesDto
            {
                Sources = [.. Current.Sources.Select(source => new MonitoredSourceDto
                {
                    Kind = CaptureSourceKinds.Slug(source.Kind),
                    Enabled = source.Enabled,
                    Targets = [.. source.Targets]
                })]
            }, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "Changed, but the capture sources couldn't be saved for next time.";
        }

        Changed?.Invoke();
        return error;
    }

    private CaptureSourceSettings Read()
    {
        try
        {
            if (!File.Exists(_path)) return new CaptureSourceSettings();

            var dto = JsonSerializer.Deserialize<CaptureSourcesDto>(File.ReadAllText(_path), JsonOptions);
            if (dto is null) return new CaptureSourceSettings();

            return Normalize(new CaptureSourceSettings
            {
                Sources = [.. dto.Sources.Select(ReadSource).OfType<MonitoredSource>()]
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreachable setting must never stop the app from
            // opening — fall back to nothing watched.
            return new CaptureSourceSettings();
        }
    }

    /// <summary>A kind nobody can read is dropped rather than guessed at;
    /// <see cref="Normalize"/> then fills the source back in switched off, which
    /// is what a missing line means too.</summary>
    private static MonitoredSource? ReadSource(MonitoredSourceDto dto) =>
        CaptureSourceKinds.TryParse(dto.Kind, out var kind)
            ? new MonitoredSource(kind, dto.Enabled, CleanTargets(dto.Targets))
            : null;

    /// <summary>
    /// Makes the set whole and puts it in the vocabulary's order: exactly one
    /// entry per monitorable kind, anything absent switched off, any duplicate
    /// resolved by keeping the first, and any kind that cannot be monitored
    /// dropped — a hand-edit that watches <c>manual</c> is watching nothing.
    /// </summary>
    private static CaptureSourceSettings Normalize(CaptureSourceSettings settings) =>
        settings with
        {
            Sources = [.. CaptureSourceKinds.Monitorable.Select(kind =>
                settings.Sources.FirstOrDefault(source => source.Kind == kind)
                ?? new MonitoredSource(kind, Enabled: false, Targets: []))]
        };

    /// <summary>Trimmed, blanks dropped, duplicates folded. What is stored is
    /// what a run will look at, so a blank line in the box is not a target.</summary>
    private static IReadOnlyList<string> CleanTargets(IEnumerable<string> targets) =>
        [.. targets
            .Select(target => target?.Trim() ?? string.Empty)
            .Where(target => target.Length > 0)
            .Distinct(StringComparer.Ordinal)];

    private sealed class CaptureSourcesDto
    {
        public List<MonitoredSourceDto> Sources { get; init; } = [];
    }

    private sealed class MonitoredSourceDto
    {
        public string Kind { get; init; } = string.Empty;

        public bool Enabled { get; init; }

        public List<string> Targets { get; init; } = [];
    }
}
