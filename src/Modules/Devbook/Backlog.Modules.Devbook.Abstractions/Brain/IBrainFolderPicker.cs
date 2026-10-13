namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// The operating system's folder picker, behind the Brain card's <b>Browse</b>.
/// <para>
/// A port rather than a call in the settings screen because only the installed
/// desktop head has a window to open a picker over. A host that registers none —
/// the browser harness, a test — draws the card without Browse, and the folder is
/// typed instead.
/// </para>
/// </summary>
public interface IBrainFolderPicker
{
    /// <summary>Asks the person for a folder, starting at
    /// <paramref name="startFolder"/> where the picker can. Null when they cancel.</summary>
    Task<string?> PickFolderAsync(string? startFolder, CancellationToken cancellationToken = default);
}
