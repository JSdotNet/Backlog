namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What a workflow subject is called, under <c>devbook.decision@1</c>: a change
/// proposal by its folder name, a decision record by its path and pull request,
/// a debt record by its path — and the id every device derives from that name.
/// <para>
/// The id is the load-bearing part. Two devices that each set a state on the
/// same subject before either has synced must end up writing one replica
/// document, not two, so the id is a function of the repository alias and the
/// subject's key rather than a fresh Guid. The expected ids below were computed
/// outside .NET (Python's <c>hashlib</c> and <c>uuid</c>) from the rule the
/// record states, so the test does not recompute them the way the code does.
/// </para>
/// </summary>
public sealed class DevbookSubjectTests
{
    private const string Repository = "JSdotNet/Backlog";

    [Fact]
    public void Each_kind_has_its_canonical_key()
    {
        Assert.Equal("proposal:pay-by-card", DevbookSubject.Proposal("pay-by-card").CanonicalKey);
        Assert.Equal("decision:.devbook/arc42/adr/0044-subject-state.md#1607", DevbookSubject.Decision(".devbook/arc42/adr/0044-subject-state.md", 1607).CanonicalKey);
        Assert.Equal("debt:.devbook/arc42/tdr/0001-archify.md", DevbookSubject.Debt(".devbook/arc42/tdr/0001-archify.md").CanonicalKey);
    }

    [Fact]
    public void A_record_path_is_spelled_one_way()
    {
        var spelled = DevbookSubject.Debt(@" ./.devbook\arc42\tdr\0001-archify.md ");

        Assert.Equal(DevbookSubject.Debt(".devbook/arc42/tdr/0001-archify.md"), spelled);
        Assert.Equal(".devbook/arc42/tdr/0001-archify.md", DevbookSubject.Debt("/.devbook/arc42/tdr/0001-archify.md").Key);
    }

    [Fact]
    public void Each_kind_offers_the_states_the_contract_closes()
    {
        Assert.Equal(["draft", "approved", "accepted"], DevbookSubject.StatesOf(DevbookSubjectKind.Proposal));
        Assert.Equal(["proposed", "decided", "declined"], DevbookSubject.StatesOf(DevbookSubjectKind.Decision));
        Assert.Equal(["identified", "planned", "in-progress", "resolved"], DevbookSubject.StatesOf(DevbookSubjectKind.Debt));
        Assert.Equal(DevbookSubject.StatesOf(DevbookSubjectKind.Debt), DevbookSubject.Debt(".devbook/arc42/tdr/0001-archify.md").States);
    }

