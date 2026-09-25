namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The product's devbook vocabularies, pinned against the rule text CI enforces.
///
/// <para>Every expected value below is read out of the vendored contract-16 rule
/// files through <see cref="DevbookRuleText"/>, never restated here. A contract
/// bump swaps those files; whatever the product has not yet learned then fails
/// by name. <c>DevbookSchemaContractTests</c> is the same idea for the
/// database's DDL.</para>
/// </summary>
public sealed class DevbookRuleTextContractTests
{
    [Theory]
    [InlineData("domain", DevbookFolder.Domain)]
    [InlineData("tech", DevbookFolder.Tech)]
    [InlineData("ai", DevbookFolder.Ai)]
    public void Each_folders_type_sets_are_the_rules(string folder, DevbookFolder devbookFolder)
    {
        var (chapter, file) = DevbookRuleText.TypeRow(folder);

        Assert.Equal(chapter, DevbookSchema.ChapterTypes(devbookFolder));
        Assert.Equal(file, DevbookSchema.FileTypes(devbookFolder));
        Assert.True(DevbookSchema.DefinesTypes(devbookFolder));
    }

    [Fact]
    public void The_domain_rule_restates_the_same_type_sets()
    {
        Assert.Equal(DevbookRuleText.DomainTypeRow("Chapter"), DevbookSchema.ChapterTypes(DevbookFolder.Domain));
        Assert.Equal(DevbookRuleText.DomainTypeRow("File"), DevbookSchema.FileTypes(DevbookFolder.Domain));
    }

    [Theory]
    [InlineData(DevbookFolder.Arc42)]
    [InlineData(DevbookFolder.Design)]
    public void Arc42_and_design_define_no_type(DevbookFolder folder)
    {
        Assert.False(DevbookSchema.DefinesTypes(folder));
        Assert.Empty(DevbookSchema.ChapterTypes(folder));
        Assert.Empty(DevbookSchema.FileTypes(folder));
    }

    [Fact]
    public void A_domain_page_the_convention_does_not_name_takes_its_filename_as_its_type()
    {
        Assert.True(DevbookSchema.IsKnownType(DevbookFolder.Domain, DevbookMetadataLevel.File, "go-live-takeover", ".domain/billing/go-live-takeover.md"));
        Assert.False(DevbookSchema.IsKnownType(DevbookFolder.Domain, DevbookMetadataLevel.File, "go-live-takeover", ".domain/billing/other.md"));
        Assert.False(DevbookSchema.IsKnownType(DevbookFolder.Domain, DevbookMetadataLevel.Chapter, "go-live-takeover", ".domain/billing/go-live-takeover.md"));
        Assert.False(DevbookSchema.IsKnownType(DevbookFolder.Tech, DevbookMetadataLevel.File, "shared", ".tech/shared.md"));
    }

    [Fact]
    public void The_folders_that_rest_by_omission_and_the_folders_that_require_status_are_the_rules()
    {
        var resting = DevbookRuleText.StatusFolders("optional");
        var required = DevbookRuleText.StatusFolders("**required**");

        foreach (var folder in Enum.GetValues<DevbookFolder>().Where(folder => folder is not DevbookFolder.Unknown and not DevbookFolder.Backlog))
        {
            var name = FolderName(folder);
            Assert.Equal(resting.Contains(name), DevbookSchema.RestsByOmission(folder));
            Assert.Equal(required.Contains(name), DevbookSchema.RequiresStatus(folder));
        }

        Assert.Equal(DevbookRuleText.RestingValue(), DevbookSchema.RestingStatus);
    }

    [Fact]
    public void The_resting_folders_status_vocabulary_allows_none_and_the_rating_folders_do_not()
    {
        foreach (var folder in new[] { DevbookFolder.Arc42, DevbookFolder.Domain, DevbookFolder.Design, DevbookFolder.Tech, DevbookFolder.Ai })
        {
            Assert.Equal(DevbookSchema.RestsByOmission(folder), DevbookStatus.Vocabulary(folder).AllowsNone);
        }
    }

