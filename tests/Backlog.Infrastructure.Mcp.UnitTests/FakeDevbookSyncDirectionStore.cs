using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel.Devbook;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// The direction map, in memory, standing in for the file store the desktop
/// composes. Keyed the way the real store keys — the folded alias and the
/// normalised address — so a test that states a direction under one layout and
/// asks under another meets what the app would. It records every alias it was
/// asked about, so a test can show the tool resolved the repository before it read.
/// </summary>
internal sealed class FakeDevbookSyncDirectionStore : IDevbookSyncDirectionStore
{
    private static readonly DateTimeOffset Stamp = new(2026, 11, 2, 9, 30, 0, TimeSpan.Zero);

    private readonly Dictionary<(string Alias, string Address), DevbookSyncStatement> _directions = [];

    public event Action? Changed;

    /// <summary>Every alias a read was asked for.</summary>
    public List<string> AskedAliases { get; } = [];

    public IReadOnlyList<DevbookSyncStatement> List(string? repositoryAlias)
    {
        var alias = DevbookSubject.AliasKey(repositoryAlias);
        AskedAliases.Add(alias);

        return [.. _directions.Where(entry => entry.Key.Alias == alias).Select(entry => entry.Value).OrderBy(entry => entry.Address, StringComparer.Ordinal)];
    }

    public string? Get(string? repositoryAlias, string address) =>
        DevbookSync.TryAddress(address, out _, out var normalised)
            ? _directions.GetValueOrDefault((DevbookSubject.AliasKey(repositoryAlias), normalised))?.Direction
            : null;

    public DevbookSyncStatement? Set(string? repositoryAlias, string address, string? direction, string by)
    {
        if (!DevbookSync.TryAddress(address, out _, out var normalised)) throw new ArgumentException("No devbook folder.", nameof(address));

        var key = (DevbookSubject.AliasKey(repositoryAlias), normalised);
        if (string.IsNullOrWhiteSpace(direction))
        {
            _directions.Remove(key);
            Changed?.Invoke();
            return null;
        }

        if (!DevbookSync.IsDirection(direction)) throw new ArgumentException("No direction.", nameof(direction));

        var statement = new DevbookSyncStatement(normalised, direction.Trim().ToLowerInvariant(), by, Stamp);
        _directions[key] = statement;
        Changed?.Invoke();

        return statement;
    }

    public bool IsImported(string? repositoryAlias) => false;

    public void Import(string? repositoryAlias, IEnumerable<(string Address, string Direction)> stated) =>
        throw new NotSupportedException("No MCP tool imports.");
}
