using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using Backlog.SharedKernel.Mcp;

namespace Backlog.Infrastructure.FileSystem;

/// <summary>
/// The agents' access requests (local ADR 0052 §5), in <c>mcp-access-requests.json</c> beside
/// <c>mcp-grants.json</c> in the installation's app-data folder. Legible JSON and nothing
/// secret: a request names a grant id, which is a label, and what the agent said about itself.
/// <para>
/// Read from the file on every call, written to a temporary file that then replaces it, under
/// one lock — as <see cref="FileMcpGrantStore"/> does, for the same reasons. A file that does
/// not parse reads as no requests and is never written over: <see cref="Add"/> and
/// <see cref="Answer"/> throw <see cref="UnreadableMcpAccessRequestFileException"/> until the
/// person mends or moves it. With no request read, a pending grant holds no scope and so
/// reaches nothing: an unreadable file fails closed.
/// </para>
/// </summary>
public sealed class FileMcpAccessRequestStore : IMcpAccessRequestStore
{
    /// <summary>The file's name in the app-data folder.</summary>
    public const string FileName = "mcp-access-requests.json";

    private const string IdAlphabet = "abcdefghijklmnopqrstuvwxyz234567";

    private const int IdLength = 16;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _path;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();

    public FileMcpAccessRequestStore(string path, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public IReadOnlyList<McpAccessRequest> List()
    {
        lock (_gate)
        {
            return [.. Load().Requests.Where(IsRequest).Select(ToRequest)];
        }
    }

    /// <inheritdoc />
    public McpAccessRequest? Find(string id)
    {
        lock (_gate)
        {
            return FindRecord(Load(), id) is { } record ? ToRequest(record) : null;
        }
    }

    /// <inheritdoc />
    /// <exception cref="UnreadableMcpAccessRequestFileException">The file exists and does not parse.</exception>
    public McpAccessRequest Add(
        string grantId,
        McpAccessRequestKind kind,
        string name,
        IReadOnlyList<string> scopes,
        string? executable,
        string? folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(scopes);

        McpAccessRequest request;

        lock (_gate)
        {
            var file = LoadForWrite();
            var record = new RequestRecord
            {
                Id = McpAccessRequest.IdPrefix + new string(RandomNumberGenerator.GetItems<char>(IdAlphabet, IdLength)),
                GrantId = grantId,
                Kind = kind,
                Name = name,
                Scopes = [.. scopes],
                Executable = executable,
                Folder = folder,
                RequestedAt = _clock.GetUtcNow(),
                State = McpAccessRequestState.Open,
                Allowed = []
            };

            file.Requests.Add(record);
            Save(file);
            request = ToRequest(record);
        }

        Changed?.Invoke();
        return request;
    }

    /// <inheritdoc />
    /// <exception cref="UnreadableMcpAccessRequestFileException">The file exists and does not parse.</exception>
    public McpAccessRequest? Answer(string id, McpAccessRequestState state, IReadOnlyList<string> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);
        if (state == McpAccessRequestState.Open) throw new ArgumentException("An answer allows or declines.", nameof(state));

        McpAccessRequest request;

        lock (_gate)
        {
            var file = LoadForWrite();
            if (FindRecord(file, id) is not { State: McpAccessRequestState.Open } record) return null;

            record.State = state;
            record.AnsweredAt = _clock.GetUtcNow();
            record.Allowed = state == McpAccessRequestState.Allowed ? [.. allowed] : [];
            Save(file);
            request = ToRequest(record);
        }

        Changed?.Invoke();
        return request;
    }

    private static bool IsRequest(RequestRecord? record) =>
        record is not null && !string.IsNullOrWhiteSpace(record.Id) && !string.IsNullOrWhiteSpace(record.GrantId);

    private static RequestRecord? FindRecord(RequestFile file, string id) =>
        file.Requests.Where(IsRequest).FirstOrDefault(candidate => string.Equals(candidate!.Id, id, StringComparison.Ordinal));

    private RequestFile LoadForWrite()
    {
        var file = Load();
        return file.Unreadable ? throw new UnreadableMcpAccessRequestFileException(_path) : file;
    }

    private RequestFile Load()
    {
        try
        {
            if (!File.Exists(_path)) return new RequestFile();

            var file = JsonSerializer.Deserialize<RequestFile>(File.ReadAllText(_path), JsonOptions);
            return file?.Requests is null ? new RequestFile { Unreadable = true } : file;
        }
        catch (JsonException)
        {
            return new RequestFile { Unreadable = true };
        }
    }

    private void Save(RequestFile file)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(file, JsonOptions));
        File.Move(temporary, _path, overwrite: true);
    }

    private static McpAccessRequest ToRequest(RequestRecord? record) => new(
        record!.Id!,
        record.GrantId!,
        record.Kind,
        record.Name ?? string.Empty,
        [.. record.Scopes ?? []],
        record.Executable,
        record.Folder,
        record.RequestedAt,
        record.State,
        record.AnsweredAt,
        [.. record.Allowed ?? []]);

    private sealed class RequestFile
    {
        [JsonPropertyName("requests")]
        public List<RequestRecord?> Requests { get; set; } = [];

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Unknown { get; set; }

        [JsonIgnore]
        public bool Unreadable { get; init; }
    }

    private sealed class RequestRecord
    {
        public string? Id { get; set; }

        public string? GrantId { get; set; }

        public McpAccessRequestKind Kind { get; set; }

        public string? Name { get; set; }

        public List<string>? Scopes { get; set; }

        public string? Executable { get; set; }

        public string? Folder { get; set; }

        public DateTimeOffset RequestedAt { get; set; }

        public McpAccessRequestState State { get; set; }

        public DateTimeOffset? AnsweredAt { get; set; }

        public List<string>? Allowed { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Unknown { get; set; }
    }
}

/// <summary>
/// <c>mcp-access-requests.json</c> exists and does not parse, so the store refuses to write it.
/// An <see cref="IOException"/>, as <see cref="UnreadableMcpGrantFileException"/> is.
/// </summary>
public sealed class UnreadableMcpAccessRequestFileException : IOException
{
    public UnreadableMcpAccessRequestFileException(string path)
        : base($"The MCP access requests file {path} does not parse, so it is not written over. Mend it or move it aside.")
    {
        Path = path;
    }

    /// <summary>The file that does not parse.</summary>
    public string Path { get; }
}
