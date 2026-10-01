using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What each Devbook panel shows while its folder is being read, and when the read
/// fails (.devbook/design/interaction-guidelines.md, Loading States and Error
/// States).
/// <para>
/// Every panel is one whole-panel load with nothing laid out until the folder is
/// in, so the wait is the library's spinner — not an alert saying "Loading". A
/// folder that could not be read is this section failing, so it is announced as an
/// alert, and it carries a Retry that asks the folder again: before this, the first
/// read throwing left the panel on its loading line for good, with nothing to press.
/// </para>
/// <para>
/// Driven through the folder source rather than through each store, because that is
/// the one port all four stores read through and the place a folder moving mid-pull
/// actually throws.
/// </para>
/// </summary>
public sealed class DevbookPanelLoadStatesTests
{
    public static TheoryData<string> Panels => ["arc42", "domain", "technology", "design"];

    [Theory]
    [MemberData(nameof(Panels))]
    public void A_panel_waiting_on_its_folder_shows_a_spinner_rather_than_a_loading_alert(string panel)
    {
        using var workspace = DevbookWorkspace.Create();
        var folders = new FlakyDevbookFolderSource(workspace.Folders) { Held = true };
        using var context = Context(workspace, folders);

        var component = context.Render(Panel(panel));

        component.WaitForAssertion(() =>
        {
            var spinner = component.Find($"[data-testid='{panel}-loading']");
            Assert.Contains("spinner", spinner.ClassList);
            Assert.Equal("status", spinner.GetAttribute("role"));
        });
        Assert.Empty(component.FindAll(".app-error-message"));
        Assert.DoesNotContain("area...", component.Markup, StringComparison.Ordinal);

        folders.Release();

        component.WaitForAssertion(() => Assert.Empty(component.FindAll($"[data-testid='{panel}-loading']")));
    }

    [Theory]
    [MemberData(nameof(Panels))]
    public void A_folder_that_could_not_be_read_is_an_alert_with_a_retry_that_reads_it_again(string panel)
    {
        using var workspace = DevbookWorkspace.Create();
        var folders = new FlakyDevbookFolderSource(workspace.Folders) { FailuresLeft = 1 };
        using var context = Context(workspace, folders);

        var component = context.Render(Panel(panel));

        component.WaitForAssertion(() =>
        {
            var error = component.Find($"[data-testid='{panel}-load-error']");
            Assert.Equal("alert", error.GetAttribute("role"));
            Assert.Contains("Could not read", error.TextContent, StringComparison.Ordinal);
            Assert.NotNull(error.QuerySelector($"[data-testid='{panel}-load-retry']"));
            Assert.Empty(component.FindAll($"[data-testid='{panel}-loading']"));
        });

        var readsBeforeRetry = folders.ContentReads;

        component.Find($"[data-testid='{panel}-load-retry']").Click();

        component.WaitForAssertion(() =>
        {
            Assert.True(folders.ContentReads > readsBeforeRetry);
            Assert.Empty(component.FindAll($"[data-testid='{panel}-load-error']"));
            Assert.Empty(component.FindAll($"[data-testid='{panel}-loading']"));
            Assert.Contains(Loaded(panel), component.Markup, StringComparison.Ordinal);
        });
    }

    /// <summary>The failure alert is checked before anything else the panel
    /// renders, so a folder change that reads the folder successfully has to clear
    /// it too — not only the Retry. Left set, the panel stayed on "Could not read"
    /// over a folder it had just read.</summary>
    [Theory]
    [MemberData(nameof(Panels))]
    public void A_folder_change_that_reads_the_folder_clears_the_failure_alert(string panel)
    {
        using var workspace = DevbookWorkspace.Create();
        var folders = new FlakyDevbookFolderSource(workspace.Folders) { FailuresLeft = 1 };
        using var context = Context(workspace, folders);

        var component = context.Render(Panel(panel));

        component.WaitForAssertion(() => Assert.NotEmpty(component.FindAll($"[data-testid='{panel}-load-error']")));

        var readsBeforeChange = folders.ContentReads;

        folders.NotifyContentChanged();

        component.WaitForAssertion(() =>
        {
            Assert.True(folders.ContentReads > readsBeforeChange);
            Assert.Empty(component.FindAll($"[data-testid='{panel}-load-error']"));
            Assert.Contains(Loaded(panel), component.Markup, StringComparison.Ordinal);
        });
    }

    /// <summary>What each panel shows once its folder did read. The workspace
    /// writes a domain and a design folder, and neither an architecture nor a
    /// technology one, so those two answer with their own "no folder here" state —
    /// which is still the load succeeding.</summary>
    private static string Loaded(string panel) => panel switch
    {
        "arc42" => "No arc42 folder here yet",
        "domain" => "Context Map: Alpha",
        "technology" => "Open repository settings",
        _ => "Design: Alpha"
    };

    private static BunitContext Context(DevbookWorkspace workspace, IDevbookFolderSource folders)
    {
        var context = workspace.CreateBunitContext(folders);
        context.Services.AddSingleton<TechnologyDevbookService>();
        return context;
    }

    /// <summary>A fragment rather than the typed builder, whose parameter
    /// selectors cannot be written against a type chosen at run time.</summary>
    private static RenderFragment Panel(string panel) => builder =>
    {
        builder.OpenComponent(0, panel switch
        {
            "arc42" => typeof(Arc42DevbookPanel),
            "domain" => typeof(DomainDevbookPanel),
            "technology" => typeof(TechnologyDevbookPanel),
            _ => typeof(DesignDevbookView)
        });
        builder.AddAttribute(1, "RepositoryAlias", DevbookWorkspace.Alias);
        builder.CloseComponent();
    };

    /// <summary>
    /// Delegates every question, and can hold or fail the content read every store
    /// makes: <c>Held</c> keeps it out until <see cref="Release"/>, and
    /// <c>FailuresLeft</c> throws the way a folder moving mid-pull does, that many
    /// times, before reading normally.
    /// </summary>
    private sealed class FlakyDevbookFolderSource(IDevbookFolderSource inner) : IDevbookFolderSource
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Held { get; init; }

        public int FailuresLeft { get; set; }

        public int ContentReads { get; private set; }

        public void Release() => _release.TrySetResult();

        public event Action? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }

        public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias) => inner.Folders(repositoryAlias);

        public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null) => inner.Resolve(key, repositoryAlias);

        public void NotifyContentChanged() => inner.NotifyContentChanged();

        public async Task<DevbookFolderLocation> PrepareContentAsync(
            string key,
            string? repositoryAlias = null,
            IReadOnlyCollection<string>? relativePaths = null,
            CancellationToken cancellationToken = default)
        {
            ContentReads++;

            if (Held) await _release.Task;

            if (FailuresLeft > 0)
            {
                FailuresLeft--;
                throw new IOException($"The {key} folder moved while it was being read.");
            }

            return await inner.PrepareContentAsync(key, repositoryAlias, relativePaths, cancellationToken);
        }
    }
}
