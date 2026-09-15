using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backlog.Infrastructure.Claude;

/// <summary>
/// The Claude organizations Backlog can read usage from, in the order they were
/// configured.
/// <para>
/// Never empty. A fresh install and a file written before accounts existed both
/// read as one account, so the Settings card always has a card to draw and a
/// caller never has to special-case "no account" separately from "an account with
/// nothing in it" — the two are the same state.
/// </para>
/// </summary>
public sealed record ClaudeSettings
{
    public IReadOnlyList<ClaudeAccount> Accounts { get; init; } = [new ClaudeAccount()];

    /// <summary>True when any account holds a key.</summary>
    [JsonIgnore]
    public bool IsConfigured => Accounts.Any(account => account.IsConfigured);

    /// <summary>The accounts the dashboard can read a personal figure from: a key
    /// and an actor each.</summary>
    [JsonIgnore]
    public IReadOnlyList<ClaudeAccount> ReportingAccounts => [.. Accounts.Where(account => account.CanReportSpend)];

    /// <summary>The account one id names, or null when there is none.</summary>
    public ClaudeAccount? Account(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : Accounts.FirstOrDefault(account => ClaudeAccount.IsSameId(account.Id, id));
}

/// <summary>
/// Reads and writes <see cref="ClaudeSettings"/> in the per-user application
/// folder. The admin keys deliberately live outside the backlog folder so synced
/// or committed content never carries credentials.
/// </summary>
public sealed class ClaudeSettingsStore
{
    public const string DefaultApiVersion = "2023-06-01";
    public const string DefaultApiEndpoint = "https://api.anthropic.com";

