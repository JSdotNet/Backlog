namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// Whether the workspace root sits inside a file-sync provider's folder.
///
/// <para>The whole class runs against a fixed fake machine — an environment
/// block and a path probe supplied per test — for the same reason
/// <see cref="VsCodeFolderEditorLauncherTests"/> does: the answer depends on
/// which providers are installed and signed in, and the build agent's own disk
/// is neither. A test that only passed on a machine with OneDrive would prove
/// nothing about the one that lost somebody's edits.</para>
///
/// <para>R9 in <c>.arc42/11-risks-and-technical-debt.md</c> is what this is for,
/// and local ADR 0005's <c>### The database filename</c> is why it is a warning
/// on the root rather than a per-device file name. Detection is heuristic, so it
/// fails open throughout: an absent variable, a throwing probe or a machine it
/// does not recognise all mean "nothing to say", never a false alarm and never an
/// exception.</para>
/// </summary>
public sealed class SyncedFolderDetectorTests
{
    private const string UserProfile = @"C:\fake\Users\dev";
    private const string LocalAppData = @"C:\fake\Users\dev\AppData\Local";

    private static readonly string PersonalOneDrive = Path.Combine(UserProfile, "OneDrive");
    private static readonly string BusinessOneDrive = Path.Combine(UserProfile, "OneDrive - Contoso");
    private static readonly string Dropbox = Path.Combine(UserProfile, "Dropbox");
    private static readonly string DropboxMarker = Path.Combine(LocalAppData, "Dropbox", "info.json");
    private static readonly string GoogleDrive = Path.Combine(UserProfile, "Google Drive");
    private static readonly string GoogleMyDrive = Path.Combine(UserProfile, "My Drive");
    private static readonly string GoogleDriveMarker = Path.Combine(LocalAppData, "Google", "DriveFS");
    private static readonly string ICloudDrive = Path.Combine(UserProfile, "iCloudDrive");

    [Fact]
    public void Recognises_a_root_inside_the_personal_onedrive_folder()
    {
        var detector = Detector(Windows(("OneDriveConsumer", PersonalOneDrive)));

        var match = detector.Detect(Path.Combine(PersonalOneDrive, "Backlog"));

        Assert.NotNull(match);
        Assert.Equal("OneDrive", match!.ProviderName);
        Assert.Equal(PersonalOneDrive, match.SyncedFolder);
    }

    /// <summary>The variable Windows sets when only one account is signed in. It
    /// is the same product under the same name, so the warning reads the same.</summary>
    [Fact]
    public void Recognises_a_root_inside_the_plain_onedrive_variable()
    {
        var detector = Detector(Windows(("OneDrive", PersonalOneDrive)));

        var match = detector.Detect(Path.Combine(PersonalOneDrive, "Notes", "Backlog"));

        Assert.Equal("OneDrive", match?.ProviderName);
    }

    /// <summary>A work tenant's folder is named after the tenant, so the provider
    /// name is what makes it recognisable rather than the path.</summary>
    [Fact]
    public void Recognises_a_root_inside_the_business_onedrive_folder()
    {
        var detector = Detector(Windows(
            ("OneDriveCommercial", BusinessOneDrive),
            ("OneDrive", BusinessOneDrive)));

        var match = detector.Detect(Path.Combine(BusinessOneDrive, "Backlog"));

        Assert.Equal("OneDrive for Business", match?.ProviderName);
    }

    [Fact]
    public void Recognises_a_root_inside_the_dropbox_folder()
    {
        var detector = Detector(Windows(), Existing(DropboxMarker));

        var match = detector.Detect(Path.Combine(Dropbox, "Backlog"));

        Assert.Equal("Dropbox", match?.ProviderName);
        Assert.Equal(Dropbox, match?.SyncedFolder);
    }

    /// <summary>Dropbox and Google Drive are named from the user folder rather
    /// than from a variable, so an installed client is the second signal that
    /// keeps a folder somebody merely named "Dropbox" from raising a warning.</summary>
    [Fact]
    public void Says_nothing_about_a_dropbox_folder_with_no_dropbox_installed()
    {
        var detector = Detector(Windows(), Existing());

        Assert.Null(detector.Detect(Path.Combine(Dropbox, "Backlog")));
    }

    [Fact]
    public void Recognises_a_root_inside_the_google_drive_folder()
    {
        var detector = Detector(Windows(), Existing(GoogleDriveMarker));

        Assert.Equal("Google Drive", detector.Detect(Path.Combine(GoogleDrive, "Backlog"))?.ProviderName);
        Assert.Equal("Google Drive", detector.Detect(Path.Combine(GoogleMyDrive, "Backlog"))?.ProviderName);
    }

