using Backlog.Infrastructure.Devbook;
using Backlog.Modules.Brain.Abstractions;

namespace Backlog.Infrastructure.Brain;

/// <summary>
/// The Brain's corpus profile: how Devbook's builder reads the Brain folder
/// (local ADR 0040 §2 — Devbook supplies the hook, the Brain conforms). It reads
/// the folders its conventions file declares and discovers, writes a
/// <c>brain</c> fingerprint line, lists placeholders, applies no devbook folder
/// convention, keeps <c>repo:</c> links, and adds the note fields.
/// </summary>
public sealed class BrainCorpusProfile : DevbookCorpusProfile
{
    public static BrainCorpusProfile Instance { get; } = new();

    private BrainCorpusProfile()
    {
    }

    public override string? FingerprintTag => "brain";

    public override bool ReadsCloudPlaceholders => true;

    public override bool HasFolderConventions => false;

    public override bool ReadsCrossCorpusLinks => true;

    /// <summary>The areas <see cref="BrainConventions.Read"/> resolves, in tab
    /// order, of those that are folders on disk — the set the pane's tabs show and
    /// <c>tools/devbook/brain.mjs</c> reads.</summary>
    public override IReadOnlyList<string>? Folders(string root) =>
        BrainConventions.Read(root)
            .Select(area => area.Folder)
            .Where(folder => Directory.Exists(Path.Combine(root, folder)))
            .ToArray();

    /// <summary>The conventions file: declaring an area changes what is read
    /// before any file under it does.</summary>
    public override IEnumerable<string> ExtraInputs(string root) => [BrainConventions.FileName];

    /// <summary>
    /// A note's <c>para</c> — its folder unless the block names one, and then the
    /// block's, so a block naming an empty one gets none rather than the folder; a
    /// heading carries one only when its block says so — and the <c>repos</c> and
    /// <c>tasks</c> it names — and, on a file, the <c>review</c> date its top block
    /// names (local ADR 0054 §6), kept as written: whether it reads as a date is
    /// <see cref="BrainReviewDateReader"/>'s question, which reports one that does
    /// not as a warning, so the build never refuses it.
    /// </summary>
    public override void ApplyNodeFields(IDevbookNodeFields node, string? folder)
    {
        ArgumentNullException.ThrowIfNull(node);

        var para = node.Has("para") ? node.Scalar("para") : folder;
        if (!string.IsNullOrEmpty(para)) node.AddAttribute("para", [para]);

        foreach (var name in (string[])["repos", "tasks"])
        {
            var values = node.List(name);
            if (values.Count > 0) node.AddAttribute(name, values);
        }

        if (node.IsFile && node.Scalar(BrainReviewDate.Field) is { Length: > 0 } review)
        {
            node.AddAttribute(BrainReviewDate.Field, [review]);
        }
    }
}
