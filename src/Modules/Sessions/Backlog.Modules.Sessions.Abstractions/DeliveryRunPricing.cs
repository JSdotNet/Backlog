namespace Backlog.Modules.Sessions.Abstractions;

/// <summary>
/// Prices a run that is not the one on screen — an earlier run a baseline is taken over,
/// or every run the Delivery economics page compares — the way its own row would be:
/// its sessions' stored requests where they answer, else its tokens at the person's
/// prices (<see cref="DeliveryRunCosts.Of(DeliveryRun, IReadOnlyList{ClaudeApiRequest}, ModelPriceTable, string?, string?)"/>).
/// </summary>
public static class DeliveryRunPricing
{
    /// <summary>
    /// What each stage of the run cost, in stage order. A store that fails leaves the run
    /// on its estimate rather than failing the caller: a figure nothing could read is
    /// absent, which is the honest answer, and the page or the pane goes on drawing.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="sessionId">The session the run was joined to — read where the run
    /// names none of its own.</param>
    /// <param name="ownerModel">The owner session's model, for the owner's share of an
    /// estimate.</param>
    /// <param name="store">Where the requests are kept, or null where the host keeps
    /// none.</param>
    /// <param name="prices">The person's rates.</param>
    /// <param name="cancellationToken">Cancels the read; a cancellation is passed on, never
    /// taken for a failed store.</param>
    public static async Task<IReadOnlyList<DeliveryRunStageCost>> PriceAsync(
        DeliveryRun run,
        string? sessionId,
        string? ownerModel,
        IClaudeApiRequestStore? store,
        ModelPriceTable prices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(prices);

        var ids = run.SessionIds.Count > 0 ? run.SessionIds : sessionId is null ? [] : [sessionId];
        var requests = new List<ClaudeApiRequest>();

        if (store is not null)
        {
            try
            {
                foreach (var id in ids.Distinct(StringComparer.Ordinal)) requests.AddRange(await store.ListAsync(id, cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                requests.Clear();
            }
        }

        return DeliveryRunCosts.Of(run, requests, prices, sessionId, ownerModel);
    }
}
