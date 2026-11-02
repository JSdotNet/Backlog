using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// The rule both ends of the annotation exchange run, on the two members a remark
/// gained with devbook's branch-true decision: its kind and its branch. Both are
/// optional on the wire, so a payload from a device that sends neither is stored as
/// it always was; a kind outside the closed set and a branch no git ref could be
/// are refused.
/// </summary>
public class AnnotationRecordLimitsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static AnnotationChange Change(string? kind = null, string? branch = null) =>
        new(Guid.NewGuid(), Noon, null, new AnnotationPayload(
            "JSdotNet/Backlog", ".devbook/domain/devbook/features.md", 2, "Who owns this?", "DEV-TOWER", Noon, false,
            Kind: kind, Branch: branch));

    [Fact]
    public void A_payload_without_kind_or_branch_is_stored()
    {
        Assert.Null(AnnotationRecordLimits.WhyRefused(Change()));
    }

    [Theory]
    [InlineData("comment")]
    [InlineData("question")]
    [InlineData("suggestion")]
    [InlineData("flag")]
    public void Each_kind_of_the_closed_set_is_stored(string kind)
    {
        Assert.Null(AnnotationRecordLimits.WhyRefused(Change(kind)));
    }

    [Fact]
    public void An_unknown_kind_is_refused()
    {
        var reason = AnnotationRecordLimits.WhyRefused(Change("praise"));

        Assert.NotNull(reason);
        Assert.Contains("praise", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_branch_is_stored()
    {
        Assert.Null(AnnotationRecordLimits.WhyRefused(Change(branch: "feature/remark-kinds")));
    }

    [Fact]
    public void A_branch_past_the_bound_is_refused()
    {
        var reason = AnnotationRecordLimits.WhyRefused(Change(branch: new string('b', AnnotationRecordLimits.BranchLength + 1)));

        Assert.NotNull(reason);
        Assert.Contains("branch", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void The_kinds_are_devbooks_closed_set_default_first()
    {
        Assert.Equal(["comment", "question", "suggestion", "flag"], AnnotationRecordLimits.Kinds);
    }
}
