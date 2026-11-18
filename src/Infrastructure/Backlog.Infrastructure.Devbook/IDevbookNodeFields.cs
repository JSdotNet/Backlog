namespace Backlog.Infrastructure.Devbook;

/// <summary>
/// The narrow face of one graph node and its <c>meta</c> block that a
/// <see cref="DevbookCorpusProfile"/> sees, so the graph itself stays the
/// builder's own. Values read the way the devbook generator reads them.
/// </summary>
public interface IDevbookNodeFields
{
    /// <summary>Whether the node is a file, its block the file's top one, rather
    /// than a heading's.</summary>
    bool IsFile { get; }

    /// <summary>Whether the block names <paramref name="field"/> with a value
    /// that is not null.</summary>
    bool Has(string field);

    /// <summary>The field as JavaScript's <c>String(value)</c> writes it — a list
    /// joins with commas — or <see langword="null"/> when the block has no value
    /// for it.</summary>
    string? Scalar(string field);

    /// <summary>The field as a list, as the generator's <c>asList</c> reads it;
    /// empty when the block has no value for it.</summary>
    IReadOnlyList<string> List(string field);

    /// <summary>Adds a <c>node_attribute</c> row per value, under
    /// <paramref name="name"/>, after the attributes already added.</summary>
    void AddAttribute(string name, IReadOnlyList<string> values);
}
