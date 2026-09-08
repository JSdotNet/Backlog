namespace Backlog.Infrastructure.FileSystem;

/// <summary>A file-sync provider's folder that the workspace root turned out to
/// sit inside: the provider under the name a person would recognise it by, and
/// the folder that matched.</summary>
public sealed record SyncedFolderMatch(string ProviderName, string SyncedFolder);

/// <summary>
/// Whether a workspace root sits inside a folder some file-sync provider
/// replicates.
///
/// <para>This is the residual half of R9 in
/// <c>.arc42/11-risks-and-technical-debt.md</c>. The Storage screen's advice to
/// put the backlog in a synced folder is gone and its copy now says the opposite,
/// but a root that followed the old advice stays on OneDrive until somebody moves
/// it, and nothing in the app used to look. Local ADR 0005's
/// <c>### The database filename</c> chose this check over giving each device its
/// own database file, and chose to run it on the <em>root</em> rather than on
/// <c>backlog.db</c>: everything under the root carries the same hazard, and what
/// the root holds is not fixed.</para>
///
/// <para><strong>It warns; it never prevents.</strong> The root is the user's
/// choice, so every uncertainty resolves to silence — an absent variable, a probe
/// that cannot answer, a machine with nothing recognisable on it. A false
/// negative leaves somebody exactly where they already were; a false positive
/// would be this app arguing with a folder it cannot see into.</para>
///
/// <para>Two signals, and the order is deliberate. The provider variables
/// Windows sets are trusted on their own: they exist only while OneDrive is
/// signed in, and their value <em>is</em> the synced folder, so nothing has to be
/// guessed. The user-folder candidates below are not trusted on their own,
/// because a folder called <c>Dropbox</c> proves only that somebody once named a
/// folder that; those need the client's own marker beside them. The exception is
/// <c>iCloudDrive</c>, believed on its name alone because no installer but iCloud
/// for Windows writes it — which also means an uninstall that leaves the folder
/// behind leaves this one candidate able to warn about a folder nothing syncs any
/// more. That is accepted rather than missed: the cost is advice to move a backlog
/// out of a folder that genuinely was synced, and the alternative is guessing at an
/// Apple install path and trading a real warning for silence wherever the guess is
/// wrong. Every other candidate fails towards silence; this one, alone, fails
/// towards saying so.</para>
///
/// <para>It lives beside the workspace store rather than inside it because
/// reading environment variables and probing the disk is file-system work and
/// nothing else — the same reason <c>VsCodeFolderEditorLauncher</c> is a type of
/// its own, and it takes its machine through the same kind of seam so a fixed
/// fake one can be asserted against.</para>
/// </summary>
public sealed class SyncedFolderDetector
{
    private readonly Func<string, string?> _readEnvironmentVariable;
    private readonly Func<string, bool> _pathExists;

    public SyncedFolderDetector()
        : this(Environment.GetEnvironmentVariable, PathExists)
    {
    }

    /// <summary>Internal, and visible to this project's tests: which providers
    /// this machine has is exactly what a test may not depend on.</summary>
    internal SyncedFolderDetector(Func<string, string?> readEnvironmentVariable, Func<string, bool> pathExists)
    {
        _readEnvironmentVariable = readEnvironmentVariable;
        _pathExists = pathExists;
    }

    /// <summary>The provider folder <paramref name="rootDirectory"/> sits inside,
    /// or null when it sits outside every one this recognises — which is also the
    /// answer for a path that is not a path and for a machine that says
    /// nothing.</summary>
    public SyncedFolderMatch? Detect(string? rootDirectory)
    {
        var root = FullPath(rootDirectory);
        if (root is null) return null;

        foreach (var candidate in Candidates())
        {
            var folder = FullPath(candidate.Folder);
            if (folder is null || !Contains(folder, root)) continue;

            if (candidate.InstalledMarker is not null && !Exists(candidate.InstalledMarker)) continue;

            return new SyncedFolderMatch(candidate.ProviderName, folder);
        }

        return null;
    }

    /// <summary>A provider folder worth recognising, and the marker that has to
    /// be beside it before the folder's name is believed.</summary>
    private readonly record struct SyncProviderCandidate(string ProviderName, string? Folder, string? InstalledMarker = null);

    private IEnumerable<SyncProviderCandidate> Candidates()
    {
        // The cheap signal, and the only one that needs no disk at all. Business
        // before personal because a machine with both accounts signed in sets
        // both, and the tenant folder is the one a work backlog lands in.
        yield return new("OneDrive for Business", Variable("OneDriveCommercial"));
        yield return new("OneDrive", Variable("OneDriveConsumer"));
        yield return new("OneDrive", Variable("OneDrive"));

        // Neither Dropbox nor Google Drive publishes its folder in the
        // environment, so these are named from the user folder the installers
        // default to and confirmed by the client's own data folder. Neither
        // covers a folder somebody relocated, and neither covers Google Drive's
        // virtual drive letter: both are silence, which is the failure this is
        // allowed to have.
        var userProfile = Variable("USERPROFILE");
        var localAppData = Variable("LOCALAPPDATA");

        yield return new("Dropbox", Combine(userProfile, "Dropbox"), Combine(localAppData, "Dropbox", "info.json"));
        yield return new("Google Drive", Combine(userProfile, "Google Drive"), Combine(localAppData, "Google", "DriveFS"));
        yield return new("Google Drive", Combine(userProfile, "My Drive"), Combine(localAppData, "Google", "DriveFS"));
        // No marker, deliberately — see the class comment: iCloud for Windows is
        // the only writer of this name, and its uninstaller leaves the folder.
        yield return new("iCloud Drive", Combine(userProfile, "iCloudDrive"));
    }

    private string? Variable(string name)
    {
        try
        {
            var value = _readEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (Exception)
        {
            // A machine that will not say is a machine this says nothing about.
            return null;
        }
    }

    private bool Exists(string path)
    {
        try
        {
            return _pathExists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private static string? Combine(string? directory, params string[] parts) =>
        directory is null ? null : Path.Combine([directory, .. parts]);

    /// <summary>Both paths normalised the same way before either is compared:
    /// one canonical separator, no trailing one. A path typed into a settings
    /// field arrives in whatever shape the person typed it.</summary>
    private static string? FullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Containment by path segment rather than by string prefix, so
    /// <c>OneDrive-old</c> beside <c>OneDrive</c> is a different folder — which is
    /// what somebody who has already moved their backlog out has.</summary>
    private static bool Contains(string folder, string root) =>
        root.Equals(folder, StringComparison.OrdinalIgnoreCase)
        || (root.Length > folder.Length
            && root.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
            && root[folder.Length] == Path.DirectorySeparatorChar);

    /// <summary>A marker is a file for one provider and a folder for another, and
    /// which it is says nothing about whether the client is installed.</summary>
    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);
}
