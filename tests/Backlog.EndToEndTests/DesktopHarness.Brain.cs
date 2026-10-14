using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Backlog.EndToEndTests;

/// <summary>
/// The Brain on the desktop: the chip that leads the header's repository scope,
/// the Brain card on Settings › Storage, the Devbook pane while it shows the Brain,
/// and the Inbox's To Brain.
///
/// <para>The harness keeps its Brain folder per worktree, in
/// <c>obj/local-development/brain.settings.json</c>, never the installed app's.
/// A scenario still never points it at a folder of the person's: it remembers what
/// the card held, points it at a temporary folder outside every repository, and
/// types the remembered folder back in when it is done (<see cref="BrainScenario"/>).</para>
/// </summary>
internal sealed partial class DesktopHarness
{
    /// <summary>The Brain chip, first in the repository scope.</summary>
    public ILocator BrainChip => Page.GetByTestId("brain-scope-option");

    /// <summary>A repository's chip in the scope, by its alias.</summary>
    public ILocator RepositoryChip(string alias) =>
        Page.GetByTestId("repository-filter-option").Filter(new LocatorFilterOptions { HasTextRegex = new Regex($"^\\s*{Regex.Escape(alias)}\\s*$") });

    /// <summary>The Devbook pane, whichever devbook it shows.</summary>
    public ILocator DevbookPane => Page.Locator("#repository-devbook-pane");

    /// <summary>The Brain badge the pane wears while it shows the Brain.</summary>
    public ILocator BrainBadge => Page.GetByTestId("devbook-brain-badge");

    // ---- Settings › Storage ----

    private ILocator BrainFolderField => Page.Locator("#brain-folder-input");

    private ILocator BrainCardRows => Page.GetByTestId("brain-folder-card-rows");

    /// <summary>Opens Settings › Storage on the Brain card.</summary>
    public async Task OpenBrainCardAsync()
    {
        await OpenSettingsAsync("storage");
        await Page.GetByTestId("brain-folder-card").WaitForAsync();
    }

    /// <summary>The folder the Brain card holds; empty while none is set.</summary>
    public async Task<string> BrainFolderAsync()
    {
        await OpenBrainCardAsync();

        // The prerendered field is empty whatever is set; the circuit fills it.
        // A folder that is set draws the "synced" row beside the field, so a
        // field that stays empty with no such row is the answer "none".
        await Task.Delay(1_500);
        return await BrainFolderField.InputValueAsync();
    }

    /// <summary>
    /// Types a folder into the Brain card and commits it with Enter, the way the
    /// harness has to — it has no folder picker. An empty value clears the Brain.
    /// Done when the card draws the rows a folder gets, or none of them for an
    /// empty value.
    /// </summary>
    public Task SetBrainFolderAsync(string folder) =>
        Interactive.RepeatAsync(
            async () =>
            {
                await BrainFolderField.FillAsync(folder);
                await BrainFolderField.PressAsync("Enter");
            },
            async () => await BrainFolderField.InputValueAsync() == folder
                        && await Page.GetByTestId("brain-folder-card-synced").IsVisibleAsync() == (folder.Length > 0)
                        && !await Page.GetByTestId("brain-folder-card-error").IsVisibleAsync(),
            $"setting the Brain folder to '{folder}'");

    /// <summary>What the card's rows say about the folder: synced or not, the
    /// database line, and the README and CONVENTIONS rows.</summary>
    public Task<string> BrainCardRowsAsync() => BrainCardRows.InnerTextAsync();

    public ILocator CreateConventionsButton => Page.GetByTestId("brain-folder-card-create-conventions");

    /// <summary>Presses Create conventions and waits for the card to say what it
    /// wrote; the button goes away once nothing is missing.</summary>
    public async Task<string> CreateConventionsAsync()
    {
        var message = Page.GetByTestId("brain-folder-card-message");
        await Interactive.ClickAsync(CreateConventionsButton, message);
        return await message.InnerTextAsync();
    }

    // ---- The header's scope ----

    /// <summary>Shows the Tasks view with every row the scope lets through:
    /// My Day and Ready in a plan off, every status. The filters are the shared
    /// workspace's, so each is set rather than assumed.</summary>
    public async Task ShowAllTasksAsync()
    {
        await ShowTasksAsync();
        await SetPressedAsync(Page.GetByTestId("readyinplan-filter-option"), false);
        await SetPressedAsync(Page.GetByTestId("myday-filter-option"), false);

        var all = Page.Locator("#status-filter-all");
        await Interactive.RepeatAsync(
            async () =>
            {
                if (await all.GetAttributeAsync("aria-checked") != "true") await all.ClickAsync();
            },
            async () => await all.GetAttributeAsync("aria-checked") == "true",
            "the All status filter");
    }

    /// <summary>Presses nothing in the repository scope: every chip that reads
    /// pressed is pressed again with Ctrl, which takes it back out.</summary>
    public async Task ClearScopeAsync()
    {
        var pressed = Page.Locator("[data-testid='brain-scope-option'][aria-pressed='true'], [data-testid='repository-filter-option'][aria-pressed='true']");
        await Interactive.RepeatAsync(
            async () =>
            {
                if (await pressed.CountAsync() > 0)
                {
                    await pressed.First.ClickAsync(new LocatorClickOptions { Modifiers = [KeyboardModifier.Control] });
                }
            },
            async () => await pressed.CountAsync() == 0,
            "clearing the repository scope");
    }

