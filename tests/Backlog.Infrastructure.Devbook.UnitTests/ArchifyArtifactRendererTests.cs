using System.Diagnostics;
using Backlog.Infrastructure.Devbook.Archify;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// A chapter's Archify artifact is drawn on demand from its committed
/// specification and kept in the app's devbook cache folder (local ADR 0029).
/// What is worth pinning is the cache: where it writes, what it is keyed on, that
/// a hit never starts Node, and that a failure is never remembered.
/// <para>
/// The generator is a recording fake here, because the decisions under test are
/// the renderer's and not Archify's. One test at the end runs the vendored
/// generator for real, where Node is installed.
/// </para>
/// </summary>
public sealed class ArchifyArtifactRendererTests : IDisposable
{
    private const string Specification = """{"type":"workflow","title":"Orders","meta":{"animation":"trace"}}""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-archify-renderer-tests", Guid.NewGuid().ToString("N"));

    private string Cache => Path.Combine(_root, "devbook-cache", DevbookDatabaseLocation.ArchifyRendersFolderName);

    private string Clone => Path.Combine(_root, "clone");

    public ArchifyArtifactRendererTests()
    {
        Directory.CreateDirectory(Clone);
        WriteGenerator(Path.Combine(Clone, "tools", "archify"), "template v1");
    }

    [Fact]
    public async Task A_render_is_written_under_the_devbook_cache_folder_and_never_beside_the_specification()
    {
        var runner = new RecordingGenerator();
        var renderer = Renderer(runner);
        var spec = WriteSpecification("flow.1.workflow.json", Specification);

        var render = await renderer.RenderAsync(Request(spec), TestContext.Current.CancellationToken);

        Assert.Null(render.Failure);
        Assert.Equal(RecordingGenerator.Document, render.Html);
        Assert.NotNull(render.ArtifactPath);
        Assert.StartsWith(Cache + Path.DirectorySeparatorChar, render.ArtifactPath, StringComparison.Ordinal);
        Assert.True(File.Exists(render.ArtifactPath));

        // Nothing lands in the repository: the folder beside the specification
        // holds the specification and nothing else.
        Assert.Equal([spec], Directory.GetFiles(Path.GetDirectoryName(spec)!));
    }

    [Fact]
    public async Task A_second_lookup_of_the_same_specification_is_served_from_the_cache_without_starting_the_generator()
    {
        var runner = new RecordingGenerator();
        var renderer = Renderer(runner);
        var spec = WriteSpecification("flow.1.workflow.json", Specification);

        Assert.Null(renderer.TryCached(Request(spec)));

        var first = await renderer.RenderAsync(Request(spec), TestContext.Current.CancellationToken);
        var cached = renderer.TryCached(Request(spec));
        var second = await renderer.RenderAsync(Request(spec), TestContext.Current.CancellationToken);

        Assert.Equal(1, runner.Runs);
        Assert.NotNull(cached);
        Assert.Equal(first.ArtifactPath, cached.ArtifactPath);
        Assert.Equal(RecordingGenerator.Document, cached.Html);
        Assert.Equal(first.ArtifactPath, second.ArtifactPath);
    }

    /// <summary>The cache is a folder, not a process's memory: a restarted app
    /// finds what the last one drew.</summary>
    [Fact]
    public async Task An_unchanged_specification_reuses_the_cached_file_in_a_new_process()
    {
        var spec = WriteSpecification("flow.1.workflow.json", Specification);
        await Renderer(new RecordingGenerator()).RenderAsync(Request(spec), TestContext.Current.CancellationToken);

        var runner = new RecordingGenerator();
        var restarted = Renderer(runner);

        Assert.NotNull(restarted.TryCached(Request(spec)));
        var render = await restarted.RenderAsync(Request(spec), TestContext.Current.CancellationToken);

        Assert.Null(render.Failure);
        Assert.Equal(0, runner.Runs);
    }

    [Fact]
    public async Task An_edited_specification_is_drawn_again()
    {
        var runner = new RecordingGenerator();
        var renderer = Renderer(runner);
        var spec = WriteSpecification("flow.1.workflow.json", Specification);

        var before = await renderer.RenderAsync(Request(spec), TestContext.Current.CancellationToken);
        File.WriteAllText(spec, Specification.Replace("Orders", "Shipments", StringComparison.Ordinal));

        Assert.Null(renderer.TryCached(Request(spec)));
        var after = await renderer.RenderAsync(Request(spec), TestContext.Current.CancellationToken);

        Assert.Equal(2, runner.Runs);
        Assert.NotEqual(before.ArtifactPath, after.ArtifactPath);
    }

    /// <summary>
    /// The four things a picture depends on, each of which has to move the key on
    /// its own. The specification and its type and quality are what is drawn and
    /// how; the generator's identity is what draws it, so a bumped or patched
    /// Archify re-renders rather than serving the old renderer's output.
    /// </summary>
    [Fact]
    public void The_cache_key_changes_with_the_specification_its_type_its_quality_and_the_generator()
    {
        var key = ArchifyArtifactRenderer.KeyFor(Specification, "workflow", "showcase", "generator-a");

        Assert.Equal(key, ArchifyArtifactRenderer.KeyFor(Specification, "workflow", "showcase", "generator-a"));
        Assert.NotEqual(key, ArchifyArtifactRenderer.KeyFor(Specification + " ", "workflow", "showcase", "generator-a"));
        Assert.NotEqual(key, ArchifyArtifactRenderer.KeyFor(Specification, "architecture", "showcase", "generator-a"));
        Assert.NotEqual(key, ArchifyArtifactRenderer.KeyFor(Specification, "workflow", "standard", "generator-a"));
        Assert.NotEqual(key, ArchifyArtifactRenderer.KeyFor(Specification, "workflow", "showcase", "generator-b"));
    }

    /// <summary>A Windows checkout hands the specification over with CRLF, and a
    /// line ending nobody edited must not read as a re-authoring.</summary>
    [Fact]
    public void The_cache_key_ignores_the_line_endings_a_checkout_chose()
    {
        Assert.Equal(
            ArchifyArtifactRenderer.KeyFor("{\n  \"type\": \"workflow\"\n}", "workflow", "showcase", "g"),
            ArchifyArtifactRenderer.KeyFor("{\r\n  \"type\": \"workflow\"\r\n}", "workflow", "showcase", "g"));
    }

    [Fact]
    public async Task A_changed_generator_is_a_new_key_and_draws_the_specification_again()
    {
        var spec = WriteSpecification("flow.1.workflow.json", Specification);
        await Renderer(new RecordingGenerator()).RenderAsync(Request(spec), TestContext.Current.CancellationToken);

        // Archify moved forward, or its template was patched in place.
        WriteGenerator(Path.Combine(Clone, "tools", "archify"), "template v2");

        var runner = new RecordingGenerator();
        var renderer = Renderer(runner);

        Assert.Null(renderer.TryCached(Request(spec)));
        await renderer.RenderAsync(Request(spec), TestContext.Current.CancellationToken);
        Assert.Equal(1, runner.Runs);
    }

    /// <summary>
    /// Only successes are kept. A failure is usually the machine's state — Node not
    /// installed yet, a timeout under load — and remembering it would keep
    /// answering "no" after the reason went away.
    /// </summary>
    [Fact]
    public async Task A_failed_render_says_why_and_is_not_cached()
    {
        var runner = new RecordingGenerator { Failure = "The Archify generator rejected this specification: - edge e1 has no target" };
        var renderer = Renderer(runner);
        var spec = WriteSpecification("flow.1.workflow.json", Specification);

        var failed = await renderer.RenderAsync(Request(spec), TestContext.Current.CancellationToken);

        Assert.Null(failed.Html);
        Assert.Null(failed.ArtifactPath);
        Assert.Equal(runner.Failure, failed.Failure);
        Assert.Null(renderer.TryCached(Request(spec)));
        Assert.False(Directory.Exists(Cache) && Directory.EnumerateFiles(Cache, "*.html").Any());

        runner.Failure = null;
        var retried = await renderer.RenderAsync(Request(spec), TestContext.Current.CancellationToken);

        Assert.Equal(2, runner.Runs);
        Assert.Null(retried.Failure);
        Assert.Equal(RecordingGenerator.Document, retried.Html);
    }

    [Fact]
    public async Task With_no_generator_in_the_clone_or_beside_the_app_the_render_says_so_without_starting_anything()
    {
        var runner = new RecordingGenerator();
        var bare = Path.Combine(_root, "bare-clone");
        Directory.CreateDirectory(bare);
        var spec = WriteSpecification("flow.1.workflow.json", Specification);

        var renderer = new ArchifyArtifactRenderer(() => Cache, bundledGenerator: null, runner, TimeSpan.FromSeconds(5));
        var render = await renderer.RenderAsync(Request(spec, bare), TestContext.Current.CancellationToken);

        Assert.Equal(0, runner.Runs);
        Assert.Null(render.Html);
        Assert.Contains("generator", render.Failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task With_no_app_storage_configured_the_render_says_so_without_starting_anything()
    {
        var runner = new RecordingGenerator();
        var spec = WriteSpecification("flow.1.workflow.json", Specification);

        var renderer = new ArchifyArtifactRenderer(() => null, bundledGenerator: null, runner, TimeSpan.FromSeconds(5));
        var render = await renderer.RenderAsync(Request(spec), TestContext.Current.CancellationToken);

        Assert.Equal(0, runner.Runs);
        Assert.NotNull(render.Failure);
        Assert.Null(renderer.TryCached(Request(spec)));
    }

    /// <summary>The clone's own generator first, because it is the copy the
    /// specification was authored against; the one bundled beside the app is for a
    /// clone that carries none.</summary>
    [Fact]
    public void The_clone_generator_is_preferred_and_the_bundled_copy_is_the_fallback()
    {
        var bundled = Path.Combine(_root, "app", "archify");
        WriteGenerator(bundled, "bundled");
        var bundledCli = Path.Combine(bundled, "bin", "archify.mjs");

        var renderer = new ArchifyArtifactRenderer(() => Cache, bundledCli, new RecordingGenerator(), TimeSpan.FromSeconds(5));

        Assert.Equal(Path.Combine(Clone, "tools", "archify", "bin", "archify.mjs"), renderer.LocateGenerator(Clone));

        var bare = Path.Combine(_root, "bare-clone");
        Directory.CreateDirectory(bare);
        Assert.Equal(bundledCli, renderer.LocateGenerator(bare));
        Assert.Equal(bundledCli, renderer.LocateGenerator(null));
    }

    [Fact]
    public async Task Two_lookups_at_once_start_the_generator_once()
    {
        var gate = new TaskCompletionSource();
        var runner = new RecordingGenerator { Gate = gate.Task };
        var renderer = Renderer(runner);
        var spec = WriteSpecification("flow.1.workflow.json", Specification);

        var first = renderer.RenderAsync(Request(spec), TestContext.Current.CancellationToken);
        var second = renderer.RenderAsync(Request(spec), TestContext.Current.CancellationToken);
        gate.SetResult();

        var renders = await Task.WhenAll(first, second);

        Assert.Equal(1, runner.Runs);
        Assert.All(renders, render => Assert.Equal(RecordingGenerator.Document, render.Html));
    }

    /// <summary>No Node on the machine is the commonest failure, and the reason
    /// has to say so rather than surface a Win32 error code.</summary>
    [Fact]
    public async Task The_node_runner_reports_a_missing_node_as_a_reason()
    {
        var runner = new NodeArchifyGeneratorRunner("backlog-no-such-node-" + Guid.NewGuid().ToString("N"));
        var spec = WriteSpecification("flow.1.workflow.json", Specification);

        var failure = await runner.DeliverAsync(
            new ArchifyDelivery(Path.Combine(Clone, "tools", "archify", "bin", "archify.mjs"), "workflow", spec, Path.Combine(_root, "out.html"), "showcase", TimeSpan.FromSeconds(5)),
            TestContext.Current.CancellationToken);

        Assert.NotNull(failure);
        Assert.Contains("Node.js", failure, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_root, "out.html")));
    }

    /// <summary>
    /// The vendored generator accepts a real committed specification and the
    /// artifact lands in the cache. Skipped, not failed, where Node is absent:
    /// a machine without Node is the case the mermaid fallback exists for.
    /// </summary>
    [Fact]
    public async Task The_vendored_generator_draws_a_committed_specification_into_the_cache()
    {
        Assert.SkipUnless(NodeInstalled(), "Node.js is not installed.");

        var repository = RepositoryRoot.Root.FullName;
        var spec = Path.Combine(repository, ".devbook", "arc42", "_archify", "03-context-and-scope.1.architecture.json");
        Assert.SkipUnless(File.Exists(spec), "The committed specification this test draws has moved.");

        var renderer = new ArchifyArtifactRenderer(() => Cache, bundledGenerator: null, new NodeArchifyGeneratorRunner(), TimeSpan.FromSeconds(120));
        var request = new ArchifyRenderRequest(spec, "architecture", "showcase", repository);

        var render = await renderer.RenderAsync(request, TestContext.Current.CancellationToken);

        Assert.Null(render.Failure);
        Assert.Contains("<html", render.Html!, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(Cache, render.ArtifactPath, StringComparison.Ordinal);
        Assert.NotNull(renderer.TryCached(request));
    }

    /// <summary>A specification Archify rejects comes back as a reason, and
    /// nothing half-written is left in the cache to be served later.</summary>
    [Fact]
    public async Task A_specification_the_vendored_generator_rejects_is_a_reason_and_leaves_nothing_cached()
    {
        Assert.SkipUnless(NodeInstalled(), "Node.js is not installed.");

        var repository = RepositoryRoot.Root.FullName;
        var spec = WriteSpecification("broken.1.workflow.json", """{"type":"workflow","nodes":"not a list"}""");

        var renderer = new ArchifyArtifactRenderer(() => Cache, bundledGenerator: null, new NodeArchifyGeneratorRunner(), TimeSpan.FromSeconds(120));
        var request = new ArchifyRenderRequest(spec, "workflow", "showcase", repository);

        var render = await renderer.RenderAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(render.Failure);
        Assert.Null(render.Html);
        Assert.Null(renderer.TryCached(request));
        Assert.False(Directory.Exists(Cache) && Directory.EnumerateFiles(Cache, "*.html").Any());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private ArchifyArtifactRenderer Renderer(IArchifyGeneratorRunner runner) =>
        new(() => Cache, bundledGenerator: null, runner, TimeSpan.FromSeconds(5));

    private ArchifyRenderRequest Request(string spec, string? root = null) =>
        new(spec, "workflow", "showcase", root ?? Clone);

    private string WriteSpecification(string name, string text)
    {
        var directory = Path.Combine(Clone, ".devbook", "domain", "orders", "_archify");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, name);
        File.WriteAllText(file, text);
        return file;
    }

    /// <summary>The parts of a generator its identity is read from: the entry
    /// point, the template every artifact is rendered from, and the package
    /// manifest naming the version.</summary>
    private static void WriteGenerator(string root, string template)
    {
        Directory.CreateDirectory(Path.Combine(root, "bin"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        File.WriteAllText(Path.Combine(root, "bin", "archify.mjs"), "// archify");
        File.WriteAllText(Path.Combine(root, "assets", "template.html"), template);
        File.WriteAllText(Path.Combine(root, "package.json"), """{"name":"archify","version":"2.16.0"}""");
    }

    private static bool NodeInstalled()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("node", "--version")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            });

            process?.WaitForExit(10_000);
            return process is { ExitCode: 0 };
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}

/// <summary>A generator that counts how often it is started and writes a fixed
/// document where <c>deliver</c> would — or fails with the reason it is
/// given.</summary>
file sealed class RecordingGenerator : IArchifyGeneratorRunner
{
    internal const string Document = "<!doctype html><html><body>Archify</body></html>";

    private int _runs;

    internal int Runs => _runs;

    internal string? Failure { get; set; }

    internal Task? Gate { get; init; }

    public async Task<string?> DeliverAsync(ArchifyDelivery delivery, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _runs);
        if (Gate is not null) await Gate.WaitAsync(cancellationToken);
        if (Failure is not null) return Failure;

        await File.WriteAllTextAsync(delivery.OutputPath, Document, cancellationToken);
        return null;
    }
}
