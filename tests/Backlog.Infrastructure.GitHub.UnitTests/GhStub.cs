using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// A stand-in for the <c>gh</c> CLI: a throwaway copy of <c>gh-stub.cmd</c> in a
/// directory of its own, plus the files that tell it what to answer and the files it
/// writes down what it was asked in.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GhCliTransport"/> starts a process, so the one seam it offers is the
/// executable it starts — its <c>executable</c> constructor parameter. There is no
/// handler to hand it and nothing to intercept, which is why these tests drive a real
/// child process rather than a fake: what is being pinned is the conversation with a
/// command line, and a fake would only pin our own idea of it.
/// </para>
/// <para>
/// Each stub copies the script into a directory of its own rather than running the one
/// in the test output, because the script is driven by the files beside it and two
/// tests running side by side would otherwise be reading each other's answers. The copy
/// also fails loudly if the project ever stops copying the script to the output
/// directory, which is the only way that wiring can be noticed.
/// </para>
/// <para>
/// Windows only. That is what the product ships on and what the .NET job in
/// <c>.github/workflows/pull-request.yml</c> runs, so a batch file costs nothing here
/// and a second project to build one executable would.
/// </para>
/// </remarks>
internal sealed class GhStub : IDisposable
{
    private const string ScriptName = "gh-stub.cmd";

    private const string EndOfCall = "--- end of call ---";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "gh-cli-stub-" + Guid.NewGuid().ToString("N"));

    public GhStub()
    {
        Directory.CreateDirectory(_directory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, ScriptName), Executable);
    }

    /// <summary>What <see cref="GhCliTransport"/> should be pointed at.</summary>
    public string Executable => Path.Combine(_directory, ScriptName);

    /// <summary>A transport that talks to this stub instead of to <c>gh</c>.</summary>
    public GhCliTransport Transport() => new(Executable);

    /// <summary>A credential source that talks to this stub instead of to
    /// <c>gh</c>. The same seam for the same reason: the executable is all there
    /// is.</summary>
    public GhCliAccountSource Source(TimeProvider? time = null) => new(Executable, time);

    /// <summary>Every call the transport made, argv element by argv element, in the
    /// order they were made.</summary>
    public IReadOnlyList<string[]> Calls
    {
        get
        {
            var path = Path.Combine(_directory, "args.txt");
            if (!File.Exists(path)) return [];

            var calls = new List<string[]>();
            var current = new List<string>();

            foreach (var line in File.ReadAllLines(path))
            {
                if (line == EndOfCall)
                {
                    calls.Add([.. current]);
                    current.Clear();
                    continue;
                }

                current.Add(line);
            }

            return calls;
        }
    }

    /// <summary>The argv of the one call that was made, failing when there was not
    /// exactly one.</summary>
    public string[] OnlyCall => Assert.Single(Calls);

    /// <summary>What the transport piped in, or null when it piped nothing.</summary>
    public string? StandardInput
    {
        get
        {
            var path = Path.Combine(_directory, "stdin.txt");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
    }

    /// <summary>Answers every call with this on stdout, and exit code 0.</summary>
    public GhStub Answers(string standardOutput)
    {
        File.WriteAllText(Path.Combine(_directory, "stdout.txt"), standardOutput);
        Set("exit-code.txt", null);
        return this;
    }

    /// <summary>Ends every call with this exit code, having written
    /// <paramref name="standardError"/> — empty for the CLI that fails and says
    /// nothing about it.</summary>
    public GhStub Fails(int exitCode = 1, string standardError = "")
    {
        Set("exit-code.txt", exitCode.ToString());
        Set("stderr.txt", standardError.Length == 0 ? null : standardError);
        return this;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory that outlives the run is not worth failing a test over.
        }
    }

    private void Set(string name, string? content)
    {
        var path = Path.Combine(_directory, name);

        if (content is null)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        File.WriteAllText(path, content);
    }
}
