using System.Collections.Concurrent;

using Backlog.Infrastructure.SpecManager.Api;
using Backlog.Infrastructure.SpecManager.OAuth;
using Backlog.Modules.Tasks.Abstractions;
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
    /// The badge: the icon is a name, as every descriptor's is, and the library has
    /// no provider mark by this one, so the badge draws the name and key alone,
    /// coloured with <c>color-band-2</c>, the teal of the design palette's bands —
    /// distinct from the ink a GitHub badge takes.
    /// <para>
    /// A target is a product, typed as its slug: the segment of the product's
    /// spec-manager address that names it, never a repository.
    /// </para>
    /// </summary>
    public TaskConnectorDescriptor Descriptor { get; } = new(ConnectorId, "spec-manager", ConnectorId, "color-band-2")
    {
        TargetLabel = "Product",
        TargetPlaceholder = "product-slug",
        TargetHelp = "The product's slug, as it appears in its spec-manager URL.",
    };

    /// <summary>Items carry storypoints and say what they wait on, and an item is
    /// completed by moving it to the product's first end status.</summary>
    public TaskConnectorCapabilities Capabilities { get; } = new(HasEffort: true, HasDependencies: true, CanComplete: true);

    public TaskConnectorAccount? Account => _signIn.Account;

    public event Action? AccountChanged
    {
        add => _signIn.AccountChanged += value;
        remove => _signIn.AccountChanged -= value;
    }

    /// <summary>
    /// The product's backlog as items — see the class remarks for what a first and
    /// a later sync ask for.
    /// <para>
    /// A refusal is said as what it means for the person, through
    /// <see cref="TaskConnectorFetchException"/>: nobody signed in, a product
    /// spec-manager does not have — most often a slug typed as something else — and
    /// a product the account may not read. A server error or a network that did not
    /// answer is left as it was thrown.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<SourceItem>> FetchAsync(string target, DateTimeOffset? since, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        IReadOnlyList<SourceItem> items;
        try
        {
            items = await ReadBacklogAsync(target, since, cancellationToken).ConfigureAwait(false);
        }
        catch (SpecManagerSignInRequiredException ex)
        {
            throw new TaskConnectorFetchException(
                TaskConnectorFetchFailure.SignInRequired,
                $"Not signed in to spec-manager, so {target} cannot sync. {WhereToSignIn}",
                ex);
        }
        catch (HttpRequestException ex) when (Classify(ex, target) is { } recognised)
        {
            throw recognised;
        }

        await NameAccountOnceAsync(target, cancellationToken).ConfigureAwait(false);

        return items;
    }

    /// <summary>
    /// What a status spec-manager refused a read with means for the person, or null
    /// for one this connector has no reading of. A 401 here has already been
    /// retried with a refreshed token, so it is a sign-in spec-manager no longer
    /// honours.
    /// </summary>
    private static TaskConnectorFetchException? Classify(HttpRequestException failure, string product) => failure.StatusCode switch
    {
        System.Net.HttpStatusCode.NotFound => new(
            TaskConnectorFetchFailure.NotFound,
            $"spec-manager has no product named {product}. Use the product's slug, as it appears in its spec-manager URL.",
            failure),
        System.Net.HttpStatusCode.Forbidden => new(
            TaskConnectorFetchFailure.NoAccess,
            $"The spec-manager account signed in here may not read {product}. Sign in as a member of the product on the spec-manager line in Settings → Connectors.",
            failure),
        System.Net.HttpStatusCode.Unauthorized => new(
            TaskConnectorFetchFailure.SignInRequired,
            $"spec-manager no longer accepts this sign-in, so {product} cannot sync. {WhereToSignIn}",
            failure),
        _ => null,
    };

    /// <summary>Where the person signs in, said the way GitHub's sentences name
    /// Settings → GitHub: the spec-manager line at the top of the Connectors
    /// section.</summary>
    private const string WhereToSignIn = "Sign in on the spec-manager line in Settings → Connectors.";

    private async Task<IReadOnlyList<SourceItem>> ReadBacklogAsync(string target, DateTimeOffset? since, CancellationToken cancellationToken)
    {
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

        return items;
    }

    /// <summary>
    /// Moves the item to the product's first end status, by the product's order —
    /// "Klaar" on a board that ends there. Every refusal is answered in words: nobody
    /// is signed in, the product has no end status, or spec-manager said no, with
    /// the status it said it with and its own reason when it gave one. A product
    /// whose agent switch is off refuses an agent's token this way.
    /// <para>
    /// The item's status at the source is not read first: the REST interface has no
    /// read of one item, and the backlog read is the whole list. What guards against
    /// moving an item that is already finished — or one closed another way, which
    /// this would move to the end status — is the held state alone: the handler asks
    /// only while the last sync saw the item open. An item finished at the source
    /// since that sync is moved to the first end status, which may not be the one it
    /// was finished in.
    /// </para>
    /// </summary>
    public async Task<string?> CompleteAsync(SourceRef item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (_signIn.Account is null) return SignInToComplete;

        try
        {
            var catalog = await CatalogAsync(item.Target, [], cancellationToken).ConfigureAwait(false);
            if (catalog.FirstEndStatus is not { } end)
            {
                return $"{item.Target} has no end status in spec-manager, so {item.DisplayKey} could not be completed.";
            }

            await _client.SetStatusAsync(item.Target, item.ExternalId, end.Id, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (SpecManagerSignInRequiredException)
        {
            return SignInToComplete;
        }
        catch (SpecManagerRefusedException ex)
        {
            return ex.Reason is null
                ? $"spec-manager refused ({(int)ex.Status})."
                : $"spec-manager refused ({(int)ex.Status}): {ex.Reason}";
        }
        catch (HttpRequestException ex)
        {
            // The catalog read is a GET that fails by status, or the network that
            // fails outright; either is a refusal the person can read.
            return ex.StatusCode is { } status
                ? $"spec-manager refused ({(int)status})."
                : $"spec-manager could not be reached: {ex.Message}";
        }
    }

    /// <summary>
    /// The products the signed-in account can see, to pick from: each stored as its
    /// slug and shown by its name, the slug standing in for a name that is blank.
    /// <para>
    /// A list that cannot be had is an answer, never a throw into the page: nobody
    /// signed in, or spec-manager refusing or failing the list — which it does today
    /// for the app's agent token, until the product list is opened to it. Either
    /// way the sentence says to type the product's slug instead.
    /// </para>
    /// </summary>
    public async Task<ConnectorTargetChoices> ListTargetChoicesAsync(CancellationToken cancellationToken)
    {
        if (_signIn.Account is null) return ConnectorTargetChoices.Unavailable(SignInToPick);

        try
        {
            var products = await _client.GetProductsAsync(cancellationToken).ConfigureAwait(false);

            return new ConnectorTargetChoices([.. products
                .Where(product => !string.IsNullOrWhiteSpace(product.Slug))
                .Select(product => new ConnectorTargetChoice(
                    product.Slug!.Trim(),
                    string.IsNullOrWhiteSpace(product.Naam) ? product.Slug.Trim() : product.Naam.Trim()))]);
        }
        catch (SpecManagerSignInRequiredException)
        {
            return ConnectorTargetChoices.Unavailable(SignInToPick);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or NotSupportedException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _log.LogInformation(ex, "spec-manager would not list the products; the settings page falls back to typing a slug.");
            return ConnectorTargetChoices.Unavailable(
                "spec-manager would not list your products yet. Type the product's slug instead, as it appears in its spec-manager URL.");
        }
    }

    /// <summary>Why the products cannot be listed with nobody signed in.</summary>
    private const string SignInToPick =
        "Sign in on the spec-manager line above to pick a product, or type the product's slug, as it appears in its spec-manager URL.";

    /// <summary>The refusal a write-back with nobody signed in answers.</summary>
    internal const string SignInToComplete = "Sign in to spec-manager to complete items there.";

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