    /// <summary>The one candidate with no installed-client marker, asserted with
    /// none present so the exemption is a recorded decision rather than a gap.
    /// The class comment says what it costs: a folder an uninstall left behind is
    /// warned about, which is the only place this detector errs towards speaking
    /// rather than towards silence.</summary>
    [Fact]
    public void Believes_an_icloud_folder_on_its_name_alone()
    {
        var detector = Detector(Windows(), Existing());

        Assert.Equal("iCloud Drive", detector.Detect(Path.Combine(ICloudDrive, "Backlog"))?.ProviderName);
    }

    /// <summary>The root is a path somebody typed into a settings field, so it
    /// arrives in whatever case and with whatever trailing separator they used.</summary>
    [Fact]
    public void Matches_whatever_case_and_separators_the_root_was_typed_in()
    {
        var detector = Detector(Windows(("OneDrive", PersonalOneDrive)));

        Assert.NotNull(detector.Detect(PersonalOneDrive.ToUpperInvariant() + Path.DirectorySeparatorChar + "backlog"));
        Assert.NotNull(detector.Detect(Path.Combine(PersonalOneDrive, "Backlog") + Path.DirectorySeparatorChar));
        Assert.NotNull(detector.Detect(PersonalOneDrive.Replace('\\', '/') + "/Backlog"));
    }

    [Fact]
    public void Matches_the_provider_folder_itself()
    {
        var detector = Detector(Windows(("OneDrive", PersonalOneDrive)));

        Assert.NotNull(detector.Detect(PersonalOneDrive));
    }

    /// <summary>Containment is by path segment, not by string prefix. A folder
    /// somebody moved their backlog *out* of OneDrive into is the case this
    /// protects, and warning about it would be the false positive the feature is
    /// not allowed to produce.</summary>
    [Fact]
    public void Says_nothing_about_a_sibling_folder_whose_name_merely_starts_the_same()
    {
        var detector = Detector(Windows(("OneDrive", PersonalOneDrive)));

        Assert.Null(detector.Detect(PersonalOneDrive + "-old"));
        Assert.Null(detector.Detect(Path.Combine(PersonalOneDrive + "-old", "Backlog")));
    }

    [Fact]
    public void Says_nothing_about_a_root_outside_every_provider_folder()
    {
        var detector = Detector(
            Windows(("OneDrive", PersonalOneDrive)),
            Existing(DropboxMarker, GoogleDriveMarker));

        Assert.Null(detector.Detect(@"D:\Notes\Backlog"));
    }

    /// <summary>Nothing recognised means nothing said. This is the non-Windows
    /// machine and the Windows one with no provider signed in, which are the same
    /// answer for the same reason: the app knows nothing, so it claims nothing.</summary>
    [Fact]
    public void Says_nothing_on_a_machine_it_recognises_nothing_about()
    {
        var detector = Detector(_ => null);

        Assert.Null(detector.Detect(Path.Combine(PersonalOneDrive, "Backlog")));
        Assert.Null(detector.Detect("/home/dev/Backlog"));
    }

    [Fact]
    public void Says_nothing_when_a_probe_cannot_answer()
    {
        foreach (var failure in new Exception[]
                 {
                     new IOException("the disk went away"),
                     new UnauthorizedAccessException("no."),
                     new NotSupportedException("not that kind of path")
                 })
        {
            var detector = Detector(Windows(), _ => throw failure);

            Assert.Null(detector.Detect(Path.Combine(Dropbox, "Backlog")));
        }
    }

    [Fact]
    public void Says_nothing_about_a_root_that_is_not_a_path()
    {
        var detector = Detector(Windows(("OneDrive", PersonalOneDrive)));

        Assert.Null(detector.Detect(null));
        Assert.Null(detector.Detect("   "));
        Assert.Null(detector.Detect("|"));
    }

    /// <summary>A variable that is set to nothing is a variable that says
    /// nothing — and must not be read as "the whole disk is OneDrive".</summary>
    [Fact]
    public void Says_nothing_when_a_provider_variable_is_empty()
    {
        var detector = Detector(Windows(("OneDrive", ""), ("OneDriveCommercial", "   ")));

        Assert.Null(detector.Detect(@"D:\Notes\Backlog"));
        Assert.Null(detector.Detect(Path.Combine(PersonalOneDrive, "Backlog")));
    }

    private static SyncedFolderDetector Detector(
        Func<string, string?> environment,
        Func<string, bool>? pathExists = null) =>
        new(environment, pathExists ?? Existing());

    /// <summary>A Windows machine with the two folders every provider below is
    /// named from, plus whichever provider variables the test sets.</summary>
    private static Func<string, string?> Windows(params (string Name, string Value)[] variables)
    {
        var block = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["USERPROFILE"] = UserProfile,
            ["LOCALAPPDATA"] = LocalAppData
        };

        foreach (var (name, value) in variables) block[name] = value;

        return name => block.TryGetValue(name, out var value) ? value : null;
    }

    private static Func<string, bool> Existing(params string[] paths) =>
        path => paths.Contains(path, StringComparer.OrdinalIgnoreCase);
}