    /// <summary>Presses a scope chip — with Ctrl to add it to what is pressed —
    /// until it reads pressed.</summary>
    public Task PressScopeChipAsync(ILocator chip, bool ctrl = false) =>
        Interactive.RepeatAsync(
            async () =>
            {
                if (await chip.GetAttributeAsync("aria-pressed") != "true")
                {
                    await chip.ClickAsync(new LocatorClickOptions { Modifiers = ctrl ? [KeyboardModifier.Control] : [] });
                }
            },
            async () => await chip.GetAttributeAsync("aria-pressed") == "true",
            $"pressing {chip}");

    /// <summary>
    /// The ids of the task rows the list shows once it has settled. The list is
    /// drawn again after a press, so it is read until two reads a second apart
    /// agree.
    /// </summary>
    public async Task<IReadOnlySet<string>> TaskIdsAsync()
    {
        var rows = Page.Locator("#backlog-pane [data-task-id]");
        string[] last = [];
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(1_000);
            var now = (await rows.EvaluateAllAsync<string[]>("rows => rows.map(r => r.dataset.taskId)")).Order(StringComparer.Ordinal).ToArray();
            if (now.Length > 0 && now.SequenceEqual(last)) return now.ToHashSet(StringComparer.Ordinal);
            last = now;
        }

        return last.ToHashSet(StringComparer.Ordinal);
    }

    // ---- The Devbook pane on the Brain ----

    /// <summary>Shows the Devbook pane beside the Tasks view.</summary>
    public Task ShowDevbookAsync() => ShowPaneAsync("devbook-pane-option", "repository-devbook-pane");

    /// <summary>The area tabs of the pane, in the order it draws them.</summary>
    public async Task<IReadOnlyList<string>> BrainAreasAsync() =>
        (await DevbookPane.GetByRole(AriaRole.Tab).AllInnerTextsAsync()).Select(s => s.Trim()).ToList();

    /// <summary>Opens one area tab of the Brain.</summary>
    public Task OpenAreaAsync(string area)
    {
        var tab = DevbookPane.GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = area, Exact = true });
        return Interactive.RepeatAsync(
            () => tab.ClickAsync(),
            async () => await tab.GetAttributeAsync("aria-selected") == "true",
            $"the {area} area");
    }

    /// <summary>The files the open area's menu lists, top to bottom.</summary>
    public async Task<IReadOnlyList<string>> AreaFilesAsync() =>
        (await DevbookPane.Locator(".devbook-menu__item--file").AllInnerTextsAsync()).Select(s => s.Trim()).ToList();

    /// <summary>The note the pane shows, by the title it is labelled with.</summary>
    public ILocator BrainNote(string title) =>
        Page.GetByTestId("brain-note").And(Page.Locator($"[aria-label='{title.Replace("'", "\\'", StringComparison.Ordinal)}']"));

    /// <summary>Captures a note through the pane's Capture dialog.</summary>
    public async Task CaptureIntoBrainAsync(string title, string body)
    {
        var dialog = Page.GetByTestId("devbook-brain-capture-dialog");
        await Interactive.ClickAsync(Page.GetByTestId("devbook-brain-capture"), dialog);
        await Interactive.FillAsync(Page.GetByTestId("devbook-brain-capture-title").Locator("input"), title);
        await Interactive.FillAsync(Page.GetByTestId("devbook-brain-capture-body").Locator("textarea"), body);
        await Interactive.ClickAsync(Page.GetByTestId("devbook-brain-capture-save"), BrainNote(title));
    }

    /// <summary>Follows the open note's gold <c>repo:</c> chip.</summary>
    public Task FollowRepoLinkAsync() =>
        Interactive.ClickAsync(
            Page.GetByTestId("brain-repo-link").Locator("button").First,
            Page.Locator("#repository-devbook-pane .devbook-document").First);

    // ---- Inbox ----

    /// <summary>Turns on the Inbox pane, which is off by default; kept per
    /// worktree.</summary>
    public async Task EnableInboxAsync()
    {
        await OpenSettingsAsync("features");
        var toggle = Page.GetByRole(AriaRole.Switch, new PageGetByRoleOptions { NameRegex = InboxPaneFeature() });
        await Interactive.RepeatAsync(
            async () =>
            {
                if (!await toggle.IsCheckedAsync()) await toggle.ClickAsync();
            },
            () => toggle.IsCheckedAsync(),
            "turning on the Inbox pane");
    }

    /// <summary>Files one thought in the desktop Inbox through the capture bar.</summary>
    public async Task CaptureIntoInboxAsync(string title)
    {
        await ShowInboxAsync();
        var input = Page.Locator("#inbox-pane-capture-input");
        await Interactive.RepeatAsync(
            async () =>
            {
                if (await InboxItem(title).CountAsync() > 0) return;
                await input.FillAsync(title);
                await input.PressAsync("Enter");
            },
            async () => await InboxItem(title).CountAsync() > 0,
            $"capturing {title} in the Inbox");
    }

    /// <summary>Routes the open Inbox item To Brain with the decision bar's
    /// button, and returns what the page said about it.</summary>
    public async Task<string> RouteOpenItemToBrainAsync()
    {
        var toBrain = Page.Locator("[data-testid='inbox-to-brain']:visible").First;
        var kept = Page.Locator("[role='status']").Filter(new LocatorFilterOptions { HasText = "in your Brain as" });
        await Interactive.ClickAsync(toBrain, kept);
        return await kept.First.InnerTextAsync();
    }
}
