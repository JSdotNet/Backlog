using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// What the workflows under <c>.github/workflows/</c> promise about themselves
/// (issue #748): no job holds a runner for GitHub's six-hour default, a nightly
/// run exercises the suite at the parallelism developer machines use, and every
/// Node step runs one version.
///
/// <para>Nothing else can prove these. A workflow runs only on GitHub, and each of
/// them fails quietly: a missing timeout is noticed when a hung workload install has
/// already blocked the self-hosted runner, and a nightly run that passes
/// <c>--parallel none</c> is green while testing nothing it was added for. The
/// files are read as text, line by line, because the repository takes no YAML
/// dependency for one test class; the shapes it reads are the two-space jobs and
/// four-space job keys every workflow here already uses.</para>
/// </summary>
public class ContinuousIntegrationWorkflowTests
{
    private static readonly string[] WorkflowFolder = [".github", "workflows"];

    /// <summary>The nightly run at default parallelism.</summary>
    private const string NightlyWorkflow = "nightly-tests.yml";

    /// <summary>
    /// The timeout each workflow's jobs carry: 60 for release and deploy jobs,
    /// whose workload installs and provisioning are slow when healthy, and for the
    /// auto-merge gate, which waits out every other check on the pull request and
    /// so must outlast their 30 plus their time in the queue; 30 for the rest, the
    /// nightly test run included. A workflow added without a row
    /// here fails the test, so the next one gets a decision rather than the
    /// platform default.
    /// </summary>
    private static readonly Dictionary<string, int> ExpectedTimeoutMinutes = new(StringComparer.Ordinal)
    {
        ["pull-request.yml"] = 30,
        ["devbook-metadata.yml"] = 30,
        ["devbook-meta.yml"] = 30,
        ["codeql.yml"] = 30,
        ["app-insights-exceptions.yml"] = 30,
        [NightlyWorkflow] = 30,

        ["release-desktop.yml"] = 60,
        ["release-mobile.yml"] = 60,
        ["deploy-foundry.yml"] = 60,
        ["deploy-sync.yml"] = 60,
        ["deploy-all.yml"] = 60,
        ["auto-merge.yml"] = 60,
    };

    /// <summary>The workflows whose <c>setup-node</c> steps must agree on one version.</summary>
    private static readonly string[] NodeWorkflows =
        ["pull-request.yml", "devbook-metadata.yml", NightlyWorkflow];

    [Fact]
    public void Every_workflow_has_a_timeout_decision()
    {
        var unlisted = WorkflowFiles()
            .Select(file => Path.GetFileName(file))
            .Where(name => !ExpectedTimeoutMinutes.ContainsKey(name))
            .ToArray();

        Assert.True(
            unlisted.Length == 0,
            $"{string.Join(", ", unlisted)} has no row in ExpectedTimeoutMinutes. Decide the job timeout "
            + "for the new workflow (30, or 60 for release and deploy) and add it there.");
    }

