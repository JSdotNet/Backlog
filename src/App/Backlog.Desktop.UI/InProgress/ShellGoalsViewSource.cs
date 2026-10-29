using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Progress.UI.Goals;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Desktop.UI.InProgress;

/// <summary>
/// The window's answer to the Goals view's <see cref="IGoalsViewSource"/>: the window's
/// task list and the pull requests its <see cref="WorkInProgressReader"/> found, as
/// GoalsCandidates, read when the view opens and on Refresh.
/// <para>
/// One per window, with the window's view state. It holds the window's reader, which a
/// task's side panel shares: the main window's shell takes a fresh one each time it
/// mounts (<see cref="Renew"/>), so a trip to Settings and back reads again, as it did
/// while the shell built its own.
/// </para>
/// </summary>
public sealed class ShellGoalsViewSource : IGoalsViewSource, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly TasksDesktopState _state;
    private readonly IAppFeatureSettings? _features;
    private WorkInProgressReader _reader;

    public ShellGoalsViewSource(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
        _state = (TasksDesktopState)(services.GetService(typeof(TasksDesktopState))
            ?? throw new InvalidOperationException("The Goals view reads the window's task list; register TasksDesktopState."));
        _features = services.GetService(typeof(IAppFeatureSettings)) as IAppFeatureSettings;
        _reader = WorkInProgressReader.From(services);
        _reader.Changed += OnChanged;
        _state.Changed += OnChanged;
    }

    public event Action? Changed;

    /// <summary>The window's reader of sessions and open pull requests.</summary>
    public WorkInProgressReader Reader => _reader;

    /// <summary>A fresh reader in place of the one held, for a shell that just mounted.</summary>
    public WorkInProgressReader Renew()
    {
        _reader.Changed -= OnChanged;
        _reader = WorkInProgressReader.From(_services);
        _reader.Changed += OnChanged;
        return _reader;
    }

    public GoalsCandidates Candidates() => GoalsCandidateSource.Of(_state, _reader);

    public Task ReadAsync(bool refresh)
    {
        // Whether pull requests are read at all is the Pull requests switch.
        _reader.ReadsPullRequests = _features?.IsEnabled(SessionFeatures.PullRequests) ?? true;
        _ = refresh ? _reader.ReadAsync() : _reader.EnsureAllAsync();
        return Task.CompletedTask;
    }

    private void OnChanged() => Changed?.Invoke();

    public void Dispose()
    {
        _reader.Changed -= OnChanged;
        _state.Changed -= OnChanged;
    }
}
