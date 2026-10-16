using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Backlog.Infrastructure.Devbook.Archify;

/// <summary>One chapter diagram's specification to draw, at the type and quality
/// its filename names, and the clone it was read out of — whose own generator is
/// the one it was authored against.</summary>
public sealed record ArchifyRenderRequest(string SpecificationPath, string Type, string Quality, string? RepositoryRoot);

/// <summary>What drawing a specification produced: the whole self-contained
/// document and where it is cached, or why there is none.</summary>
public sealed record ArchifyRender(string? Html, string? ArtifactPath, string? Failure)
{
    public static ArchifyRender Failed(string reason) => new(null, null, reason);
}

/// <summary>
/// Draws a chapter diagram's Archify artifact from its committed specification,
/// into the app's devbook cache folder, beside the databases (local ADRs 0015 and
/// 0034).
/// <para>
/// The renders used to be committed beside their specifications, roughly 700 KB
/// each and more than half the tracked tree. What makes drawing them on demand
/// affordable is that one render takes well under a second, and almost every
/// lookup is for a specification somebody already drew. So the artifact is filed
/// under a hash of everything it depends on — the specification's text, its type,
/// its quality and the generator's identity — and the generator runs only for a
/// combination nobody has drawn on this machine yet. An edited specification or a
/// bumped generator is a new key, and the old file is simply never asked for again.
/// </para>
/// <para>
/// Only successes are kept. A failure is usually the machine's state — Node not
/// installed yet, a timeout under load — and remembering it would keep answering
/// "no" after the reason went away.
/// </para>
/// </summary>
public sealed class ArchifyArtifactRenderer
{
    /// <summary>How many artifacts are kept on disk. A repository's whole devbook
    /// is a few dozen diagrams, so this holds two or three generator versions'
    /// worth before the oldest are dropped.</summary>
    private const int Keep = 128;

    private const int KeyLength = 16;

    /// <summary>The generator's parts a picture depends on, read for its identity:
    /// the entry point, the template and its assets, the renderers, the schemas
    /// and the brand marks. Its documentation and examples are not among them, so
    /// a README edit does not redraw every diagram.</summary>
    private static readonly string[] IdentityFolders = ["assets", "bin", "brand-marks", "renderers", "schemas"];

    private const string Manifest = "package.json";

    private readonly Func<string?> _cacheDirectory;
    private readonly string? _bundledGenerator;
    private readonly IArchifyGeneratorRunner _runner;
    private readonly TimeSpan _timeout;

    /// <summary>A generator's identity, read once per generator per process. A
    /// generator updated while the app runs is noticed on the next start.</summary>
    private readonly ConcurrentDictionary<string, string> _identities = new(StringComparer.Ordinal);

    /// <summary>Renders under way, by artifact path, so two views of one diagram
    /// start the generator once.</summary>
    private readonly ConcurrentDictionary<string, Lazy<Task<ArchifyRender>>> _inFlight = new(StringComparer.Ordinal);

    /// <param name="cacheDirectory">Where artifacts are kept, read on every call so
    /// a moved Storage setting takes effect at once; null while there is no app
    /// storage, and then every render says so.</param>
    /// <param name="bundledGenerator">The <c>bin/archify.mjs</c> shipped beside the
    /// application, for a clone that carries no generator of its own.</param>
    /// <param name="runner">What starts the generator.</param>
    /// <param name="timeout">How long one generator run may take before it is
    /// stopped.</param>
    public ArchifyArtifactRenderer(Func<string?> cacheDirectory, string? bundledGenerator, IArchifyGeneratorRunner runner, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(cacheDirectory);
        ArgumentNullException.ThrowIfNull(runner);

        _cacheDirectory = cacheDirectory;
        _bundledGenerator = bundledGenerator;
        _runner = runner;
        _timeout = timeout;
    }