    /// <summary>
    /// A job on a runner declares <c>timeout-minutes</c>; a reusable-workflow call
    /// does not, because GitHub rejects the key there and the called workflow's own
    /// job timeout applies under both dispatch and call.
    /// </summary>
    [Fact]
    public void Every_job_on_a_runner_has_its_timeout()
    {
        var problems = new List<string>();

        foreach (var file in WorkflowFiles())
        {
            var name = Path.GetFileName(file);
            if (!ExpectedTimeoutMinutes.TryGetValue(name, out var expected))
            {
                continue;
            }

            foreach (var job in Jobs(File.ReadAllLines(file)))
            {
                var timeout = JobKey(job, "timeout-minutes");

                if (JobKey(job, "uses") is not null)
                {
                    if (timeout is not null)
                    {
                        problems.Add($"{name}: '{job.Name}' calls a reusable workflow and cannot carry timeout-minutes.");
                    }

                    continue;
                }

                if (JobKey(job, "runs-on") is null)
                {
                    continue;
                }

                if (timeout != expected.ToString(System.Globalization.CultureInfo.InvariantCulture))
                {
                    problems.Add($"{name}: '{job.Name}' has timeout-minutes '{timeout ?? "(none)"}', expected {expected}.");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// The pull request run is serial, so the <c>DisableParallelization</c>
    /// collections and the environment-variable test of issue #724 are only ever
    /// parallel-tested here.
    /// </summary>
    [Fact]
    public void A_nightly_run_tests_at_default_parallelism()
    {
        var workflow = Workflow(NightlyWorkflow);

        Assert.Matches(new Regex(@"^  schedule:\s*$", RegexOptions.Multiline), workflow);
        Assert.Matches(new Regex(@"^    - cron: '[^']+'\s*$", RegexOptions.Multiline), workflow);
        Assert.Matches(new Regex(@"^  workflow_dispatch:\s*$", RegexOptions.Multiline), workflow);

        var run = Step(workflow, "Run tests");
        Assert.Contains("--solution Backlog.WithoutAppHeads.slnf", run, StringComparison.Ordinal);
        Assert.Contains("--report-trx", run, StringComparison.Ordinal);
        Assert.DoesNotContain("--parallel", run, StringComparison.Ordinal);
        Assert.DoesNotContain("--max-parallel-test-modules", run, StringComparison.Ordinal);

        Assert.Contains("if: always()", Step(workflow, "Upload test results"), StringComparison.Ordinal);
        Assert.Contains("actions/setup-node@", Step(workflow, "Setup Node.js"), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_node_step_runs_one_node_version()
    {
        var versions = NodeWorkflows
            .SelectMany(name => Regex.Matches(Workflow(name), @"node-version: '([^']+)'")
                .Select(match => $"{name}: {match.Groups[1].Value}"))
            .ToArray();

        var distinct = versions.Select(entry => entry[(entry.IndexOf(": ", StringComparison.Ordinal) + 2)..])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            distinct.Length == 1,
            $"The setup-node steps disagree: {string.Join("; ", versions)}. Pin one version across them.");
    }

    /// <summary>
    /// <c>build-database.mjs</c> needs <c>node:sqlite</c> (Node 22.5+), so each
    /// devbook job sets Node up before it runs node, rather than taking whatever
    /// the runner image ships.
    /// </summary>
    [Fact]
    public void Every_devbook_metadata_job_sets_up_node_before_running_it()
    {
        foreach (var job in Jobs(File.ReadAllLines(WorkflowPath("devbook-metadata.yml"))))
        {
            var setup = job.Lines.FindIndex(line => line.Contains("uses: actions/setup-node@", StringComparison.Ordinal));
            var firstNode = job.Lines.FindIndex(line => line.TrimStart().StartsWith("run: node ", StringComparison.Ordinal));

            Assert.True(firstNode >= 0, $"devbook-metadata.yml: '{job.Name}' runs no node command.");
            Assert.True(
                setup >= 0 && setup < firstNode,
                $"devbook-metadata.yml: '{job.Name}' runs node without an actions/setup-node step before it.");
        }
    }

    private sealed record Job(string Name, List<string> Lines);

    private static string[] WorkflowFiles() =>
        Directory.GetFiles(RepositoryRoot.Directory(WorkflowFolder), "*.yml")
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string WorkflowPath(string name) => RepositoryRoot.File([.. WorkflowFolder, name]);

    private static string Workflow(string name) => File.ReadAllText(WorkflowPath(name));

    /// <summary>The jobs under <c>jobs:</c>, each with the lines of its body.</summary>
    private static List<Job> Jobs(string[] lines)
    {
        var jobs = new List<Job>();
        var inJobs = false;
        Job? current = null;

        foreach (var line in lines)
        {
            if (Regex.IsMatch(line, @"^\S"))
            {
                inJobs = line.TrimEnd() == "jobs:";
                current = null;
                continue;
            }

            if (!inJobs)
            {
                continue;
            }

            var header = Regex.Match(line, @"^  ([A-Za-z0-9_-]+):\s*$");
            if (header.Success)
            {
                current = new Job(header.Groups[1].Value, []);
                jobs.Add(current);
                continue;
            }

            current?.Lines.Add(line);
        }

        return jobs;
    }

    /// <summary>The value of a key at the job's own level, or null.</summary>
    private static string? JobKey(Job job, string key)
    {
        foreach (var line in job.Lines)
        {
            var match = Regex.Match(line, $@"^    {Regex.Escape(key)}:\s*(.*?)\s*$");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    /// <summary>A step's text, from its <c>- name:</c> line to the next step or job.</summary>
    private static string Step(string workflow, string name)
    {
        var match = Regex.Match(
            workflow,
            $@"^      - name: {Regex.Escape(name)}\s*$.*?(?=^      - name: |^  \S|\z)",
            RegexOptions.Multiline | RegexOptions.Singleline);

        Assert.True(match.Success, $"No step named '{name}'.");
        return match.Value;
    }
}
