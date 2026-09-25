namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The status words a surface allows, taken out of the knowledge folder they used
/// to be locked behind.
///
/// <para><c>MetadataView</c> took a <c>DevbookFolder</c> and asked
/// <c>DevbookStatus</c> both of the questions a record actually has — which words
/// may be offered, and what each of them looks like — so no caller outside
/// <c>.arc42</c>, <c>.domain</c>, <c>.design</c>, <c>.backlog</c> and <c>.tech</c>
/// could draw a record at all. Nothing else about the record was ever
/// folder-shaped: a field nobody writes is simply absent, which
/// <c>MetadataRecord</c> already models.</para>
///
/// <para>Two claims here, and the second is the one that matters most: that the
/// knowledge side still behaves exactly as it did. The vocabulary is a new way of
/// asking an old question, not a new answer to it.</para>
/// </summary>
public sealed class MetadataStatusVocabularyTests
{
    /// <summary>The knowledge folders, so a claim about "every vocabulary" is made
    /// against all five and not against the one that happened to be handy.</summary>
    private static readonly DevbookFolder[] Folders = Enum.GetValues<DevbookFolder>();

    [Fact]
    public void A_folders_vocabulary_is_the_folders_words_in_the_folders_order()
    {
        foreach (var folder in Folders)
        {
            Assert.Equal(DevbookStatus.Values(folder), DevbookStatus.Vocabulary(folder).Values);
        }
    }

    /// <summary>
    /// The reason the vocabularies are held rather than built.
    ///
    /// <para>A fresh object is a changed parameter to Blazor, so a folder resolved
    /// in a render expression would re-render every record on every pass — and the
    /// call sites that resolve one <em>are</em> render expressions:
    /// <c>Vocabulary="@DevbookStatus.Vocabulary(DevbookFolder)"</c> is what the
    /// header, the read view and the block view all write.</para>
    /// </summary>
    [Fact]
    public void The_same_folder_answers_with_the_same_instance()
    {
        foreach (var folder in Folders)
        {
            Assert.Same(DevbookStatus.Vocabulary(folder), DevbookStatus.Vocabulary(folder));
        }
    }

    /// <summary>Every word of every folder wears the badge that folder's tone maps
    /// onto, and a word in no folder wears the flag — which is
    /// <c>DevbookStatusBadge</c>'s whole former contents, asked of the vocabulary
    /// instead. Asserted against the tone rather than against a copy of the
    /// mapping: a second table here would be the thing it is checking.</summary>
    [Fact]
    public void Every_folders_word_keeps_the_badge_its_tone_maps_onto()
    {
        foreach (var folder in Folders)
        {
            var vocabulary = DevbookStatus.Vocabulary(folder);

            foreach (var status in DevbookStatus.Values(folder).Concat(DevbookStatus.RecognisedOnly(folder)))
            {
                // Every word but two kinds is offered: the resting value of a
                // folder that spells it by omission, whose option is the empty
                // one, and domain/'s decision rungs, which are never picked.
                var offered = status != vocabulary.RestingValue && !DevbookSchema.IsDecisionRung(status);
                Assert.Equal(offered, vocabulary.Offers(status));
                Assert.True(vocabulary.Recognises(status));
                Assert.False(vocabulary.IsUnrecognised(status));

                // The resting value written out is the one recognised word with
                // something to say about it — see
                // The_resting_value_written_out_keeps_its_tone_and_says_how_it_is_written.
                if (status == vocabulary.RestingValue) Assert.NotNull(vocabulary.Expectation(status));
                else Assert.Null(vocabulary.Expectation(status));

                // Not the empty string: every value of every vocabulary has a
                // tone, so a bare `badge--status` would mean a word fell through
                // to "no opinion".
                Assert.NotEqual(string.Empty, vocabulary.SlugFor(status));
            }
        }
    }