    /// <summary>The renderer the application composes: the devbook cache folder
    /// the composition root configured, the generator bundled beside the
    /// executable, and Node on the PATH.</summary>
    public static ArchifyArtifactRenderer ForApplication() =>
        new(
            () => DevbookDatabaseLocation.ArchifyRendersDirectory,
            BundledGenerator(AppContext.BaseDirectory),
            new NodeArchifyGeneratorRunner(),
            TimeSpan.FromSeconds(60));

    /// <summary>The generator the desktop project bundles beside the executable as
    /// <c>archify\</c>, or null where there is none.</summary>
    public static string? BundledGenerator(string baseDirectory)
    {
        var bundled = Path.Combine(baseDirectory, "archify", "bin", "archify.mjs");
        return File.Exists(bundled) ? bundled : null;
    }

    /// <summary>
    /// The generator to draw with: the clone's own <c>tools/archify</c> first,
    /// because it is the copy the specification was authored and validated
    /// against, and the one bundled beside the app for a clone that carries none.
    /// Null when neither exists.
    /// </summary>
    public string? LocateGenerator(string? repositoryRoot)
    {
        if (!string.IsNullOrWhiteSpace(repositoryRoot))
        {
            var vendored = Path.Combine(repositoryRoot, "tools", "archify", "bin", "archify.mjs");
            if (File.Exists(vendored)) return vendored;
        }

        return _bundledGenerator is not null && File.Exists(_bundledGenerator) ? _bundledGenerator : null;
    }

