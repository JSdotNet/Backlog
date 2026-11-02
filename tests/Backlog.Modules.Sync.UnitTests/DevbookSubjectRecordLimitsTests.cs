using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// The one rule both ends of the devbook subject exchange run: what a subject
/// state change must be for the service to store it. The desktop leaves out of
/// its push whatever this refuses, and the endpoint refuses a batch carrying
/// one, so each case here is a change that would otherwise take its whole batch
/// down on every cycle.
/// </summary>
public class DevbookSubjectRecordLimitsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Id = Guid.Parse("0f4b1c3e-2a7d-5e11-9a3b-6c2d4e5f6a7b");

    private static DevbookSubjectChange Debt(string state = "planned") =>
        new(Id, Noon, new DevbookSubjectPayload("JSdotNet/Backlog", "debt", ".devbook/arc42/tdr/0003-paging.md", null, state, "DEV-TOWER", Noon));

    private static DevbookSubjectChange Proposal(string state, DevbookSubjectDecisionPayload? approval = null, DevbookSubjectDecisionPayload? acceptance = null) =>
        new(Id, Noon, new DevbookSubjectPayload("JSdotNet/Backlog", "proposal", "add-subject-store", null, state, "DEV-TOWER", Noon, approval, acceptance));

    private static DevbookSubjectChange Decision(string state, int? pullRequest) =>
        new(Id, Noon, new DevbookSubjectPayload("JSdotNet/Backlog", "decision", ".devbook/arc42/adr/0044-subject-state.md", pullRequest, state, "DEV-TOWER", Noon));

    private static DevbookSubjectChange Decided() => Decision("decided", 7);

    private static readonly DevbookSubjectDecisionPayload Approval =new("DEV-TOWER", Noon, "sha256:0123abcd");

    [Fact]
    public void A_state_each_kind_can_be_in_is_stored()
    {
        Assert.Null(DevbookSubjectRecordLimits.WhyRefused(Debt("in-progress")));
        Assert.Null(DevbookSubjectRecordLimits.WhyRefused(Decision("decided", 1604)));
        Assert.Null(DevbookSubjectRecordLimits.WhyRefused(Proposal("draft")));
        Assert.Null(DevbookSubjectRecordLimits.WhyRefused(Proposal("approved", Approval)));
        Assert.Null(DevbookSubjectRecordLimits.WhyRefused(Proposal("accepted", Approval, Approval)));
    }

    /// <summary>The contract's rows (devbook.decision@1 §Subjects), written down
    /// here independently of the Devbook module's copy: a state the module adds
    /// without the wire knowing would be refused here, which is the drift the
    /// cross-check in the desktop's suite is there to catch.</summary>
    [Fact]
    public void The_states_are_the_contracts_rows()
    {
        Assert.Equal(["draft", "approved", "accepted"], DevbookSubjectRecordLimits.StatesOf("proposal"));
        Assert.Equal(["proposed", "decided", "declined"], DevbookSubjectRecordLimits.StatesOf("decision"));
        Assert.Equal(["identified", "planned", "in-progress", "resolved"], DevbookSubjectRecordLimits.StatesOf("debt"));
        Assert.Empty(DevbookSubjectRecordLimits.StatesOf("chapter"));
    }

    [Theory]
    [InlineData("no subject")]
    [InlineData("empty id")]
    [InlineData("blank repository alias")]
    [InlineData("unknown kind")]
    [InlineData("kind in another case")]
    [InlineData("blank key")]
    [InlineData("state outside the row")]
    [InlineData("another kind's state")]
    [InlineData("blank by")]
    [InlineData("decision with no pull request")]
    [InlineData("decision with pull request zero")]
    [InlineData("pull request on a debt record")]
    [InlineData("approval on a debt record")]
    [InlineData("acceptance on a decision record")]
    [InlineData("approval fingerprint not one")]
    [InlineData("acceptance fingerprint in capitals")]
    [InlineData("approval with no by")]
    [InlineData("repository alias over 200")]
    [InlineData("key over 1024")]
    [InlineData("by over 255")]
    [InlineData("approval by over 255")]
    public void A_change_the_service_would_not_store_is_refused_with_a_reason(string shape)
    {
        var debt = Debt();
        var change = shape switch
        {
            "no subject" => debt with { Subject = null! },
            "empty id" => debt with { Id = Guid.Empty },
            "blank repository alias" => debt with { Subject = debt.Subject with { RepositoryAlias = "  " } },
            "unknown kind" => debt with { Subject = debt.Subject with { Kind = "chapter" } },
            "kind in another case" => debt with { Subject = debt.Subject with { Kind = "Debt" } },
            "blank key" => debt with { Subject = debt.Subject with { Key = " " } },
            "state outside the row" => Debt("someday"),
            "another kind's state" => Debt("decided"),
            "blank by" => debt with { Subject = debt.Subject with { By = "" } },
            "decision with no pull request" => Decision("proposed", null),
            "decision with pull request zero" => Decision("proposed", 0),
            "pull request on a debt record" => debt with { Subject = debt.Subject with { PullRequest = 12 } },
            "approval on a debt record" => debt with { Subject = debt.Subject with { Approval = Approval } },
            "acceptance on a decision record" => Decided() with { Subject = Decided().Subject with { Acceptance = Approval } },
            "approval fingerprint not one" => Proposal("approved", Approval with { Fingerprint = "sha256:xyz" }),
            "acceptance fingerprint in capitals" => Proposal("accepted", Approval, Approval with { Fingerprint = "sha256:0123ABCD" }),
            "approval with no by" => Proposal("approved", Approval with { By = " " }),
            "repository alias over 200" => debt with { Subject = debt.Subject with { RepositoryAlias = new string('a', 201) } },
            "key over 1024" => debt with { Subject = debt.Subject with { Key = new string('k', 1_025) } },
            "by over 255" => debt with { Subject = debt.Subject with { By = new string('w', 256) } },
            "approval by over 255" => Proposal("approved", Approval with { By = new string('w', 256) }),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "No such shape."),
        };

        var reason = DevbookSubjectRecordLimits.WhyRefused(change);

        Assert.False(string.IsNullOrWhiteSpace(reason), shape);
    }

    /// <summary>The reason names the field and never repeats the caller's value,
    /// as <see cref="AnnotationRecordLimits"/>' never does: it is checked before
    /// any length is, so an echoed value could be as long as the body cap, and it
    /// goes back in a <c>400</c> and into the desktop's warning.</summary>
    [Theory]
    [InlineData("kind")]
    [InlineData("state")]
    [InlineData("approval fingerprint")]
    [InlineData("acceptance fingerprint")]
    public void A_refusal_names_the_field_and_never_echoes_the_value(string field)
    {
        var echo = "ECHO-" + new string('x', 4_000);
        var debt = Debt();
        var change = field switch
        {
            "kind" => debt with { Subject = debt.Subject with { Kind = echo } },
            "state" => Debt(echo),
            "approval fingerprint" => Proposal("approved", Approval with { Fingerprint = echo }),
            "acceptance fingerprint" => Proposal("accepted", Approval, Approval with { Fingerprint = echo }),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "No such field."),
        };

        var reason = DevbookSubjectRecordLimits.WhyRefused(change);

        Assert.NotNull(reason);
        Assert.DoesNotContain("ECHO-", reason, StringComparison.Ordinal);
        Assert.True(reason.Length < 200, reason);
    }

    [Fact]
    public void The_bounds_are_the_annotation_ones_where_the_fields_are_the_same()
    {
        Assert.Equal(AnnotationRecordLimits.RepositoryAliasLength, DevbookSubjectRecordLimits.RepositoryAliasLength);
        Assert.Equal(AnnotationRecordLimits.ChapterPathLength, DevbookSubjectRecordLimits.KeyLength);
        Assert.Equal(AnnotationRecordLimits.AuthorLength, DevbookSubjectRecordLimits.ByLength);
    }
}