    /// <summary>A word one keystroke from a real one is flagged, wears the one
    /// modifier that spends no colour, and names the words that were expected. The
    /// urgency is in the title rather than in the tone, because painted on the
    /// alarming surface it wore the same red as a legitimate <c>blocked</c> and the
    /// two could not be told apart at a glance.</summary>
    [Fact]
    public void A_word_the_vocabulary_does_not_have_is_flagged_and_the_expectation_named()
    {
        var vocabulary = DevbookStatus.Vocabulary(DevbookFolder.Tech);

        Assert.True(vocabulary.IsUnrecognised("adoptd"));
        Assert.Equal("archived", vocabulary.SlugFor("adoptd"));
        Assert.Equal(
            "Unexpected status. Expected one of: candidate, trial, adopted, hold, retired.",
            vocabulary.Expectation("adoptd"));
    }

    /// <summary>No vocabulary is "nobody said", not "nothing is allowed". So it
    /// flags nothing, offers nothing, and leaves the plain badge — which is exactly
    /// the "no opinion" it means, and what <c>DevbookFolder.Unknown</c> has always
    /// produced.</summary>
    [Fact]
    public void No_vocabulary_judges_nothing()
    {
        foreach (var vocabulary in new[] { MetadataStatusVocabulary.None, DevbookStatus.Vocabulary(DevbookFolder.Unknown) })
        {
            Assert.True(vocabulary.IsEmpty);
            Assert.False(vocabulary.Offers("adopted"));
            Assert.False(vocabulary.IsUnrecognised("anything at all"));
            Assert.Equal(string.Empty, vocabulary.SlugFor("anything at all"));
            Assert.Null(vocabulary.Expectation("anything at all"));
        }
    }

    /// <summary>
    /// The two questions about membership, and why they are not one.
    ///
    /// <para><see cref="MetadataStatusVocabulary.Recognises"/> trims and ignores
    /// case, because a stray capital is not a different status.
    /// <see cref="MetadataStatusVocabulary.Offers"/> does not, because a browser
    /// matches a select's value against its options literally: a status of
    /// <c>Adopted</c> put into a select offering <c>adopted</c> would leave the
    /// control showing the first option as though the file had said it. So the loose
    /// question decides whether to flag, and the strict one decides whether a select
    /// is honest.</para>
    /// </summary>
    [Fact]
    public void A_stray_capital_is_recognised_and_still_not_offered()
    {
        var vocabulary = DevbookStatus.Vocabulary(DevbookFolder.Tech);

        Assert.True(vocabulary.Recognises(" Adopted "));
        Assert.False(vocabulary.IsUnrecognised(" Adopted "));
        Assert.False(vocabulary.Offers(" Adopted "));
    }

    /// <summary>Nothing in, nothing claimed: a blank status is not a word the
    /// vocabulary failed to recognise, and asking about one must not produce a
    /// modifier. The badge draws nothing at all for it — see
    /// <c>MetadataStatusBadgeTests</c>.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_status_takes_no_modifier(string? status)
    {
        Assert.Equal(string.Empty, MetadataStatusVocabulary.None.SlugFor(status));
        Assert.False(MetadataStatusVocabulary.None.Recognises(status));
    }

    /// <summary>The point of the whole change: a vocabulary that is nobody's
    /// folder. The resolver is the caller's, and it is only ever asked about a word
    /// in the list — the flag on a word in neither belongs to the vocabulary, so a
    /// caller never has to answer for something it has never heard of.</summary>
    [Fact]
    public void A_caller_outside_the_knowledge_folders_can_state_its_own_words()
    {
        var asked = new List<string>();

        var shipping = new MetadataStatusVocabulary(
            ["staged", "shipped", "rolled-back"],
            status =>
            {
                asked.Add(status);
                return status == "shipped" ? "done" : "ready";
            });

        Assert.Equal("done", shipping.SlugFor("shipped"));
        Assert.True(shipping.Offers("staged"));

        // The unrecognised case never reaches the resolver.
        Assert.Equal("archived", shipping.SlugFor("shiped"));
        Assert.Equal(["shipped"], asked);
    }

