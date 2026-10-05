using System.Collections.Concurrent;

using Backlog.Infrastructure.SpecManager.Api;
using Backlog.Infrastructure.SpecManager.OAuth;
using Backlog.Modules.Tasks.Abstractions.Connectors;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Backlog.Infrastructure.SpecManager;

/// <summary>
/// A spec-manager product's backlog as linked tasks (local ADR 0020). A target is a
/// product slug; signing in is through the person's browser.
/// <para>
/// <b>What a fetch asks for.</b> The first sync of a target reads the backlog
/// without the archive and keeps what is not finished. Every sync after it reads
/// the same list whole — an item missing from it has vanished — keeping the
/// finished items only when they changed since the last sync, and adds the archive's
/// changes since then, each of them Dropped. "Since the last sync" reaches back
/// <see cref="ClockSkewAllowance"/> further, because the last sync's moment is this
/// machine's clock and the change stamps are the server's.
/// </para>
/// <para>
/// <b>The status and label names are cached per product</b> for an hour, and read
/// again sooner when an item names one the cache does not have — a status added
/// since is otherwise an item read as Open under no name.
/// </para>
/// </summary>
internal sealed class SpecManagerConnector : ITaskConnector, ITaskConnectorSignIn
{
    /// <summary>The id every linked task from spec-manager carries. Stored, so it
    /// never changes.</summary>
    public const string ConnectorId = "spec-manager";

    /// <summary>How long a product's statuses and labels are trusted.</summary>
    public static readonly TimeSpan CatalogLifetime = TimeSpan.FromHours(1);

    /// <summary>
    /// How far before the last sync a later fetch reaches back. That moment is read
    /// off this machine's clock and compared with the server's <c>bijgewerktOp</c>,
    /// so a local clock running ahead would leave out an item that finished or was
    /// archived just after the last sync — and the sync would archive its task as
    /// vanished. Fifteen minutes covers a badly kept clock; reaching back too far
    /// only returns an already-closed item once more, which the sync takes in its
    /// stride.
    /// </summary>
    public static readonly TimeSpan ClockSkewAllowance = TimeSpan.FromMinutes(15);

    private readonly SpecManagerClient _client;
    private readonly SpecManagerSignIn _signIn;
    private readonly SpecManagerOptions _options;
    private readonly TimeProvider _time;
    private readonly IConnectedTargets? _targets;
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<string, ProductCatalog> _catalogs = new(StringComparer.OrdinalIgnoreCase);
    private int _namingTried;

    public SpecManagerConnector(
        SpecManagerClient client,
        SpecManagerSignIn signIn,
        IOptions<SpecManagerOptions> options,
        TimeProvider time,
        IConnectedTargets? targets = null,
        ILogger<SpecManagerConnector>? log = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(signIn);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);

