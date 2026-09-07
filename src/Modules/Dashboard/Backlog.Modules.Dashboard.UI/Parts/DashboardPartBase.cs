using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Insights;
using Backlog.UI.Components.Metrics;
using Microsoft.AspNetCore.Components;

namespace Backlog.Modules.Dashboard.UI.Parts;

/// <summary>
/// What every part of the dashboard has in common: it fetches its own figures,
/// carries its own status, and refreshes on its own.
/// </summary>
/// <remarks>
/// <para>
/// This class is the independence the dashboard promises, in one place. The pane
/// awaits nothing and renders all of its parts immediately; each one starts its
/// fetch here, so a provider that is slow or unconfigured delays and explains
/// itself alone. A base class rather than seven copies of the same lifecycle,
/// because the interesting failure — a part that forgets to leave
/// <see cref="MetricStatusKind.Loading"/> when its source refuses — is the kind
/// that only shows up in the copy nobody re-read.
/// </para>
/// <para>
/// Deliberately no <c>try</c> around the fetch. The module already turns a
/// provider's throw into an unavailable result carrying the reason, so catching
/// here would either duplicate that or hide the one case that should still be
/// loud: this component being wired up wrong.
/// </para>
/// <para>
/// Not one <c>ConfigureAwait(false)</c> anywhere below, and that is deliberate
/// rather than an oversight. A component's continuations have to come back to the
/// renderer's dispatcher, because <see cref="ComponentBase.StateHasChanged"/>
/// asserts it is on that dispatcher and throws otherwise. Configuring the awaits
/// away worked against test doubles that complete synchronously and killed the
/// circuit against a real provider, which is a failure mode worth naming here: the
/// awaits in the adapters and the module below them are a different matter and do
/// configure away, correctly, because nothing there renders.
/// </para>
/// </remarks>
/// <typeparam name="T">What this part renders once it has it.</typeparam>
public abstract class DashboardPartBase<T> : ComponentBase, IDisposable
    where T : class
{
    private CancellationTokenSource? _inFlight;
    private DashboardScope? _fetchedFor;

    /// <summary>What the dashboard is looking at. Parts that cannot narrow by
    /// repository or by machine still receive it — see <see cref="FollowsRepository"/>
    /// and <see cref="FollowsMachine"/>.</summary>
    [Parameter]
    public DashboardScope Scope { get; set; } = DashboardScope.Default;

    /// <summary>Ready, Loading, Empty, or Unavailable — the four states the metric
    /// components already know how to draw.</summary>
    protected MetricStatusKind Status { get; private set; } = MetricStatusKind.Loading;

    /// <summary>The source's own words for why there is nothing to show. Null when
    /// there is something.</summary>
    protected string? StatusMessage { get; private set; }

    /// <summary>The figures, once there are some.</summary>
    protected T? Value { get; private set; }

    /// <summary>True when there is something to draw. Every part's markup branches
    /// on this before touching <see cref="Value"/>.</summary>
    protected bool IsReady => Status == MetricStatusKind.Ready && Value is not null;

    /// <summary>
    /// Whether the repository filter changes this part's answer.
    /// <para>
    /// False for every cost part, because neither provider reports spend per
    /// repository, and false for the sessions part, because Claude records no
    /// repository against a session and filtering on one would silently drop every
    /// Claude session from a part whose whole point is showing both assistants. A part
    /// that returns false ignores that filter and says on screen that it does — the
    /// constraint is stated in both places rather than left for the reader to notice.
    /// </para>
    /// </summary>
    protected virtual bool FollowsRepository => true;

    /// <summary>
    /// Whether the machine filter changes this part's answer.
    /// <para>
    /// False by default, which is the opposite default from
    /// <see cref="FollowsRepository"/> and is the honest one: every part that existed
    /// before the machine filter is GitHub-backed or provider-backed, and neither
    /// GitHub nor a billing report knows which machine a piece of work was done from.
    /// Only the sessions part can answer the question, so only it opts in.
    /// </para>
    /// </summary>
    protected virtual bool FollowsMachine => false;

    /// <summary>
    /// Whether the window changes this part's answer.
    /// <para>
    /// The third dimension of the scope, and the third flag, because a part declares
    /// what it can honour one dimension at a time. False for every cost part:
    /// <c>ICostInsights</c> takes no scope at all — a bill arrives per calendar month
    /// and the two windows on the strip are not months — so a window change cannot move
    /// those figures and must not re-ask the providers for them.
    /// </para>
    /// </summary>
    protected virtual bool FollowsWindow => true;

    /// <summary>Asks this part's question. One call, one part.</summary>
    protected abstract Task<InsightResult<T>> FetchAsync(CancellationToken cancellationToken);

    /// <summary>Whether an answer that arrived has anything in it, so an empty
    /// quarter reads as empty rather than as broken.</summary>
    protected abstract bool IsEmpty(T value);

    /// <summary>
    /// What this part says when the answer arrived with nothing in it.
    /// <para>
    /// Each part supplies its own, because the metric components' default empty line
    /// talks about usage and most of these parts are not about usage. "No usage in this
    /// period" under a rework heading tells a reader nothing about whether their work
    /// came back after review — the sentence has to name the thing that was absent.
    /// </para>
    /// </summary>
    protected virtual string? EmptyMessage => null;

    /// <summary>Drops what the module cached for this part, so a refresh goes back
    /// to the provider.</summary>
    protected abstract void InvalidateSource();

    /// <summary>
    /// The scope as this part sees it: the dimensions it follows kept, the ones it
    /// cannot answer for blanked.
    /// <para>
    /// Comparing this rather than the whole scope is what makes each filter reach
    /// exactly the parts that can honour it. A record's value equality does the rest —
    /// moving the machine filter leaves a productivity part's relevant scope identical,
    /// so it neither re-fetches nor flashes Loading over figures that cannot have
    /// changed.
    /// </para>
    /// </summary>
    private DashboardScope Relevant(DashboardScope scope) => scope with
    {
        RepositoryAlias = FollowsRepository ? scope.RepositoryAlias : null,
        MachineId = FollowsMachine ? scope.MachineId : null,
        Period = FollowsWindow ? scope.Period : DashboardScope.Default.Period
    };

    protected override async Task OnParametersSetAsync()
    {
        // Re-fetch when the part of the scope this part cares about actually changed.
        // Without this check, moving a filter would re-spend the whole call budget on
        // parts that cannot narrow by it, for an answer that cannot have changed.
        var relevant = Relevant(Scope);

        if (_fetchedFor is not null && _fetchedFor == relevant) return;

        _fetchedFor = relevant;

        await LoadAsync();
    }

    /// <summary>Fetches again from the provider. What the part's refresh control does.</summary>
    protected async Task RefreshAsync()
    {
        InvalidateSource();

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        // A filter changed twice in quick succession must not let the first answer
        // arrive after the second and win.
        var previous = _inFlight;
        _inFlight = new CancellationTokenSource();
        if (previous is not null)
        {
            await previous.CancelAsync();
            previous.Dispose();
        }

        var token = _inFlight.Token;

        Status = MetricStatusKind.Loading;
        StatusMessage = null;
        Value = null;
        StateHasChanged();

        InsightResult<T> result;
        try
        {
            result = await FetchAsync(token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested) return;

        if (!result.HasValue)
        {
            Status = MetricStatusKind.Unavailable;
            StatusMessage = string.IsNullOrWhiteSpace(result.Availability.Reason)
                ? "This is not available right now."
                : result.Availability.Reason;
        }
        else if (IsEmpty(result.Value!))
        {
            Status = MetricStatusKind.Empty;
            StatusMessage = EmptyMessage;
            Value = result.Value;
        }
        else
        {
            Status = MetricStatusKind.Ready;
            Value = result.Value;
        }

        StateHasChanged();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;

        _inFlight?.Cancel();
        _inFlight?.Dispose();
        _inFlight = null;
    }
}
