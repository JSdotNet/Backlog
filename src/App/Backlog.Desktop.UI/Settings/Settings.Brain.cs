using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.UI.Components.Brain;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

// The component's published namespace, not the folder's: see the note at the top
// of Settings.razor for why the two must differ.
namespace Backlog.Desktop.UI.Shell;

/// <summary>
/// The Brain card on Settings › Storage (local ADR 0026,
/// <c>.devbook/domain/devbook/features.md#brain-folder-setup</c>): the folder,
/// what it holds, and <b>Create conventions</b>.
/// <para>
/// A partial of its own because the card's state is self-contained and the
/// screen's file is long enough already. The three collaborators are read off the
/// service provider rather than injected, the way the backup worker is: a host
/// that composes no Brain setting still renders the screen, and draws no card.
/// </para>
/// </summary>
public partial class Settings
{
    /// <summary>The page a link opened this screen at — <c>settings?page=storage</c>
    /// — or null for the default one.</summary>
    [SupplyParameterFromQuery(Name = "page")]
    public string? Page { get; set; }

    private IBrainSettings? _brainSettings;
    private BrainFolderSetup? _brainSetup;
    private IBrainFolderPicker? _brainPicker;

    private string _draftBrainFolder = string.Empty;
    private BrainFolderCardStatus? _brainStatus;
    private string? _brainError;
    private string? _brainMessage;
    private bool _browsingBrainFolder;

    private void InitializeBrain()
    {
        _brainSettings = Services.GetService<IBrainSettings>();
        _brainSetup = Services.GetService<BrainFolderSetup>();
        _brainPicker = Services.GetService<IBrainFolderPicker>();

        if (_brainSettings is null) return;

        // Another window, or the MCP side of the app, may set the folder while
        // this screen is open; the card follows rather than showing a stale path.
        _brainSettings.Changed += OnBrainSettingsChanged;
        _draftBrainFolder = _brainSettings.BrainFolder ?? string.Empty;
        RefreshBrainStatus();
    }

    /// <summary>Looks at the folder again. Asked when the card is drawn and after
    /// every act on it, so a file somebody added by hand since is noticed the next
    /// time Storage is opened.</summary>
    private void RefreshBrainStatus()
    {
        if (_brainSettings?.BrainFolder is not { } folder || _brainSetup is null)
        {
            _brainStatus = null;
            return;
        }

        var status = _brainSetup.Inspect(folder);
        _brainStatus = new BrainFolderCardStatus(status.SyncedBy?.ProviderName, status.HasReadme, status.HasConventions);
    }

    private void ApplyBrainFolder(string folder)
    {
        if (_brainSettings is null) return;

        _draftBrainFolder = folder;

        _brainMessage = null;
        _brainError = _brainSettings.TrySetBrainFolder(_draftBrainFolder);

        // A refused path stays in the field so it can be corrected; an accepted
        // one is shown the way the store normalised it.
        if (_brainError is null) _draftBrainFolder = _brainSettings.BrainFolder ?? string.Empty;

        RefreshBrainStatus();
    }

    private async Task BrowseBrainFolderAsync()
    {
        if (_brainPicker is null || _browsingBrainFolder) return;

        _browsingBrainFolder = true;
        try
        {
            var start = string.IsNullOrWhiteSpace(_draftBrainFolder) ? null : _draftBrainFolder;
            var picked = await _brainPicker.PickFolderAsync(start);
            if (picked is null) return;

            ApplyBrainFolder(picked);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _brainError = $"Couldn't open the folder picker: {ex.Message}";
        }
        finally
        {
            _browsingBrainFolder = false;
        }
    }

    private void CreateBrainConventions()
    {
        if (_brainSettings?.BrainFolder is not { } folder || _brainSetup is null) return;

        // Two small files and a handful of folders: written in the click's own
        // turn, so the card has no busy state to show here.
        var result = _brainSetup.CreateConventions(folder);
        _brainError = result.Error;
        _brainMessage = result.Error is null ? DescribeConventions(result) : null;

        RefreshBrainStatus();
    }

    /// <summary>What Create conventions did, in one sentence: what it wrote, and
    /// that nothing already there was changed whenever something was.</summary>
    internal static string DescribeConventions(BrainConventionsResult result)
    {
        var files = result.Written.Where(name => !name.EndsWith('/')).ToList();
        var folders = result.Written.Count - files.Count;

        var parts = new List<string>(files);
        if (folders > 0) parts.Add(folders == 1 ? "1 area folder" : $"{folders} area folders");

        var written = parts.Count switch
        {
            0 => "Nothing was missing",
            1 => $"Wrote {parts[0]}",
            _ => $"Wrote {string.Join(", ", parts[..^1])} and {parts[^1]}",
        };

        return result.Kept.Count == 0
            ? written + "."
            : written + ". Everything already there was left as it was.";
    }

    /// <summary>Brings the card's field into view and focus when a link opened
    /// the screen at the Brain card - the Devbook pane's first-use prompt - so the
    /// first thing in front of the person is the field they came to fill.</summary>
    private async Task RevealBrainCardAsync()
    {
        if (_brainSettings is null || _activePage is not SettingsPage.Storage) return;
        if (!new Uri(Navigation.Uri).Fragment.Equals("#" + BrainSetup.CardId, StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            await JS.InvokeVoidAsync("backlogFocus", BrainSetup.CardId + "-input");
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException)
        {
            // Bringing the card into view is a courtesy; the page is already on it.
        }
    }

    private void OnBrainSettingsChanged() => _ = InvokeAsync(() =>
    {
        _draftBrainFolder = _brainSettings?.BrainFolder ?? string.Empty;
        RefreshBrainStatus();
        StateHasChanged();
    });
}
