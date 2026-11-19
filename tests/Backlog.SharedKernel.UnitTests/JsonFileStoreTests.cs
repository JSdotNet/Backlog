using System.Text.Json;
using Backlog.SharedKernel.Storage;
using Backlog.Tests;

namespace Backlog.SharedKernel.UnitTests;

/// <summary>
/// The file half the per-user JSON settings stores share: a missing, locked or corrupt
/// file reads as nothing rather than failing the app's start, a refused write answers
/// false rather than throwing, and an empty document deletes the file.
/// </summary>
public sealed class JsonFileStoreTests : IDisposable
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly TemporaryDirectory _folder = new("json-file-store-tests");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Creates_the_files_folder()
    {
        var path = _folder.Combine("nested", "settings.json");

        _ = new JsonFileStore<Document>(path, Options);

        Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_missing_file_loads_as_nothing()
    {
        var store = new JsonFileStore<Document>(_folder.Combine("settings.json"), Options);

        Assert.Null(store.Load());
    }

    [Fact]
    public void A_saved_document_loads_back_in_the_options_shape()
    {
        var path = _folder.Combine("settings.json");
        var store = new JsonFileStore<Document>(path, Options);

        Assert.True(store.TrySave(new Document { ServiceUrl = "https://sync.example" }));

        Assert.Equal("https://sync.example", store.Load()?.ServiceUrl);
        Assert.Contains("\"serviceUrl\": \"https://sync.example\"", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    [InlineData("null")]
    public void A_file_that_is_not_the_document_loads_as_nothing(string content)
    {
        var path = _folder.Combine("settings.json");
        File.WriteAllText(path, content);

        Assert.Null(new JsonFileStore<Document>(path, Options).Load());
    }

    [Fact]
    public void Saving_nothing_deletes_the_file()
    {
        var path = _folder.Combine("settings.json");
        var store = new JsonFileStore<Document>(path, Options);
        store.TrySave(new Document { ServiceUrl = "https://sync.example" });

        Assert.True(store.TrySave(null));

        Assert.False(File.Exists(path));
        Assert.True(store.TrySave(null));
    }

    [Fact]
    public void A_locked_file_loads_as_nothing_and_refuses_the_save()
    {
        var path = _folder.Combine("settings.json");
        var store = new JsonFileStore<Document>(path, Options);
        store.TrySave(new Document { ServiceUrl = "https://sync.example" });

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Null(store.Load());
            Assert.False(store.TrySave(new Document { ServiceUrl = "https://other.example" }));
        }

        Assert.Equal("https://sync.example", store.Load()?.ServiceUrl);
    }

    [Fact]
    public void A_path_that_is_a_folder_refuses_the_save()
    {
        var path = _folder.Combine("settings.json");
        Directory.CreateDirectory(path);

        Assert.False(new JsonFileStore<Document>(path, Options).TrySave(new Document()));
    }

    public sealed class Document
    {
        public string? ServiceUrl { get; init; }
    }
}