    [Fact]
    public void Only_the_folders_whose_status_rests_allow_stating_none()
    {
        // The split is the whole convention: where the field records how settled a
        // piece of writing is there is a resting value that needs no saying, and
        // where it is a rating on a ladder or a work state every value is a claim
        // the reader needs.
        Assert.True(DevbookStatus.Vocabulary(DevbookFolder.Arc42).AllowsNone);
        Assert.True(DevbookStatus.Vocabulary(DevbookFolder.Domain).AllowsNone);
        Assert.True(DevbookStatus.Vocabulary(DevbookFolder.Design).AllowsNone);
        Assert.False(DevbookStatus.Vocabulary(DevbookFolder.Tech).AllowsNone);
        Assert.False(DevbookStatus.Vocabulary(DevbookFolder.Backlog).AllowsNone);
        Assert.False(MetadataStatusVocabulary.None.AllowsNone);
    }

    [Fact]
    public void A_blank_is_selectable_only_where_none_is_allowed()
    {
        foreach (var folder in Folders)
        {
            var vocabulary = DevbookStatus.Vocabulary(folder);

            // Never offered — a browser matches option values literally and no
            // folder lists a blank among its words.
            Assert.False(vocabulary.Offers(string.Empty));
            Assert.False(vocabulary.Offers(null));

            // But selectable where the surface allows stating none, which is what
            // keeps the control on screen after a reader clears it.
            Assert.Equal(vocabulary.AllowsNone, vocabulary.Selectable(string.Empty));
            Assert.Equal(vocabulary.AllowsNone, vocabulary.Selectable(null));
        }
    }

    [Fact]
    public void Every_folders_words_stay_selectable_exactly_as_they_were()
    {
        foreach (var folder in Folders)
        {
            var vocabulary = DevbookStatus.Vocabulary(folder);
            foreach (var value in vocabulary.Values)
            {
                // Except the resting value spelled out, which contract 16 made
                // the one word of a list that is not an option: its option is
                // the empty one, and `status: active` keeps the badge that says
                // how it is written.
                Assert.Equal(value != vocabulary.RestingValue, vocabulary.Selectable(value));
            }

            foreach (var rung in vocabulary.RecognisedOnly)
            {
                Assert.False(vocabulary.Selectable(rung));
            }

            // And a typo still is not, so it keeps falling through to the pill
            // that flags it rather than being quietly offered.
            Assert.False(vocabulary.Selectable("nonsense"));
        }
    }

    [Fact]
    public void The_options_lead_with_no_status_only_where_none_is_allowed()
    {
        // Under contract 9 this read "No status", "draft", "active",
        // "deprecated": two options for one state. Contract 16 spells `active` by
        // omitting the field, so the empty option is `active`, and the word is
        // not offered a second time.
        Assert.Equal(
            ["active", "draft", "deprecated"],
            DevbookStatus.Vocabulary(DevbookFolder.Design).Options().Select(option => option.Label));

        // Value empty, because that is what a browser hands back for an option
        // with no value — there is no sentinel to translate out of later.
        Assert.Equal(
            [string.Empty, "draft", "deprecated"],
            DevbookStatus.Vocabulary(DevbookFolder.Design).Options().Select(option => option.Value));

        Assert.Equal(
            DevbookStatus.Values(DevbookFolder.Tech),
            DevbookStatus.Vocabulary(DevbookFolder.Tech).Options().Select(option => option.Value).ToList());
    }

    [Theory]
    [InlineData(DevbookFolder.Arc42, "active,draft,proposed,deprecated")]
    [InlineData(DevbookFolder.Domain, "active,draft,proposed,deprecated")]
    [InlineData(DevbookFolder.Design, "active,draft,deprecated")]
    public void A_resting_folder_offers_its_resting_value_once_as_the_empty_option(DevbookFolder folder, string labels)
    {
        var options = DevbookStatus.Vocabulary(folder).Options();

        Assert.Equal(labels.Split(','), options.Select(option => option.Label));
        Assert.Equal(DevbookSchema.RestingStatus, options[0].Label);
        Assert.Equal(string.Empty, options[0].Value);
        Assert.DoesNotContain(options, option => option.Value == DevbookSchema.RestingStatus);
    }

