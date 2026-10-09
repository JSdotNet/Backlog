using Microsoft.AspNetCore.Components;

namespace Backlog.UI.Components.Devbook;

/// <summary>
/// A host's answer to an image a knowledge chapter names by something other than a
/// path — <c>scenario:&lt;stem&gt;#&lt;label&gt;</c>, a scenario page's latest capture of
/// a screenshot point — cascaded to every chapter view below it.
/// <para>
/// <see cref="DevbookInlineTargets"/> asks it first and keeps its own rule — a relative
/// image is inert — for every target it answers null to. The library resolves no file
/// itself, so the picture and the line that says where it came from are the host's,
/// which knows the repository and its runs.
/// </para>
/// </summary>
public interface IDevbookImageSource
{
    /// <summary>What to draw for <c>![caption](target)</c> in the chapter at
    /// <paramref name="documentPath"/>, or null to leave the image to the chapter's own
    /// rule.</summary>
    RenderFragment? Image(string target, string caption, string? documentPath);
}
