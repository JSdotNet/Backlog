using Backlog.Mobile.UI.AppUpdate;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// What the person reads when Android's package installer reports back. The codes
/// are <c>PackageInstaller.STATUS_*</c>, which Android documents as fixed values.
/// </summary>
public sealed class ApkInstallStatusTests
{
    [Theory]
    [InlineData(ApkInstallStatus.FailureConflict)]
    [InlineData(ApkInstallStatus.FailureIncompatible)]
    public void A_conflict_names_the_likely_key_mismatch_without_claiming_it_and_carries_androids_words(int status)
    {
        var result = ApkInstallStatus.Describe(status, "INSTALL_FAILED_UPDATE_INCOMPATIBLE: signatures do not match");

        Assert.False(result.Started);
        Assert.Contains("may be signed with a different key", result.Message);
        Assert.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE: signatures do not match", result.Message);
    }

    [Fact]
    public void A_conflict_without_androids_words_still_reads_whole()
    {
        var result = ApkInstallStatus.Describe(ApkInstallStatus.FailureConflict, null);

        Assert.False(result.Started);
        Assert.EndsWith(".", result.Message);
        Assert.DoesNotContain("()", result.Message);
    }

    [Fact]
    public void Cancelling_the_android_prompt_is_a_cancelled_update()
    {
        var result = ApkInstallStatus.Describe(ApkInstallStatus.FailureAborted, null);

        Assert.False(result.Started);
        Assert.Contains("cancelled", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Success_is_an_update_under_way()
    {
        Assert.True(ApkInstallStatus.Describe(ApkInstallStatus.Success, null).Started);
    }

    [Fact]
    public void Not_enough_storage_says_so()
    {
        var result = ApkInstallStatus.Describe(ApkInstallStatus.FailureStorage, null);

        Assert.False(result.Started);
        Assert.Contains("storage", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_unknown_failure_carries_androids_own_words()
    {
        var result = ApkInstallStatus.Describe(42, "INSTALL_FAILED_SOMETHING_NEW");

        Assert.False(result.Started);
        Assert.Contains("INSTALL_FAILED_SOMETHING_NEW", result.Message);
    }
}