    [Fact]
    public void A_decision_record_needs_its_pull_request()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DevbookSubject.Decision(".devbook/arc42/adr/0044-subject-state.md", 0));
        Assert.Throws<ArgumentException>(() => new DevbookSubject(DevbookSubjectKind.Decision, ".devbook/arc42/adr/0044-subject-state.md"));
        Assert.Throws<ArgumentException>(() => new DevbookSubject(DevbookSubjectKind.Debt, ".devbook/arc42/tdr/0001-archify.md", 12));
        Assert.Throws<ArgumentException>(() => DevbookSubject.Proposal("  "));
    }

    /// <summary>The alias is digested as <see cref="DevbookSubject.AliasKey"/>
    /// spells it — trimmed and lower-cased — so the expected values below are over
    /// <c>jsdotnet/backlog</c>.</summary>
    [Fact]
    public void The_id_is_the_same_on_every_device_for_the_same_subject()
    {
        Assert.Equal(
            Guid.Parse("f73649b2-5f7f-5428-850c-c844369aab7a"),
            DevbookSubject.IdFor(Repository, DevbookSubject.Debt(".devbook/arc42/tdr/0001-archify.md")));
        Assert.Equal(
            Guid.Parse("d983c893-ecbd-56d1-9a8c-1bfc8cbdcdd0"),
            DevbookSubject.IdFor(Repository, DevbookSubject.Proposal("pay-by-card")));
        Assert.Equal(
            Guid.Parse("e1fc062b-3217-5414-9d6e-002053268964"),
            DevbookSubject.IdFor(null, DevbookSubject.Decision(".devbook/arc42/adr/0044-subject-state.md", 1607)));
    }

    [Fact]
    public void Another_repository_or_pull_request_is_another_subject()
    {
        var debt = DevbookSubject.Debt(".devbook/arc42/tdr/0001-archify.md");

        Assert.NotEqual(DevbookSubject.IdFor(Repository, debt), DevbookSubject.IdFor("JSdotNet/devbook", debt));
        Assert.NotEqual(
            DevbookSubject.IdFor(Repository, DevbookSubject.Decision(".devbook/arc42/adr/0044-subject-state.md", 1607)),
            DevbookSubject.IdFor(Repository, DevbookSubject.Decision(".devbook/arc42/adr/0044-subject-state.md", 1608)));
    }

    /// <summary>A repository alias is matched without regard to case everywhere a
    /// store looks one up, so it has to be digested the same way: two spellings of
    /// one alias are one subject, never two states.</summary>
    [Fact]
    public void An_alias_names_one_repository_whatever_its_case()
    {
        var debt = DevbookSubject.Debt(".devbook/arc42/tdr/0001-archify.md");

        Assert.Equal(DevbookSubject.IdFor(Repository, debt), DevbookSubject.IdFor(" jsdotnet/BACKLOG ", debt));
        Assert.Equal("jsdotnet/backlog", DevbookSubject.AliasKey("  JSdotNet/Backlog "));
        Assert.Equal(string.Empty, DevbookSubject.AliasKey(null));
        Assert.Equal(string.Empty, DevbookSubject.AliasKey("   "));
    }

    /// <summary>A proposal is keyed by its folder name under
    /// <c>openspec/changes/</c>, and the same name is read off disk to fingerprint
    /// the change — so a path, a parent step or the archive folder is no
    /// name.</summary>
    [Theory]
    [InlineData("changes/pay-by-card")]
    [InlineData(@"changes\pay-by-card")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("C:x")]
    [InlineData("../../secrets")]
    [InlineData("pay..by-card")]
    [InlineData("archive")]
    [InlineData("Archive")]
    public void A_proposal_name_that_is_no_folder_name_is_refused(string name)
    {
        Assert.False(DevbookSubject.IsProposalName(name));
        Assert.Throws<ArgumentException>(() => DevbookSubject.Proposal(name));
    }

    [Fact]
    public void A_proposal_name_is_a_folder_name() =>
        Assert.True(DevbookSubject.IsProposalName("pay-by-card"));

    /// <summary>Who sets a state is the host's to say, and the host already names
    /// its person one way — the machine label a remark is written by — so a state
    /// and a remark set on the same machine carry the same name.</summary>
    [Fact]
    public void A_state_is_set_by_the_same_host_name_a_remark_is_written_by()
    {
        Assert.Equal(Environment.MachineName, DevbookLocalAuthor.Name);
        Assert.Equal(DevbookLocalAuthor.Name, DevbookAnnotationComments.LocalAuthor);
    }

    [Theory]
    [InlineData(".devbook/arc42/adr/0044-subject-state.md", DevbookSubjectKind.Decision)]
    [InlineData(".arc42/adr/0001-decision.md", DevbookSubjectKind.Decision)]
    [InlineData(@".devbook\arc42\tdr\0001-archify.md", DevbookSubjectKind.Debt)]
    [InlineData(".arc42/tdr/0002-conventions.md", DevbookSubjectKind.Debt)]
    public void A_record_under_adr_or_tdr_is_a_subject(string path, DevbookSubjectKind kind) =>
        Assert.Equal(kind, DevbookSubjects.Classify(path));

    [Theory]
    [InlineData(".devbook/arc42/adr/README.md")]
    [InlineData(".devbook/arc42/tdr/README.md")]
    [InlineData(".devbook/arc42/adr/index.md")]
    [InlineData(".devbook/arc42/adr/guidelines/0004-result-objects.md")]
    [InlineData(".devbook/arc42/05-building-block-view.md")]
    [InlineData(".devbook/domain/devbook/domain.md")]
    [InlineData(".devbook/arc42/adr/diagram.png")]
    public void An_ordinary_chapter_is_no_subject(string path) =>
        Assert.Null(DevbookSubjects.Classify(path));
}
