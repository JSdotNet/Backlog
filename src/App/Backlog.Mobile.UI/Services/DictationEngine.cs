namespace Backlog.Mobile.UI.Services;

/// <summary>
/// What turns speech into text on this phone: the phone's Dictation Engine setting
/// (local ADR 0045 §6).
/// </summary>
/// <remarks>
/// Stored by name (<see cref="ISpeechSettingsStore"/>), like the language. The default is
/// the first member, on purpose: a recording leaves the phone only for a person who chose
/// the cloud, and choosing it is the consent ADR 0045 §7 asks for.
/// </remarks>
public enum DictationEngine
{
    /// <summary>The device's own recogniser — Android's, or the browser's Web Speech API.</summary>
    OnDevice = 0,

    /// <summary>The phone records, and the sync service transcribes the recording with
    /// Backlog's Foundry model and keeps none of it.</summary>
    Cloud
}