    [Fact]
    public void A_generic_surface_that_allows_none_still_leads_with_no_status()
    {
        // The resting label is a knowledge folder's fact. A caller that allows
        // none and names no resting value keeps the plain "No status" entry.
        var vocabulary = new MetadataStatusVocabulary(["open", "closed"], allowsNone: true);

        Assert.Equal(["No status", "open", "closed"], vocabulary.Options().Select(option => option.Label));
        Assert.Null(vocabulary.RestingValue);
    }

    [Fact]
    public void The_resting_value_written_out_keeps_its_tone_and_says_how_it_is_written()
    {
        // The "reported" case: right state, wrong spelling. Not a typo — it keeps
        // `active`'s modifier and no flag — but not offered either, and the title
        // says the value is written by omitting the field.
        var vocabulary = DevbookStatus.Vocabulary(DevbookFolder.Domain);

        Assert.True(vocabulary.Recognises("active"));
        Assert.False(vocabulary.IsUnrecognised("active"));
        Assert.False(vocabulary.Offers("active"));
        Assert.Equal("active", vocabulary.SlugFor("active"));
        Assert.Contains("omitting the status field", vocabulary.Expectation("active"), StringComparison.Ordinal);

        // A rating folder has no resting value, so `adopted` there is an ordinary
        // offered word with nothing to say about it.
        Assert.Null(DevbookStatus.Vocabulary(DevbookFolder.Tech).Expectation("adopted"));
    }

    [Fact]
    public void A_blank_in_a_resting_folder_wears_the_resting_tone()
    {
        // The select a chapter with no status shows reads `active`, so it wears
        // `active`'s badge rather than the "no opinion" one.
        foreach (var folder in new[] { DevbookFolder.Arc42, DevbookFolder.Domain, DevbookFolder.Design })
        {
            Assert.Equal("active", DevbookStatus.Vocabulary(folder).SlugFor(null));
        }
    }

    [Fact]
    public void Words_recognised_and_not_offered_wear_their_tone_and_are_never_options()
    {
        var asked = new List<string>();
        var vocabulary = new MetadataStatusVocabulary(
            ["draft", "live"],
            status =>
            {
                asked.Add(status);
                return status == "signed-off" ? "done" : "active";
            },
            recognisedOnly: ["signed-off"]);

        Assert.True(vocabulary.Recognises("signed-off"));
        Assert.False(vocabulary.IsUnrecognised("signed-off"));
        Assert.False(vocabulary.Offers("signed-off"));
        Assert.False(vocabulary.Selectable("signed-off"));
        Assert.Equal("done", vocabulary.SlugFor("signed-off"));
        Assert.DoesNotContain(vocabulary.Options(), option => option.Value == "signed-off");

        // And a typo names it among the words that were expected, since it is one.
        Assert.Equal("Unexpected status. Expected one of: draft, live, signed-off.", vocabulary.Expectation("signed"));
    }

    [Fact]
    public void A_blank_status_is_not_a_typo_in_any_folder()
    {
        // It used to be treated as one, which is why this is pinned. Every
        // non-empty vocabulary answered IsUnrecognised("") with true, so a blank
        // wore the "archived" pill — the retired state — under a tooltip claiming
        // it was unexpected. Latent only while nothing rendered a blank; the
        // moment a cleared chapter drew its control, "no status" looked retired.
        foreach (var folder in Folders)
        {
            var vocabulary = DevbookStatus.Vocabulary(folder);

            Assert.False(vocabulary.IsUnrecognised(string.Empty));
            Assert.False(vocabulary.IsUnrecognised(null));
            Assert.False(vocabulary.IsUnrecognised("   "));

            // No modifier, except where a blank states the resting value and so
            // wears its tone — see A_blank_in_a_resting_folder_wears_the_resting_tone.
            Assert.Equal(vocabulary.RestingValue is null ? string.Empty : "active", vocabulary.SlugFor(string.Empty));
            Assert.Null(vocabulary.Expectation(string.Empty));
        }
    }
}
