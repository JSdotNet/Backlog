using System.Text.Json;

namespace Backlog.Infrastructure.GitHub;

/// <summary>
/// The pull requests pinned on this device: each stays on the pull requests list —
/// read by number, whatever state it reaches — until it is unpinned.
/// <para>
/// Its own file beside the app's other per-user settings, for the reason
/// <c>ShellNavigationStore</c> has one: a pin is how one reader keeps one thing in
/// front of them on this PC, it changes far more often than anything in
/// <c>github.json</c>, and sync never carries it. Here rather than in the file-system
/// adapter because what it holds is this adapter's <see cref="PullRequestPin"/>, and
/// <see cref="GitHubIntegration.ListPinnedPullRequestsAsync"/> is what reads them.
/// </para>
/// <para>
/// Follows the house rule of no save button: a pin is written as it is made. A file
/// that cannot be read opens as nothing pinned, and a write that fails keeps the pin
/// for this session — losing it costs the next launch the pin, never this one.
/// </para>
/// </summary>
public sealed class PullRequestPinsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private readonly Lock _gate = new();
    private List<PullRequestPin> _pins;

    /// <summary>Where the pins sit when nothing overrides it.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Backlog",
        "pull-request-pins.json");

    public PullRequestPinsStore()
        : this(DefaultPath)
    {
    }

    /// <summary>Names the file separately from the per-user location, so a test — or a
    /// harness running beside the installed app — has pins of its own.</summary>
    public PullRequestPinsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        _pins = Read();
    }

    /// <summary>Raised after a pin is made or taken away.</summary>
    public event Action? Changed;

    /// <summary>Where the pins are written.</summary>
    public string SettingsPath => _path;

    /// <summary>Every pin, in the order they were made.</summary>
    public IReadOnlyList<PullRequestPin> Pins
    {
        get
        {
            lock (_gate) return [.. _pins];
        }
    }

    public bool IsPinned(string repositoryFullName, int number)
    {
        lock (_gate) return _pins.Any(pin => pin.Names(repositoryFullName, number));
    }

    /// <summary>Pins a pull request. Pinning one already pinned changes nothing and
    /// announces nothing.</summary>
    /// <exception cref="ArgumentException">The repository is not an
    /// <c>owner/name</c>, or the number is not a pull request's.</exception>
    public void Pin(string repositoryFullName, int number)
    {
        var pin = Validated(repositoryFullName, number);

        lock (_gate)
        {
            if (_pins.Any(known => known.Names(pin.RepositoryFullName, pin.Number))) return;
            _pins = [.. _pins, pin];
            Save();
        }

        Changed?.Invoke();
    }

    /// <summary>Takes a pin away. Unpinning one that is not pinned changes nothing and
    /// announces nothing.</summary>
    public void Unpin(string repositoryFullName, int number)
    {
        lock (_gate)
        {
            if (!_pins.Any(pin => pin.Names(repositoryFullName, number))) return;
            _pins = [.. _pins.Where(pin => !pin.Names(repositoryFullName, number))];
            Save();
        }

        Changed?.Invoke();
    }

    private static PullRequestPin Validated(string repositoryFullName, int number)
    {
        var repository = repositoryFullName?.Trim() ?? string.Empty;

        if (!NamesARepository(repository))
        {
            throw new ArgumentException($"'{repositoryFullName}' is not an owner/name repository.", nameof(repositoryFullName));
        }

        if (number <= 0)
        {
            throw new ArgumentException($"{number} is not a pull request number.", nameof(number));
        }

        return new PullRequestPin(repository, number);
    }

    /// <summary>An <c>owner/name</c> with neither half blank — the one test a pin is
    /// written under and read back under, so the file never yields a pin the store
    /// would have refused.</summary>
    private static bool NamesARepository(string? repository) =>
        repository?.Split('/') is [var owner, var name] && owner.Trim().Length > 0 && name.Trim().Length > 0;

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            // Written beside the file and moved over it, so a write cut short leaves
            // the previous pins rather than half a file that reads as none.
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new PinsDto
            {
                Pins = [.. _pins.Select(pin => new PinDto { Repository = pin.RepositoryFullName, Number = pin.Number })]
            }, JsonOptions));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The pin holds for this session; only the next launch loses it.
        }
    }

    private List<PullRequestPin> Read()
    {
        try
        {
            if (!File.Exists(_path)) return [];

            var dto = JsonSerializer.Deserialize<PinsDto>(File.ReadAllText(_path), JsonOptions);

            // A row that names no pull request is dropped rather than repaired, the
            // tolerance every per-user file here has.
            return [.. (dto?.Pins ?? [])
                .Where(row => row.Number > 0 && NamesARepository(row.Repository))
                .Select(row => new PullRequestPin(row.Repository!.Trim(), row.Number))
                .DistinctBy(pin => (pin.RepositoryFullName.ToUpperInvariant(), pin.Number))];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreachable file must never stop the app from opening.
            return [];
        }
    }

    private sealed class PinsDto
    {
        public List<PinDto> Pins { get; set; } = [];
    }

    private sealed class PinDto
    {
        public string? Repository { get; set; }
        public int Number { get; set; }
    }
}
