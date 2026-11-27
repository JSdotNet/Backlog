using Backlog.Modules.Repositories.Abstractions.Environments;
using Backlog.Modules.Repositories.Abstractions.Services;

namespace Backlog.Modules.Repositories.Services;

/// <summary>
/// <see cref="IEnvironments"/> over the whole list an <see cref="IEnvironmentStore"/>
/// keeps. The rules are here and nowhere else: a home per environment, a name and one
/// launch link for life, and arranging edits that change only the arrangement.
/// </summary>
internal sealed class Environments : IEnvironments
{
    public const string NameRequired = "Give the environment a name.";
    public const string LinkRequired = "Give the environment a launch link.";
    public const string UrlNotWebAddress = "A URL launch link has to be an http or https address.";
    public const string NotFound = "That environment is no longer there.";
    public const string CannotMove = "That environment can't move further.";

    private readonly IEnvironmentStore _store;
    private readonly Lock _gate = new();
    private List<StoredEnvironment> _environments;

    /// <summary>The store could not be read when the module opened it: every edit is
    /// refused until a later read succeeds, so nothing overwrites a file that is there
    /// and only unreadable.</summary>
    private bool _unreadable;

    public Environments(IEnvironmentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        var read = store.Load();
        _unreadable = read is null;
        _environments = [.. read ?? []];
        store.Changed += OnStoreChanged;
    }

    public event Action? Changed;

    public IReadOnlyList<RepositoryEnvironment> For(EnvironmentHome home, bool includeHidden = false)
    {
        lock (_gate)
        {
            var own = _environments.Where(stored => stored.Home.Is(home)).Select(stored => stored.Environment);
            if (!includeHidden) own = own.Where(environment => !environment.Hidden);

            // A stable sort: pinned first, each group keeping its order.
            return [.. own.OrderBy(environment => environment.Pinned ? 0 : 1)];
        }
    }

    public int HiddenCount(EnvironmentHome home)
    {
        lock (_gate) return _environments.Count(stored => stored.Home.Is(home) && stored.Environment.Hidden);
    }

    public string? Add(EnvironmentHome home, string name, EnvironmentType type, LaunchLink link, IEnumerable<string>? accessHints = null)
    {
        ArgumentNullException.ThrowIfNull(link);

        var trimmedName = name?.Trim() ?? string.Empty;
        var value = link.Value?.Trim() ?? string.Empty;

        if (trimmedName.Length == 0) return NameRequired;
        if (value.Length == 0) return LinkRequired;
        if (link.Kind == LaunchLinkKind.Url && !EnvironmentLaunch.IsWebAddress(value)) return UrlNotWebAddress;

        var hints = (accessHints ?? [])
            .Select(hint => hint?.Trim() ?? string.Empty)
            .Where(hint => hint.Length > 0)
            .Select(hint => new AccessHint(hint))
            .ToList();

        var environment = new RepositoryEnvironment(Guid.NewGuid(), trimmedName, type, link with { Value = value }, hints);

        return Edit(list => list.Add(new StoredEnvironment(home, environment)));
    }

    public string? SetPinned(EnvironmentHome home, Guid id, bool pinned) =>
        Arrange(home, id, environment => environment with { Pinned = pinned });

    public string? SetHidden(EnvironmentHome home, Guid id, bool hidden) =>
        Arrange(home, id, environment => environment with { Hidden = hidden });

    public string? Move(EnvironmentHome home, Guid id, int offset)
    {
        string? refusal = null;

        var error = Edit(list =>
        {
            var at = IndexOf(list, home, id);
            if (at < 0)
            {
                refusal = NotFound;
                return false;
            }

            // The neighbour is the next environment in the list the person sees: the same
            // home and pin group, and - for one that is shown - not a hidden one. So a
            // move always changes what is on screen, and never leaves the pin group the
            // list sorts it into.
            var moving = list[at].Environment;
            var step = Math.Sign(offset);
            var to = -1;
            for (var index = at + step; step != 0 && index >= 0 && index < list.Count; index += step)
            {
                var candidate = list[index];
                if (!candidate.Home.Is(home) || candidate.Environment.Pinned != moving.Pinned) continue;
                if (candidate.Environment.Hidden && !moving.Hidden) continue;

                to = index;
                break;
            }

            if (to < 0)
            {
                refusal = CannotMove;
                return false;
            }

            // Taken out and put back beside its neighbour, so what lies between them -
            // hidden ones, another home's - keeps its place.
            var stored = list[at];
            list.RemoveAt(at);
            list.Insert(to, stored);
            return true;
        });

        return refusal ?? error;
    }

    public string? Remove(EnvironmentHome home, Guid id)
    {
        string? refusal = null;

        var error = Edit(list =>
        {
            var index = IndexOf(list, home, id);
            if (index < 0)
            {
                refusal = NotFound;
                return false;
            }

            list.RemoveAt(index);
            return true;
        });

        return refusal ?? error;
    }

    private string? Arrange(EnvironmentHome home, Guid id, Func<RepositoryEnvironment, RepositoryEnvironment> arrange)
    {
        string? refusal = null;

        var error = Edit(list =>
        {
            var index = IndexOf(list, home, id);
            if (index < 0)
            {
                refusal = NotFound;
                return false;
            }

            var stored = list[index];
            var arranged = arrange(stored.Environment);

            // Arranging leaves the launch link, the name and the type alone, whatever the
            // arrangement asked for.
            list[index] = stored with
            {
                Environment = stored.Environment with { Pinned = arranged.Pinned, Hidden = arranged.Hidden }
            };
            return true;
        });

        return refusal ?? error;
    }

    private string? Edit(Action<List<StoredEnvironment>> edit) => Edit(list =>
    {
        edit(list);
        return true;
    });

    private string? Edit(Func<List<StoredEnvironment>, bool> edit)
    {
        string? error;

        lock (_gate)
        {
            if (_unreadable) return IEnvironments.StoreUnreadable;

            var list = new List<StoredEnvironment>(_environments);
            if (!edit(list)) return null;

            _environments = list;
            error = _store.Save(list) is null ? null : IEnvironments.KeptNotSaved;
        }

        Changed?.Invoke();
        return error;
    }

    private static int IndexOf(List<StoredEnvironment> list, EnvironmentHome home, Guid id) =>
        list.FindIndex(stored => stored.Home.Is(home) && stored.Environment.Id == id);

    private void OnStoreChanged()
    {
        lock (_gate)
        {
            // A read that fails keeps what is held, an edit whose save failed included:
            // an unreadable file is not an empty one.
            var read = _store.Load();
            if (read is null) return;

            _environments = [.. read];
            _unreadable = false;
        }

        Changed?.Invoke();
    }
}
