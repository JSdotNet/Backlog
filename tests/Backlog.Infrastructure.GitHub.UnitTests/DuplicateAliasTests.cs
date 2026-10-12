using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// A registry that reaches this machine with two rows sharing one alias. The
/// Repositories tab keys one subpage per alias, so a duplicate threw Blazor's
/// duplicate-key exception and the whole Settings page showed the unhandled-error
/// banner. A bare-name plan registration (<c>finance/finance</c>) beside the real
/// repository (<c>JSdotNet/finance</c>) is how the first one arrived.
/// </summary>
public sealed class DuplicateAliasTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "duplicate-alias-tests-" + Guid.NewGuid().ToString("N"));

    private string WorkspaceRoot => Path.Combine(_root, "workspace");

    private GitHubSettingsStore Store() =>
        new(Path.Combine(_root, "local", "github.json"), () => WorkspaceRoot);

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private void WriteRegistry(string json)
    {
        var path = Path.Combine(WorkspaceRoot, GitHubSettingsStore.RegistryFolderName, "repos.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private const string TwoFinances = """
        {
          "repositories": [
            { "id": "finance/finance", "alias": "finance" },
            { "id": "JSdotNet/finance", "alias": "finance" }
          ]
        }
        """;

    [Fact]
    public void A_registry_with_a_shared_alias_loads_with_distinct_aliases()
    {
        WriteRegistry(TwoFinances);

        var repositories = Store().Current.Repositories;

        Assert.Equal(["finance/finance", "JSdotNet/finance"], repositories.Select(r => r.FullName));
        Assert.Equal(["finance", "jsdotnet-finance"], repositories.Select(r => r.Alias));
    }

    /// <summary>A later row's own alias is reserved before a free one is handed
    /// out, so making room for a duplicate never renames a third repository.</summary>
    [Fact]
    public void A_free_alias_never_takes_one_a_later_row_chose()
    {
        WriteRegistry("""
            {
              "repositories": [
                { "id": "finance/finance", "alias": "finance" },
                { "id": "JSdotNet/finance", "alias": "finance" },
                { "id": "x/y", "alias": "jsdotnet-finance" }
              ]
            }
            """);

        Assert.Equal(
            ["finance", "jsdotnet-finance-2", "jsdotnet-finance"],
            Store().Current.Repositories.Select(r => r.Alias));
    }

    /// <summary>Two rows for one repository spelled in different case still
    /// cannot share a key on the Repositories tab.</summary>
    [Fact]
    public void Rows_differing_only_in_case_get_distinct_aliases()
    {
        WriteRegistry("""
            {
              "repositories": [
                { "id": "JSdotNet/finance", "alias": "finance" },
                { "id": "jsdotnet/Finance", "alias": "finance" }
              ]
            }
            """);

        var aliases = Store().Current.Repositories.Select(r => r.Alias).ToList();

        Assert.Equal(aliases.Count, aliases.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_save_never_writes_a_shared_alias()
    {
        var store = Store();

        store.SetRepositories(
        [
            new GitHubRepositoryRef("finance", "finance", "finance"),
            new GitHubRepositoryRef("finance", "JSdotNet", "finance")
        ]);

        Assert.Equal(["finance", "jsdotnet-finance"], Store().Current.Repositories.Select(r => r.Alias));
    }
}