    /// <summary>The key an artifact is filed under: the first sixteen hexadecimal
    /// characters of the SHA-256 over its type, quality, generator identity and the
    /// specification's text, LF-normalised so a CRLF checkout does not read as a
    /// re-authoring.</summary>
    public static string KeyFor(string specification, string type, string quality, string generatorIdentity)
    {
        var text = string.Join('\n', type, quality, generatorIdentity, specification.Replace("\r\n", "\n", StringComparison.Ordinal));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..KeyLength];
    }

    /// <summary>The artifact already drawn for this specification as it stands, or
    /// null — without starting anything. What a synchronous lookup can
    /// answer.</summary>
    public ArchifyRender? TryCached(ArchifyRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (Plan(request) is not { Artifact: { } artifact }) return null;

        return TryRead(artifact) is { } html ? new ArchifyRender(html, artifact, null) : null;
    }

    /// <summary>The artifact for this specification, drawn now if nobody has drawn
    /// it yet. Returns why it could not be drawn rather than throwing.</summary>
    public async Task<ArchifyRender> RenderAsync(ArchifyRenderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan = Plan(request);
        if (plan.Failure is { } failure) return ArchifyRender.Failed(failure);

        if (TryRead(plan.Artifact!) is { } html) return new ArchifyRender(html, plan.Artifact, null);

        // Shared rather than per caller, and not cancelled by any one of them: a
        // view that goes away must not leave the next one to start over, and the
        // generator's own timeout already bounds the run.
        var render = _inFlight.GetOrAdd(plan.Artifact!, artifact => new Lazy<Task<ArchifyRender>>(() => GenerateOnceAsync(artifact, request, plan)));

        return await render.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A generator run that takes itself out of the in-flight table when
    /// it ends, whoever is still waiting, so a failure is never handed to the next
    /// caller as an answer.</summary>
    private async Task<ArchifyRender> GenerateOnceAsync(string artifact, ArchifyRenderRequest request, RenderPlan plan)
    {
        try
        {
            return await GenerateAsync(request, plan).ConfigureAwait(false);
        }
        finally
        {
            _inFlight.TryRemove(artifact, out _);
        }
    }

    /// <summary>One generator run into a partial file, moved into place only once
    /// it is whole so a reader never loads half of one.</summary>
    private async Task<ArchifyRender> GenerateAsync(ArchifyRenderRequest request, RenderPlan plan)
    {
        var artifact = plan.Artifact!;
        var partial = Path.Combine(plan.CacheDirectory!, $"{Path.GetFileNameWithoutExtension(artifact)}.{Guid.NewGuid():N}.partial.html");

        try
        {
            Directory.CreateDirectory(plan.CacheDirectory!);

            var failure = await _runner.DeliverAsync(
                new ArchifyDelivery(plan.Generator!, request.Type, request.SpecificationPath, partial, request.Quality, _timeout))
                .ConfigureAwait(false);

            if (failure is not null)
            {
                TryDelete(partial);
                return ArchifyRender.Failed(failure);
            }

            File.Move(partial, artifact, overwrite: true);
            Prune(plan.CacheDirectory!);

            return new ArchifyRender(await File.ReadAllTextAsync(artifact).ConfigureAwait(false), artifact, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(partial);
            return ArchifyRender.Failed($"The Archify artifact could not be written: {ex.Message}");
        }
    }

    /// <summary>Where this request's artifact would be, or why there is nowhere.
    /// Reads the specification, because its text is half the key.</summary>
    private RenderPlan Plan(ArchifyRenderRequest request)
    {
        if (_cacheDirectory() is not { Length: > 0 } cache)
        {
            return RenderPlan.Failed("The app has no devbook cache folder configured, so there is nowhere to keep an Archify artifact.");
        }

        if (LocateGenerator(request.RepositoryRoot) is not { } generator)
        {
            return RenderPlan.Failed("No Archify generator was found: the repository has no tools/archify, and none is bundled with the app.");
        }

        string specification;
        try
        {
            specification = File.ReadAllText(request.SpecificationPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return RenderPlan.Failed($"The Archify specification could not be read: {ex.Message}");
        }

        string identity;
        try
        {
            identity = _identities.GetOrAdd(generator, GeneratorIdentity);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return RenderPlan.Failed($"The Archify generator could not be read: {ex.Message}");
        }

        var key = KeyFor(specification, request.Type, request.Quality, identity);
        var stem = Path.GetFileNameWithoutExtension(request.SpecificationPath);

        return new RenderPlan(cache, generator, Path.Combine(cache, $"{stem}-{key}.html"), null);
    }

    /// <summary>
    /// What a generator is, for the cache key: a hash over the relative path and
    /// bytes of every file a render depends on, plus its package manifest.
    /// <para>
    /// The content and not the version, because this repository patches the
    /// vendored template in place — the looping trace is one such patch — without
    /// the upstream version moving. The clone's copy and the copy bundled beside
    /// the app hash alike when they are alike, so either one's artifacts serve the
    /// other.
    /// </para>
    /// </summary>
    private static string GeneratorIdentity(string generator)
    {
        var root = Path.GetDirectoryName(Path.GetDirectoryName(generator))!;

        var files = IdentityFolders
            .Select(folder => Path.Combine(root, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            .Append(Path.Combine(root, Manifest))
            .Where(File.Exists)
            .Select(file => (Relative: Path.GetRelativePath(root, file).Replace('\\', '/'), File: file))
            .OrderBy(entry => entry.Relative, StringComparer.Ordinal);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var (relative, file) in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(relative + '\n'));
            hash.AppendData(File.ReadAllBytes(file));
            hash.AppendData("\n"u8);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Keeps the newest artifacts and drops the rest. The folder is a
    /// cache: anything missing is drawn again.</summary>
    private static void Prune(string cache)
    {
        try
        {
            var stale = new DirectoryInfo(cache)
                .GetFiles("*.html")
                .Where(file => !file.Name.EndsWith(".partial.html", StringComparison.Ordinal))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Skip(Keep);

            foreach (var file in stale) TryDelete(file.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A cache that could not be tidied is still a working cache.
        }
    }

    private static string? TryRead(string file)
    {
        try
        {
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record RenderPlan(string? CacheDirectory, string? Generator, string? Artifact, string? Failure)
    {
        public static RenderPlan Failed(string reason) => new(null, null, null, reason);
    }
}
