using System.Text.Json;
using Backlog.Infrastructure.GitHub;

namespace Backlog.Infrastructure.GitHub.UnitTests;

/// <summary>
/// The approvals a branch requires, as one branch's rules are read
/// (<see cref="GitHubClient.ReadRequiredApprovalsOfRef"/>): from its branch protection rule
/// and the pull request rules rulesets apply to it, the larger of the two, and none where
/// no rule requires an approval (<c>.devbook/domain/progress/features.md#pull-request-status</c>).
/// The read that asks GitHub for them is <see cref="GitHubRequiredApprovalsTests"/>'.
/// </summary>
public sealed class GitHubClientRequiredApprovalCountTests
{
    [Fact]
    public void A_branch_protection_rule_requiring_approvals_gives_its_count()
    {
        Assert.Equal(2, Required(BaseRef(protection: """{ "requiresApprovingReviews": true, "requiredApprovingReviewCount": 2 }""")));
    }

    [Fact]
    public void A_ruleset_pull_request_rule_gives_its_count()
    {
        Assert.Equal(1, Required(BaseRef(rules: """[ { "type": "REQUIRED_STATUS_CHECKS", "parameters": {} }, { "type": "PULL_REQUEST", "parameters": { "requiredApprovingReviewCount": 1 } } ]""")));
    }

    [Fact]
    public void The_larger_of_the_protection_rule_and_every_ruleset_rule_is_required()
    {
        var baseRef = BaseRef(
            protection: """{ "requiresApprovingReviews": true, "requiredApprovingReviewCount": 1 }""",
            rules: """[ { "type": "PULL_REQUEST", "parameters": { "requiredApprovingReviewCount": 3 } }, { "type": "PULL_REQUEST", "parameters": { "requiredApprovingReviewCount": 2 } } ]""");

        Assert.Equal(3, Required(baseRef));
    }

    /// <summary>No rule means no required total — not zero. GitHub's own answer for
    /// <c>main</c> here: no protection rule, and a ruleset that requires status checks
    /// and no review.</summary>
    [Fact]
    public void A_base_branch_with_no_rule_has_no_required_total()
    {
        Assert.Null(Required(BaseRef(rules: """[ { "type": "REQUIRED_STATUS_CHECKS", "parameters": {} } ]""")));
        Assert.Null(Required(BaseRef()));
    }

    [Fact]
    public void A_protection_rule_that_does_not_require_approving_reviews_has_no_required_total()
    {
        Assert.Null(Required(BaseRef(protection: """{ "requiresApprovingReviews": false, "requiredApprovingReviewCount": 2 }""")));
        Assert.Null(Required(BaseRef(rules: """[ { "type": "PULL_REQUEST", "parameters": { "requiredApprovingReviewCount": 0 } } ]""")));
    }

    private static int? Required(string baseRef)
    {
        using var json = JsonDocument.Parse(baseRef);
        return GitHubClient.ReadRequiredApprovalsOfRef(json.RootElement);
    }

    private static string BaseRef(string protection = "null", string rules = "[]") =>
        $$"""{ "branchProtectionRule": {{protection}}, "rules": { "nodes": {{rules}} } }""";
}