    private const string NotAnAccount = "That Claude account is no longer configured.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public ClaudeSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Backlog",
            "claude.json"))
    {
    }

    public ClaudeSettingsStore(string path)
    {
        _path = path;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Current = Read();
    }

    /// <summary>Raised after the configuration changes, so open views and any
    /// cached connection can react.</summary>
    public event Action? Changed;

    public ClaudeSettings Current { get; private set; }

    /// <summary>Where the file lives, shown in Settings so it can be found (and
    /// so it is obvious the keys are not in the backlog folder).</summary>
    public string SettingsPath => _path;

    /// <summary>Adds an empty account at the end of the list. The caller finds it
    /// as the last of <see cref="ClaudeSettings.Accounts"/>.</summary>
    public string? AddAccount() =>
        Save(Current with { Accounts = [.. Current.Accounts, new ClaudeAccount()] });

    /// <summary>
    /// Forgets one account, key and all. Removing the last one leaves a blank
    /// account in its place rather than an empty list, which is the invariant the
    /// type documents.
    /// </summary>
    public string? RemoveAccount(string id)
    {
        if (Current.Account(id) is not { } target) return NotAnAccount;

        return Save(Current with
        {
            Accounts = [.. Current.Accounts.Where(account => !ClaudeAccount.IsSameId(account.Id, target.Id))]
        });
    }

    public string? SetDisplayName(string id, string? displayName) =>
        Update(id, account => account with { DisplayName = displayName });

    public string? SetAdminApiKey(string id, string? adminApiKey) =>
        Update(id, account => account with { AdminApiKey = adminApiKey });

    public string? SetWorkspaceId(string id, string? workspaceId) =>
        Update(id, account => account with { WorkspaceId = workspaceId });

    public string? SetActor(string id, string? actor) =>
        Update(id, account => account with { Actor = actor });

    public string? SetApiEndpoint(string id, string? apiEndpoint) =>
        Update(id, account => account with { ApiEndpoint = apiEndpoint ?? DefaultApiEndpoint });

    public string? ClearAdminApiKey(string id) => SetAdminApiKey(id, null);

    private string? Update(string id, Func<ClaudeAccount, ClaudeAccount> change)
    {
        if (Current.Account(id) is not { } target) return NotAnAccount;

        return Save(Current with
        {
            Accounts = [.. Current.Accounts.Select(account => ClaudeAccount.IsSameId(account.Id, target.Id) ? change(account) : account)]
        });
    }

    private string? Save(ClaudeSettings settings)
    {
        Current = Normalize(settings);

        string? error = null;
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(new SettingsFile { Accounts = [.. Current.Accounts] }, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "Changed, but the Claude settings couldn't be saved for next time.";
        }

        Changed?.Invoke();
        return error;
    }

    private ClaudeSettings Read()
    {
        try
        {
            if (!File.Exists(_path)) return Normalize(new ClaudeSettings());

            var file = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(_path), JsonOptions);
            return Normalize(file?.ToSettings() ?? new ClaudeSettings());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt settings file must never stop the app from opening.
            return Normalize(new ClaudeSettings());
        }
    }

    /// <summary>
    /// Every account as it is stored: trimmed fields, a normalized endpoint, an id
    /// that is present and unique, and at least one account in the list.
    /// </summary>
    private static ClaudeSettings Normalize(ClaudeSettings settings)
    {
        var accounts = new List<ClaudeAccount>();

        foreach (var account in settings.Accounts)
        {
            var id = Clean(account.Id);
            if (id is null || accounts.Any(known => ClaudeAccount.IsSameId(known.Id, id))) id = ClaudeAccount.NewId();

            accounts.Add(new ClaudeAccount
            {
                Id = id,
                DisplayName = Clean(account.DisplayName),
                AdminApiKey = Clean(account.AdminApiKey),
                WorkspaceId = Clean(account.WorkspaceId),
                Actor = Clean(account.Actor),
                ApiVersion = Clean(account.ApiVersion) ?? DefaultApiVersion,
                ApiEndpoint = NormalizeEndpoint(account.ApiEndpoint)
            });
        }

        if (accounts.Count == 0) accounts.Add(new ClaudeAccount());

        return new ClaudeSettings { Accounts = accounts };
    }

    private static string NormalizeEndpoint(string? endpoint)
    {
        var trimmed = Clean(endpoint);
        if (trimmed is null) return DefaultApiEndpoint;

        return trimmed.EndsWith("/", StringComparison.Ordinal)
            ? trimmed.TrimEnd('/')
            : trimmed;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The file's shape. It carries the flat fields the file had before accounts
    /// existed so an older <c>claude.json</c> reads as one account with nothing
    /// lost; they are read, never written back, so the first save moves the file
    /// to the list shape for good.
    /// </summary>
    private sealed class SettingsFile
    {
        public List<ClaudeAccount>? Accounts { get; set; }

        // Read-only in practice: a save constructs this with the list alone, and
        // the condition keeps the null legacy fields out of the file it writes.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? AdminApiKey { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? WorkspaceId { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Actor { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ApiVersion { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ApiEndpoint { get; set; }

        public ClaudeSettings ToSettings()
        {
            if (Accounts is { Count: > 0 }) return new ClaudeSettings { Accounts = Accounts };

            var hasLegacyFields = !string.IsNullOrWhiteSpace(AdminApiKey)
                || !string.IsNullOrWhiteSpace(WorkspaceId)
                || !string.IsNullOrWhiteSpace(Actor)
                || !string.IsNullOrWhiteSpace(ApiVersion)
                || !string.IsNullOrWhiteSpace(ApiEndpoint);

            return hasLegacyFields
                ? new ClaudeSettings
                {
                    Accounts =
                    [
                        new ClaudeAccount
                        {
                            AdminApiKey = AdminApiKey,
                            WorkspaceId = WorkspaceId,
                            Actor = Actor,
                            ApiVersion = ApiVersion ?? DefaultApiVersion,
                            ApiEndpoint = ApiEndpoint ?? DefaultApiEndpoint
                        }
                    ]
                }
                : new ClaudeSettings();
        }
    }
}