    [Fact]
    public void The_decision_rungs_are_the_rules_and_belong_to_domain_alone()
    {
        Assert.Equal(DevbookRuleText.DecisionRungs(), DevbookSchema.DecisionRungs);

        foreach (var rung in DevbookSchema.DecisionRungs)
        {
            Assert.True(DevbookStatus.IsKnown(DevbookFolder.Domain, rung), $"domain/ should recognise '{rung}'.");

            foreach (var folder in new[] { DevbookFolder.Arc42, DevbookFolder.Design, DevbookFolder.Tech, DevbookFolder.Ai })
            {
                Assert.False(DevbookStatus.IsKnown(folder, rung), $"{folder} must not recognise '{rung}'.");
                Assert.DoesNotContain(DevbookStatus.Vocabulary(folder).Options(), option => option.Value == rung);
            }

            // Recognised, and still never offered: a rung is written by the
            // approval gate with its record, never picked from a list.
            Assert.DoesNotContain(DevbookStatus.Vocabulary(DevbookFolder.Domain).Options(), option => option.Value == rung);
        }

        Assert.True(DevbookSchema.AllowsDecisionRungs(DevbookFolder.Domain));
        Assert.False(DevbookSchema.AllowsDecisionRungs(DevbookFolder.Arc42));
    }

    [Fact]
    public void The_six_record_fields_are_the_rules_and_are_domain_only()
    {
        var bullets = DevbookRuleText.FieldBullets("approved-").Concat(DevbookRuleText.FieldBullets("accepted-")).ToList();

        Assert.Equal(bullets.Select(bullet => bullet.Field), DevbookSchema.DecisionRecordFields);
        Assert.All(bullets, bullet => Assert.StartsWith("`domain/` only", bullet.Scope, StringComparison.Ordinal));
    }

    [Fact]
    public void The_review_triad_and_its_states_are_the_rules()
    {
        var triad = DevbookRuleText.FieldBullets("review").Select(bullet => bullet.Field).ToList();

        Assert.Equal(triad, DevbookSchema.ReviewFields);
        Assert.Equal(DevbookRuleText.ReviewStates(), DevbookSchema.ReviewStates);
        Assert.Equal(9, DevbookSchema.StateFields.Count);
        Assert.All(DevbookSchema.StateFields, field => Assert.True(DevbookSchema.IsStateField(field)));
    }

    [Fact]
    public void Deployment_and_index_values_are_the_rules()
    {
        Assert.Equal(DevbookRuleText.DeploymentValues(), DevbookSchema.DeploymentValues);
        Assert.Equal(DevbookRuleText.IndexValues(), DevbookSchema.IndexValues);
    }

    [Fact]
    public void Extension_keys_are_the_rules_shape()
    {
        var shape = DevbookRuleText.ExtensionKeyShape();

        Assert.StartsWith(DevbookSchema.ExtensionPrefix, shape, StringComparison.Ordinal);
        Assert.True(DevbookSchema.IsExtensionKey("ext.some-plugin.checked"));
        Assert.False(DevbookSchema.IsExtensionKey("extra"));
    }

    [Fact]
    public void Annotation_kinds_and_states_are_the_rules_with_the_rules_defaults()
    {
        var (defaultKind, kinds) = DevbookRuleText.AnnotationField("kind");
        var (defaultStatus, statuses) = DevbookRuleText.AnnotationField("status");

        Assert.Equal(kinds, DevbookAnnotationFence.Kinds);
        Assert.Equal(statuses, DevbookAnnotationFence.Statuses);

        var bare = DevbookAnnotationFence.Parse("author: jobsc\ndate: 2026-09-02\nbody: Is this still true?");
        Assert.Equal(defaultKind, bare.Kind);
        Assert.Equal(defaultStatus, bare.Status);
    }

    [Fact]
    public void The_domain_type_marker_vocabulary_is_the_domain_rule()
    {
        Assert.Equal(DevbookSchema.ChapterTypes(DevbookFolder.Domain), DevbookTypeMarkers.ChapterTypes);
        Assert.Equal(DevbookSchema.FileTypes(DevbookFolder.Domain), DevbookTypeMarkers.FileTypes);
    }

    private static string FolderName(DevbookFolder folder) => folder switch
    {
        DevbookFolder.Arc42 => "arc42",
        DevbookFolder.Domain => "domain",
        DevbookFolder.Design => "design",
        DevbookFolder.Tech => "tech",
        DevbookFolder.Ai => "ai",
        _ => throw new ArgumentOutOfRangeException(nameof(folder))
    };
}
