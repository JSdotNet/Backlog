namespace Backlog.Modules.Devbook.Abstractions;

/// <summary>
/// What the rest of the app says and links to while the Brain is, or is not yet,
/// set up — so the header's Brain chip and the Settings card cannot drift apart.
/// <para>
/// Until a folder is set the Brain chip reads <see cref="SetUpLabel"/> and opens
/// the Brain card on Settings › Storage
/// (<c>.devbook/domain/brain/features.md#brain-scope</c>). The chip itself is
/// the header's; this is the one place its words and its destination are kept.
/// </para>
/// </summary>
public static class BrainSetup
{
    /// <summary>The chip's name once a folder is set.</summary>
    public const string Label = "Brain";

    /// <summary>The chip's name while no folder is set.</summary>
    public const string SetUpLabel = "Set up your Brain";

    /// <summary>The element id of the Brain card on Settings › Storage, which the
    /// link below scrolls to.</summary>
    public const string CardId = "brain-folder";

    /// <summary>Settings opened on its Storage page, at the Brain card. Relative,
    /// like every other link to the settings screen, so it resolves against the
    /// host's base address.</summary>
    public const string SettingsHref = "settings/storage#" + CardId;

    /// <summary>The chip's name for the settings as they stand.</summary>
    public static string ChipLabel(IBrainSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.IsSet ? Label : SetUpLabel;
    }
}
