using System.Diagnostics;
using System.Text.Json;

namespace Backlog.ArchitectureTests;

/// <summary>
/// <c>build/Get-NextReleaseVersion.ps1</c> numbers every release Release All publishes.
/// Desktop and mobile once numbered themselves from two unrelated
/// <c>github.run_number</c> counters, so the same build shipped as <c>v0.1.122</c> on
/// one channel and <c>mobile-v0.1.63</c> on the other. The script takes the highest
/// <c>MAJOR.MINOR.PATCH</c> over both tag namespaces and adds one to the patch, so the
/// one tag it returns is higher than anything either channel has installed: the MSIX
/// <c>Version</c> and the APK <c>versionCode</c> both only ever rise.
///
/// <para>Run through <c>pwsh</c> rather than re-implemented here, because the claim is
/// about what the workflow will compute, and the workflow runs this file. The tags are
/// passed in, so no case depends on the tags this clone happens to have.</para>
/// </summary>
public class NextReleaseVersionScriptTests
{
    [Fact]
    public void The_next_version_follows_the_highest_tag_of_either_channel()
    {
        var next = Run(["v0.1.122", "mobile-v0.1.63"]);

        Assert.Equal("0.1.123", next.Display);
        Assert.Equal("v0.1.123", next.Tag);
        Assert.Equal("0.1.123.0", next.Package);
        Assert.Equal(100123L, next.VersionCode);
        Assert.True(next.Derived);
    }

    [Fact]
    public void A_mobile_tag_higher_than_every_desktop_tag_sets_the_next_number()
    {
        var next = Run(["v0.1.40", "mobile-v0.1.63"]);

        Assert.Equal("v0.1.64", next.Tag);
    }

    [Fact]
    public void Tags_are_compared_as_numbers_not_as_text()
    {
        var next = Run(["v0.1.9", "v0.1.10", "v0.2.3", "v0.1.99"]);

        Assert.Equal("0.2.4", next.Display);
    }

    [Fact]
    public void With_no_release_tags_the_first_version_is_0_1_1()
    {
        var next = Run([]);

        Assert.Equal("v0.1.1", next.Tag);
        Assert.Equal(100001L, next.VersionCode);
    }

    [Fact]
    public void Tags_that_are_not_a_release_version_are_ignored()
    {
        var next = Run(["v0.1.5", "v0.1.900-beta", "v2", "release-9.9.9", "mobile-v0.1.x", "v9.9.9.9", "deploy-v9.0.0"]);

        Assert.Equal("v0.1.6", next.Tag);
    }

    [Fact]
    public void An_explicit_version_lower_than_the_highest_release_is_refused()
    {
        var error = Fail(["v0.1.122", "mobile-v0.1.63"], requested: "0.1.100");

        Assert.Contains("0.1.100", error, StringComparison.Ordinal);
        Assert.Contains("0.1.122", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_explicit_version_equal_to_the_highest_release_reruns_it()
    {
        var rerun = Run(["v0.1.122", "mobile-v0.1.63"], requested: "0.1.122");

        Assert.Equal("v0.1.122", rerun.Tag);
        Assert.False(rerun.Derived);
    }

    [Fact]
    public void A_pushed_v_tag_is_taken_as_its_version()
    {
        var pushed = Run(["v0.1.122", "v0.2.0"], requested: "v0.2.0");

        Assert.Equal("0.2.0", pushed.Display);
        Assert.Equal("0.2.0.0", pushed.Package);
        Assert.Equal(200000L, pushed.VersionCode);
        Assert.False(pushed.Derived);
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.2.3-beta")]
    [InlineData("mobile-v1.2.3")]
    public void An_explicit_version_that_is_not_major_minor_patch_is_refused(string requested)
    {
        var error = Fail([], requested);

        Assert.Contains("MAJOR.MINOR.PATCH", error, StringComparison.Ordinal);
    }

    /// <summary>The ceiling packs past <see cref="int.MaxValue"/>, as
    /// <c>ApkVersionTests</c> has it.</summary>
    [Fact]
    public void The_highest_packable_version_keeps_its_full_versionCode()
    {
        var ceiling = Run([], requested: "214.99.99999");

        Assert.Equal(2149999999L, ceiling.VersionCode);
    }

    /// <summary>The limits <c>ApkVersion</c> mirrors: major 214, minor 99, patch 99999,
    /// beyond which the packed <c>versionCode</c> overflows or collides.</summary>
    [Theory]
    [InlineData(new string[0], "215.0.0", "214")]
    [InlineData(new string[0], "0.100.0", "99")]
    [InlineData(new[] { "v0.1.99999" }, "", "99999")]
    public void A_version_past_the_versionCode_packing_limits_is_refused(string[] tags, string requested, string limit)
    {
        var error = Fail(tags, requested);

        Assert.Contains("packing limit", error, StringComparison.Ordinal);
        Assert.Contains(limit, error, StringComparison.Ordinal);
    }

    private sealed record NextVersion(string Tag, string Display, string Package, long VersionCode, bool Derived);

    private static NextVersion Run(string[] tags, string requested = "")
    {
        var (exit, output, error) = Pwsh(tags, requested);
        Assert.True(exit == 0, $"Get-NextReleaseVersion.ps1 exited {exit}: {error}");

        return JsonSerializer.Deserialize<NextVersion>(output)
            ?? throw new InvalidOperationException($"The script wrote no version: '{output}'.");
    }

    private static string Fail(string[] tags, string requested)
    {
        var (exit, output, error) = Pwsh(tags, requested);
        Assert.True(exit != 0, $"Get-NextReleaseVersion.ps1 accepted '{requested}' and returned {output}");
        return error;
    }

    private static (int Exit, string Output, string Error) Pwsh(string[] tags, string requested)
    {
        Assert.SkipUnless(PwshInstalled(), "PowerShell 7 (pwsh) is not installed.");

        var tagList = string.Join(',', tags.Select(tag => $"'{tag}'"));
        var command =
            "$ErrorActionPreference = 'Stop'; "
            + $"& ./build/Get-NextReleaseVersion.ps1 -Tag @({tagList}) -Requested '{requested}' "
            + "| ConvertTo-Json -Compress";

        var startInfo = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = RepositoryRoot.Root.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", command })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, output.Trim(), error.GetAwaiter().GetResult());
    }

    private static bool PwshInstalled()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("pwsh", "-NoProfile -Command exit")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });

            process?.WaitForExit(30_000);
            return process is { ExitCode: 0 };
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
