namespace Backlog.Desktop.UI.UnitTests;

/// <summary>Which page of the new domain view a Domain menu row opens, read from
/// its path alone (local ADR 0040).</summary>
public sealed class DomainViewUnitTests
{
    [Theory]
    [InlineData(null, DomainViewPage.None)]
    [InlineData("", DomainViewPage.None)]
    [InlineData("context-map.md", DomainViewPage.ContextMap)]
    [InlineData("tasks", DomainViewPage.Context)]
    [InlineData("tasks/", DomainViewPage.Context)]
    [InlineData("tasks/context.md", DomainViewPage.Context)]
    [InlineData("tasks/dependencies.md", DomainViewPage.Context)]
    [InlineData("tasks/domain.md", DomainViewPage.Domain)]
    [InlineData("financieel/domain.boeking.md", DomainViewPage.Domain)]
    [InlineData("financieel/domain.boeking.invariants.md", DomainViewPage.Domain)]
    [InlineData("tasks/features.md", DomainViewPage.Features)]
    [InlineData("tasks/requirements.md", DomainViewPage.Requirements)]
    [InlineData("tasks/requirements.checkout.md", DomainViewPage.Requirements)]
    [InlineData("tasks/model.md", DomainViewPage.Model)]
    [InlineData("tasks/flow.md", DomainViewPage.Flow)]
    [InlineData(@"tasks\flow.md", DomainViewPage.Flow)]
    [InlineData("tasks/glossary-notes.md", DomainViewPage.Other)]
    public void A_row_opens_the_page_for_its_file_kind(string? path, DomainViewPage expected) =>
        Assert.Equal(expected, DomainViewUnit.PageFor(path));

    [Theory]
    [InlineData(DomainViewPage.ContextMap, "context-map")]
    [InlineData(DomainViewPage.Requirements, "requirements")]
    public void A_page_key_is_kebab_case(DomainViewPage page, string expected) =>
        Assert.Equal(expected, DomainViewUnit.Key(page));

    [Fact]
    public void Every_page_has_a_placeholder_title_and_description()
    {
        foreach (var page in Enum.GetValues<DomainViewPage>())
        {
            Assert.False(string.IsNullOrWhiteSpace(DomainViewUnit.Title(page)));
            Assert.False(string.IsNullOrWhiteSpace(DomainViewUnit.Description(page)));
        }
    }
}
