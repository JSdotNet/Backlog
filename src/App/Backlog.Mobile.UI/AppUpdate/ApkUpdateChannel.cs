namespace Backlog.Mobile.UI.AppUpdate;

/// <summary>
/// Where this build looks for a newer APK: the GitHub repository whose releases
/// it was published to, as <c>owner/repository</c>.
/// </summary>
/// <remarks>
/// <para>
/// The repository is compiled in, the way the sync address is: release-mobile.yml
/// passes <c>-p:BacklogUpdateRepository=</c> to the head's publish, and the head turns
/// it into a constant. Every other build leaves it empty — <c>build/Install-AndroidApp.ps1</c>
/// and a debug deploy alike — and an empty channel is the one and only sign of a build
/// that cannot update itself. Never the version number: a developer can build any.
/// </para>
/// <para>
/// That is not caution for its own sake. Those builds are signed with the developer
/// keystore, and Android refuses an update signed with a different key than the
/// installed app, so a release APK could never replace one.
/// </para>
/// <para>
/// The value is not validated here. The <c>GenerateUpdateChannelBuildDefaults</c> target
/// in <c>Backlog.Mobile.csproj</c> fails the build on anything that is not
/// <c>owner/repository</c>, which is the only way a value reaches this type in the app;
/// one check in one place, where a bad value costs a build rather than a launch.
/// </para>
/// </remarks>
public sealed class ApkUpdateChannel(string? repository)
{
    /// <summary>What a build without a channel says when asked to check or install.</summary>
    public const string DevelopmentBuildMessage =
        "This build was installed from a development machine (build/Install-AndroidApp.ps1), so it cannot update itself. " +
        "Reinstall it with the script, or install a signed release from GitHub Releases to get updates here.";

    /// <summary>The channel of a build that cannot update itself.</summary>
    public static ApkUpdateChannel None { get; } = new(null);

    /// <summary><c>owner/repository</c>, or null when this build has no channel.</summary>
    public string? Repository { get; } = string.IsNullOrWhiteSpace(repository) ? null : repository.Trim();

    public bool IsConfigured => Repository is not null;

    /// <summary>
    /// GitHub's "list releases" for the channel, newest first, at the page size's
    /// maximum. The desktop publishes about as many releases in six weeks as a smaller
    /// page holds, so a stretch of skipped nightlies could otherwise push the newest APK
    /// off the page.
    /// </summary>
    public Uri ReleasesUri => IsConfigured
        ? new Uri($"https://api.github.com/repos/{Repository}/releases?per_page=100")
        : throw new InvalidOperationException("A build without an update channel has no releases to list.");
}
