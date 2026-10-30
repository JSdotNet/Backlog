using System.Text.Json;

using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.Devbook.Documents;

/// <summary>
/// What a repository adopted from the delivery stack, as the stamps under
/// <c>components</c> in its <c>.devbook/config.json</c> record it — the whole
/// source the AI section's Devbook tab draws from. Read here, beside the folder
/// readers, because a module's screen opens no file itself.
/// <para>
/// Only the stamps: no installed or newest version is asked of anything, because
/// the tab says what this repository took up, and the stamp is where each
/// component's own init and update write that down. A component with no stamp is
/// not adopted — <see langword="null"/> here — which is a state of the repository,
/// never an error.
/// </para>
/// </summary>
/// <param name="Delivery">The delivery engine's stamp, <c>components.delivery</c>.</param>
/// <param name="Devbook">The devbook plugin's stamp, <c>components.devbook</c>.</param>
/// <param name="Schedule">The delivery schedule's stamp, <c>components.schedule</c>.</param>
public sealed record DevbookStackStamps(
    DevbookComponentStamp? Delivery,
    DevbookPluginStamp? Devbook,
    DevbookScheduleStamp? Schedule)
{
    /// <summary>The configuration file, beside the devbook folders under
    /// <c>.devbook/</c>.</summary>
    public const string FileName = "config.json";

    /// <summary>
    /// The configuration of the repository at <paramref name="repositoryRoot"/>, or
    /// <see langword="null"/> when it has none — no file, one that cannot be read,
    /// or one that is not a JSON object. Each of those is drawn as the empty state:
    /// a repository with no readable configuration records no adoption.
    /// </summary>
    public static DevbookStackStamps? Read(string? repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot)) return null;

        try
        {
            var path = Path.Combine(repositoryRoot, DevbookFolderSetting.DevbookRoot, FileName);
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The configuration of the repository the AI folder resolves in. The folder
    /// source answers offline and from disk, so this is cheap enough to ask each
    /// time the folder is read again; a branch snapshot carries only the devbook
    /// folders, so it reads as having no configuration.
    /// </summary>
    public static DevbookStackStamps? Load(IDevbookFolderSource? folders, string folderKey, string? repositoryAlias)
    {
        if (folders is null) return null;

        try
        {
            return Read(folders.Resolve(folderKey, repositoryAlias).RootPath);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>The configuration file's text read for its stamps; see
    /// <see cref="Read"/> for what reads as none.</summary>
    public static DevbookStackStamps? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

            if (!document.RootElement.TryGetProperty("components", out var components) || components.ValueKind != JsonValueKind.Object)
            {
                return new DevbookStackStamps(null, null, null);
            }

            return new DevbookStackStamps(
                Object(components, "delivery") is { } delivery ? new DevbookComponentStamp(Text(delivery, "pluginVersion")) : null,
                Object(components, "devbook") is { } devbook ? PluginStamp(devbook) : null,
                Object(components, "schedule") is { } schedule ? new DevbookScheduleStamp(Text(schedule, "pluginVersion"), Words(schedule, "enabled")) : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DevbookPluginStamp PluginStamp(JsonElement devbook)
    {
        var managed = 0;
        var unmanaged = 0;
        if (devbook.TryGetProperty("materialized", out var materialized) && materialized.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in materialized.EnumerateObject())
            {
                // Only an entry that says it is managed counts as managed: one with no
                // flag is a file the plugin wrote once and no longer keeps.
                var isManaged = entry.Value.ValueKind == JsonValueKind.Object
                    && entry.Value.TryGetProperty("managed", out var flag)
                    && flag.ValueKind == JsonValueKind.True;
                if (isManaged) managed++;
                else unmanaged++;
            }
        }

        var migrations = new List<DevbookMigrationStamp>();
        if (devbook.TryGetProperty("migrations", out var recorded) && recorded.ValueKind == JsonValueKind.Array)
        {
            foreach (var migration in recorded.EnumerateArray())
            {
                if (migration.ValueKind == JsonValueKind.String && migration.GetString() is { Length: > 0 } bare)
                {
                    migrations.Add(new DevbookMigrationStamp(bare, null));
                }
                else if (migration.ValueKind == JsonValueKind.Object && Text(migration, "id") is { } id)
                {
                    migrations.Add(new DevbookMigrationStamp(id, Text(migration, "applied")));
                }
            }
        }

        var procedures = Object(devbook, "procedures") is { } block ? Words(block, "adopted") : [];

        return new DevbookPluginStamp(
            Text(devbook, "pluginVersion"),
            Text(devbook, "contractVersion"),
            Words(devbook, "adopted"),
            procedures,
            managed,
            unmanaged,
            migrations);
    }

    private static JsonElement? Object(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    /// <summary>A scalar as the file wrote it: a contract version is a number and a
    /// plugin version a string, and both are drawn as text.</summary>
    private static string? Text(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)) return null;

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static IReadOnlyList<string> Words(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!.Trim())
                .Where(item => item.Length > 0)]
            : [];
}

/// <summary>A component's stamp that records its version and nothing else.</summary>
/// <param name="PluginVersion">The plugin version the component was last
/// initialised or updated at.</param>
public sealed record DevbookComponentStamp(string? PluginVersion);

/// <summary>The devbook plugin's stamp.</summary>
/// <param name="PluginVersion">The plugin version it was last updated at.</param>
/// <param name="ContractVersion">The folder contract the corpus is at.</param>
/// <param name="Adopted">The devbook folders adopted, by folder name.</param>
/// <param name="Procedures">The procedures adopted, <c>procedures.adopted</c>.</param>
/// <param name="ManagedFiles">The <c>materialized</c> entries flagged managed.</param>
/// <param name="UnmanagedFiles">The <c>materialized</c> entries that are not.</param>
/// <param name="Migrations">The migrations it records, in the order written.</param>
public sealed record DevbookPluginStamp(
    string? PluginVersion,
    string? ContractVersion,
    IReadOnlyList<string> Adopted,
    IReadOnlyList<string> Procedures,
    int ManagedFiles,
    int UnmanagedFiles,
    IReadOnlyList<DevbookMigrationStamp> Migrations)
{
    public int MaterializedFiles => ManagedFiles + UnmanagedFiles;
}

/// <param name="Id">The migration's id.</param>
/// <param name="Applied">The date it was applied, as written, or null.</param>
public sealed record DevbookMigrationStamp(string Id, string? Applied);

/// <summary>The delivery schedule's stamp.</summary>
/// <param name="PluginVersion">The plugin version it was last updated at.</param>
/// <param name="Enabled">The schedules enabled, by catalog name.</param>
public sealed record DevbookScheduleStamp(string? PluginVersion, IReadOnlyList<string> Enabled);
