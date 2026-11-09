using Backlog.UI.Components.Devbook;
using Backlog.UI.Components.Diagrams.C4;

using Microsoft.AspNetCore.Components;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// The image source the architecture panel cascades to its chapters: an embed
/// <c>![alt](c4:&lt;view key&gt;)</c> becomes that view of the workspace, drawn inline, and
/// every other image goes to the source the pane cascaded above it — the scenario
/// screenshots — unchanged.
/// <para>
/// One instance per panel, cascaded as fixed, with the catalog swapped inside it, for
/// the reason <see cref="DevbookScenarioImages"/> gives: a cascade that is not fixed
/// re-renders every chapter view under it on every render of the panel.
/// </para>
/// <para>
/// A key is matched without regard to case, as devbook's checker matches it. A key
/// that names no view — or any key while the feature is off, when the catalog is
/// <see cref="C4Catalog.Off"/> — is declined, so the chapter keeps its own rule and
/// shows the alt text, which is what devbook's checker flags as an error anyway.
/// </para>
/// </summary>
public sealed class C4ViewImages : IDevbookImageSource
{
    /// <summary>The scheme a chapter embeds a C4 view under.</summary>
    public const string Scheme = "c4:";

    /// <summary>The model the embeds resolve against.</summary>
    public C4Catalog Catalog { get; set; } = C4Catalog.Off;

    /// <summary>The source cascaded above this one, asked about every image this
    /// declines.</summary>
    public IDevbookImageSource? Next { get; set; }

    /// <summary>What following "Open in C4 tab" does. Unset, the figure offers no way
    /// across.</summary>
    public EventCallback<C4View> OnOpen { get; set; }

    /// <summary>The way across as the tabs name it.</summary>
    public const string TabLabel = "Open in C4 tab";

    /// <summary>The way across as the architecture surface names it: it has no C4 tab,
    /// and the view opens in its picture.</summary>
    public const string SurfaceLabel = "Open in the C4 model";

    /// <summary>What the way across says; the host sets it to where the view opens.</summary>
    public string OpenLabel { get; set; } = TabLabel;

    public RenderFragment? Image(string target, string caption, string? documentPath) =>
        Resolve(target) is { } resolved
            ? Draw(resolved.Workspace, resolved.View, string.IsNullOrWhiteSpace(caption) ? resolved.Entry.Title : caption)
            : Next?.Image(target, caption, documentPath);

    /// <summary>The view an embed target names, or null when it is not a <c>c4:</c>
    /// target or names no view the catalog can draw.</summary>
    public (C4Workspace Workspace, C4View View, C4ViewEntry Entry)? Resolve(string? target)
    {
        var key = Key(target);
        if (key is null || !Catalog.HasViews) return null;

        var entry = Catalog.Views.FirstOrDefault(view => string.Equals(view.Key, key, StringComparison.OrdinalIgnoreCase));
        if (Catalog.WorkspaceOf(entry) is not { } workspace) return null;
        if (workspace.Workspace.View(entry!.Key) is not { } view) return null;

        return (workspace.Workspace, view, entry);
    }

    /// <summary>The view key a <c>c4:</c> target names — trimmed, with the angle
    /// brackets CommonMark allows around a destination removed — or null.</summary>
    public static string? Key(string? target)
    {
        var trimmed = target?.Trim().TrimStart('<').TrimEnd('>').Trim();
        if (trimmed is null || !trimmed.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)) return null;

        var key = trimmed[Scheme.Length..].Trim();
        return key.Length == 0 ? null : key;
    }

    private RenderFragment Draw(C4Workspace workspace, C4View view, string caption) => builder =>
    {
        builder.OpenComponent<C4EmbeddedView>(0);
        builder.AddComponentParameter(1, nameof(C4EmbeddedView.Workspace), workspace);
        builder.AddComponentParameter(2, nameof(C4EmbeddedView.View), view);
        builder.AddComponentParameter(3, nameof(C4EmbeddedView.Caption), caption);
        builder.AddComponentParameter(4, nameof(C4EmbeddedView.OnOpen), OnOpen);
        builder.AddComponentParameter(5, nameof(C4EmbeddedView.OpenLabel), OpenLabel);
        builder.CloseComponent();
    };
}
