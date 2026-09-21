using System.Globalization;
using System.Text.Json;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The disk half of <see cref="IAiCreditUsageCache"/>: one small JSON file per
/// login per month, under the spend cache root.
/// <para>
/// The scope is stored as its name rather than its number. The enum is this
/// adapter's and it may gain a member; a name that no longer parses reads as
/// Unknown, which is the honest value for a file written by a version that knew
/// something this one does not, where a number would silently mean a different
/// member.
/// </para>
/// <para>
/// Non-throwing on every path, like every cache beside it: a corrupt or unreadable
/// month is fetched, and one that could not be written is fetched again next time.
/// </para>
/// </summary>
public sealed class AiCreditUsageCache(Func<string> cacheRoot) : IAiCreditUsageCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private const int Version = 1;

    private const string FolderName = "copilot";

    private readonly Func<string> _cacheRoot = cacheRoot ?? throw new ArgumentNullException(nameof(cacheRoot));

    public GitHubAiCreditUsage? TryRead(string login, int year, int month)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(login);

        try
        {
            var path = EntryPath(login, year, month);
            if (!File.Exists(path)) return null;

            var stored = JsonSerializer.Deserialize<StoredMonth>(File.ReadAllText(path), JsonOptions);

            if (stored is null || stored.Version != Version) return null;

            return new GitHubAiCreditUsage(
                [.. stored.Items.Select(item => new GitHubAiCreditUsageItem(
                    item.Product,
                    item.Sku,
                    item.Model,
                    item.UnitType,
                    item.PricePerUnit,
                    item.GrossQuantity,
                    item.GrossAmount,
                    item.DiscountQuantity,
                    item.DiscountAmount,
                    item.NetQuantity,
                    item.NetAmount))],
                Enum.TryParse<GitHubBillingScope>(stored.Scope, ignoreCase: true, out var scope)
                    ? scope
                    : GitHubBillingScope.Unknown);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // A half-written or unreadable month is a miss that costs one call.
            return null;
        }
    }

    public void Write(string login, int year, int month, GitHubAiCreditUsage usage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(login);
        ArgumentNullException.ThrowIfNull(usage);

        var path = EntryPath(login, year, month);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            File.WriteAllText(path, JsonSerializer.Serialize(
                new StoredMonth
                {
                    Version = Version,
                    Scope = usage.Scope.ToString(),
                    Items = [.. usage.Items.Select(item => new StoredItem
                    {
                        Product = item.Product,
                        Sku = item.Sku,
                        Model = item.Model,
                        UnitType = item.UnitType,
                        PricePerUnit = item.PricePerUnit,
                        GrossQuantity = item.GrossQuantity,
                        GrossAmount = item.GrossAmount,
                        DiscountQuantity = item.DiscountQuantity,
                        DiscountAmount = item.DiscountAmount,
                        NetQuantity = item.NetQuantity,
                        NetAmount = item.NetAmount
                    })]
                },
                JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A month that could not be written is fetched again next time.
        }
    }

    public void Forget()
    {
        var root = Path.Combine(_cacheRoot(), FolderName);

        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left behind rather than fatal.
        }
    }

    /// <summary>Lower-cased before it names a folder, because a GitHub login is
    /// case-insensitive and one person is one folder.</summary>
    private string EntryPath(string login, int year, int month) =>
        Path.Combine(
            _cacheRoot(),
            FolderName,
            CachePaths.Safe(login.Trim().ToLowerInvariant()),
            $"{year.ToString("D4", CultureInfo.InvariantCulture)}-{month.ToString("D2", CultureInfo.InvariantCulture)}.json");

    private sealed record StoredMonth
    {
        public int Version { get; init; }

        public string? Scope { get; init; }

        public StoredItem[] Items { get; init; } = [];
    }

    private sealed record StoredItem
    {
        public string? Product { get; init; }

        public string? Sku { get; init; }

        public string? Model { get; init; }

        public string? UnitType { get; init; }

        public decimal PricePerUnit { get; init; }

        public decimal GrossQuantity { get; init; }

        public decimal GrossAmount { get; init; }

        public decimal DiscountQuantity { get; init; }

        public decimal DiscountAmount { get; init; }

        public decimal NetQuantity { get; init; }

        public decimal NetAmount { get; init; }
    }
}