        _client = client;
        _signIn = signIn;
        _options = options.Value;
        _time = time;
        _targets = targets;
        _log = log ?? NullLogger<SpecManagerConnector>.Instance;
    }

    /// <summary>
    /// The badge: a boxed glyph, the text icon the shared library already draws
    /// for a system (<c>C4SvgWriter</c>), coloured with <c>color-band-2</c>, the
    /// teal of the design palette's bands — distinct from the primary yellow a
    /// GitHub badge would most likely take.
    /// </summary>
    public TaskConnectorDescriptor Descriptor { get; } = new(ConnectorId, "spec-manager", "▣", "color-band-2");

    /// <summary>Items carry storypoints and say what they wait on. Nothing is
    /// written back yet.</summary>
    public TaskConnectorCapabilities Capabilities { get; } = new(HasEffort: true, HasDependencies: true);

    public TaskConnectorAccount? Account => _signIn.Account;

    public event Action? AccountChanged
    {
        add => _signIn.AccountChanged += value;
        remove => _signIn.AccountChanged -= value;
    }

    public async Task<IReadOnlyList<SourceItem>> FetchAsync(string target, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        if (_signIn.Account is null) throw new SpecManagerSignInRequiredException();

        // The last sync's moment is this machine's clock and bijgewerktOp is the
        // server's; the allowance keeps a fast local clock from skipping an item
        // that closed just after that sync.
        var cutoff = since - ClockSkewAllowance;

        var current = await _client.GetBacklogAsync(target, archived: false, changedSince: null, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<BacklogitemDto> archived = cutoff is { } changedSince
            ? await _client.GetBacklogAsync(target, archived: true, changedSince, cancellationToken).ConfigureAwait(false)
            : [];

        var catalog = await CatalogAsync(target, current.Concat(archived), cancellationToken).ConfigureAwait(false);

        var known = new Dictionary<string, BacklogitemDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in current.Concat(archived)) known.TryAdd(item.Id, item);

        var items = new List<SourceItem>(current.Count + archived.Count);
        foreach (var item in current)
        {
            var mapped = SpecManagerItemMapper.Map(item, catalog, known, _options.Root, target);

            // Open work always; finished work only once it is news. On a first sync
            // nothing finished is: there is no task for it to close.
            if (mapped.State == NormalisedSourceState.Done && !(cutoff is { } changed && item.BijgewerktOp >= changed)) continue;

            items.Add(mapped);
        }

        items.AddRange(archived.Select(item => SpecManagerItemMapper.Map(item with { IsGearchiveerd = true }, catalog, known, _options.Root, target)));

        await NameAccountOnceAsync(target, cancellationToken).ConfigureAwait(false);

        return items;
    }

    /// <summary>
    /// The signed-in member's id in the first enabled spec-manager target — the
    /// same id an item's assignee is — or null when nobody is signed in, nothing is
    /// connected, or the installation does not answer who that is.
    /// </summary>
    public async Task<string?> WhoAmIAsync(CancellationToken cancellationToken)
    {
        if (_signIn.Account is null || FirstTarget() is not { } target) return null;

        var members = await _client.GetMembersAsync(target.Target, cancellationToken).ConfigureAwait(false);
        return members?.FirstOrDefault(member => member.IsJij)?.GebruikerId;
    }

    public async Task<string?> SignInAsync(CancellationToken cancellationToken)
    {
        var error = await _signIn.SignInAsync(cancellationToken).ConfigureAwait(false);
        if (error is null)
        {
            await NameAccountAsync(cancellationToken).ConfigureAwait(false);
        }

        return error;
    }

    public Task SignOutAsync(CancellationToken cancellationToken) => _signIn.SignOutAsync(cancellationToken);

    /// <summary>
    /// Puts the person's own name on the account where a connected product can say
    /// it. The sign-in has already succeeded by then, so a product that cannot say —
    /// none connected yet, an installation without the members path — leaves the
    /// account under <see cref="SpecManagerSignIn.FallbackAccountName"/> rather than
    /// failing it.
    /// </summary>
    private async Task NameAccountAsync(CancellationToken cancellationToken)
    {
        if (FirstTarget() is not { } target) return;

        await NameAccountFromAsync(target.Target, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Names an account still under the default name from the first product that
    /// fetched — the sign-in may have come before any product was connected. Tried
    /// once per run, so an installation without the members path is not asked on
    /// every sync.
    /// </summary>
    private async Task NameAccountOnceAsync(string product, CancellationToken cancellationToken)
    {
        if (_signIn.Account?.DisplayName != SpecManagerSignIn.FallbackAccountName) return;
        if (Interlocked.Exchange(ref _namingTried, 1) == 1) return;

        await NameAccountFromAsync(product, cancellationToken).ConfigureAwait(false);
    }

    private async Task NameAccountFromAsync(string product, CancellationToken cancellationToken)
    {
        try
        {
            var members = await _client.GetMembersAsync(product, cancellationToken).ConfigureAwait(false);
            if (members?.FirstOrDefault(member => member.IsJij)?.Naam is { Length: > 0 } name)
            {
                _signIn.SetAccountName(name);
            }
        }
        catch (Exception ex) when (SpecManagerSignIn.IsUnreachable(ex, cancellationToken) || SpecManagerSignIn.IsNotKept(ex))
        {
            // The sign-in or the fetch has already succeeded; the name is a nicety.
            _log.LogInformation(ex, "spec-manager did not say whose the sign-in is; it keeps the default name.");
        }
    }

    private ConnectedTarget? FirstTarget() =>
        _targets?.List().FirstOrDefault(target => target.Enabled && target.ConnectorId == ConnectorId);

    /// <summary>The product's catalog, read again when it is older than
    /// <see cref="CatalogLifetime"/> or does not know a status or label one of
    /// <paramref name="items"/> names.</summary>
    private async Task<ProductCatalog> CatalogAsync(string product, IEnumerable<BacklogitemDto> items, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        if (_catalogs.TryGetValue(product, out var cached)
            && now - cached.LoadedAt <= CatalogLifetime
            && items.All(cached.Knows))
        {
            return cached;
        }

        var statuses = await _client.GetStatusesAsync(product, cancellationToken).ConfigureAwait(false);
        var labels = await _client.GetLabelsAsync(product, cancellationToken).ConfigureAwait(false);

        var catalog = new ProductCatalog(statuses, labels, now);
        _catalogs[product] = catalog;
        return catalog;
    }
}
