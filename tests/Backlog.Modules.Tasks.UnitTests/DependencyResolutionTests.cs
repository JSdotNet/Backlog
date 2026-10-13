using Backlog.Modules.Tasks.Abstractions;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// The index is how the pane asks the dependency rule once per change instead of
/// once per row, so every answer it gives has to be the answer the candidate list
/// gives. Each case states the expected id from the rule itself, then holds the
/// two ways of asking to it.
/// </summary>
public class DependencyResolutionTests
{
    private static readonly Guid PlanAStep = Guid.Parse("0f2f6c1e-0a3b-4d5e-8f90-1a2b3c4d5e6f");
    private static readonly Guid PlanBStep = Guid.Parse("7a8b9c0d-1e2f-4a3b-9c4d-5e6f7a8b9c0d");
    private static readonly Guid Loose = Guid.Parse("c3d4e5f6-a7b8-4c9d-8e0f-1a2b3c4d5e6f");

    private static readonly IReadOnlyList<string> NoTags = [];

    /// <summary>Two plans that each own a <c>review-plan</c> step, and one step
    /// nobody else's slug collides with.</summary>
    private static readonly DependencyResolution.Candidate[] Store =
    [
        new(PlanAStep, "review-plan", "plan-a"),
        new(PlanBStep, "review-plan", "plan-b"),
        new(Loose, "write-tests", "plan-c")
    ];

    private static string ResolveBothWays(string value, string? planId, IReadOnlyList<string> tagged)
    {
        var fromList = DependencyResolution.Resolve(value, Store, planId, tagged);
        var fromIndex = DependencyResolution.Resolve(value, new DependencyResolution.CandidateIndex(Store), planId, tagged);

        Assert.Equal(fromList, fromIndex);
        return fromIndex;
    }

    [Theory]
    [InlineData("0f2f6c1e-0a3b-4d5e-8f90-1a2b3c4d5e6f")]
    [InlineData("0F2F6C1E-0A3B-4D5E-8F90-1A2B3C4D5E6F")]
    public void A_real_id_comes_back_as_written_in_either_case(string written)
    {
        Assert.Equal(written, ResolveBothWays(written, planId: null, NoTags));
    }

    [Fact]
    public void A_real_id_is_never_reread_as_a_slug_another_entry_carries()
    {
        // A stored id: token that happens to spell another entry's real id must
        // not pull the value away from the entry it already names.
        DependencyResolution.Candidate[] store =
        [
            new(PlanAStep, null, null),
            new(PlanBStep, PlanAStep.ToString(), "plan-b")
        ];
        var value = PlanAStep.ToString();

        var fromIndex = DependencyResolution.Resolve(value, new DependencyResolution.CandidateIndex(store), "plan-b", NoTags);

        Assert.Equal(value, fromIndex);
        Assert.Equal(DependencyResolution.Resolve(value, store, "plan-b", NoTags), fromIndex);
    }

    [Fact]
    public void A_slug_resolves_to_the_step_under_the_asking_entrys_plan()
    {
        Assert.Equal(PlanBStep.ToString(), ResolveBothWays("review-plan", planId: "plan-b", NoTags));
    }

    [Fact]
    public void A_slug_resolves_through_a_plan_the_asking_entry_is_tagged_with()
    {
        Assert.Equal(PlanAStep.ToString(), ResolveBothWays("review-plan", planId: null, ["misc", "plan-a"]));
    }

    [Fact]
    public void The_plan_wins_over_a_tag_naming_another_plan()
    {
        Assert.Equal(PlanBStep.ToString(), ResolveBothWays("review-plan", planId: "plan-b", ["plan-a"]));
    }

    [Fact]
    public void A_slug_only_one_entry_carries_resolves_to_it_without_a_plan()
    {
        Assert.Equal(Loose.ToString(), ResolveBothWays("write-tests", planId: "unrelated", NoTags));
    }

    [Fact]
    public void A_slug_two_plans_own_is_left_as_written_for_an_entry_under_neither()
    {
        Assert.Equal("review-plan", ResolveBothWays("review-plan", planId: "plan-z", ["other"]));
    }

    [Fact]
    public void A_slug_nothing_carries_is_left_as_written()
    {
        Assert.Equal("nobody-has-this", ResolveBothWays("nobody-has-this", planId: "plan-a", NoTags));
    }

    [Fact]
    public void A_slug_is_matched_by_its_exact_spelling()
    {
        Assert.Equal("Write-Tests", ResolveBothWays("Write-Tests", planId: null, NoTags));
    }

    [Fact]
    public void The_first_of_two_same_plan_steps_in_store_order_wins()
    {
        var first = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var second = Guid.Parse("22222222-2222-4222-8222-222222222222");
        DependencyResolution.Candidate[] store =
        [
            new(first, "setup", "plan-a"),
            new(second, "setup", "plan-a")
        ];

        var fromIndex = DependencyResolution.Resolve("setup", new DependencyResolution.CandidateIndex(store), "plan-a", NoTags);

        Assert.Equal(first.ToString(), fromIndex);
        Assert.Equal(DependencyResolution.Resolve("setup", store, "plan-a", NoTags), fromIndex);
    }

    [Fact]
    public void A_list_keeps_its_order_and_collapses_a_slug_and_the_id_it_means()
    {
        string[] values = ["write-tests", "review-plan", PlanAStep.ToString().ToUpperInvariant(), Loose.ToString()];

        var fromList = DependencyResolution.ResolveAll(values, Store, "plan-a", NoTags);
        var fromIndex = DependencyResolution.ResolveAll(values, new DependencyResolution.CandidateIndex(Store), "plan-a", NoTags);

        string[] expected = [Loose.ToString(), PlanAStep.ToString()];
        Assert.Equal(expected, fromIndex);
        Assert.Equal(fromList, fromIndex);
    }

    /// <summary>An import asks once per entry it wrote, most of which wait on
    /// nothing: an empty list is answered without reading the store at all.</summary>
    [Fact]
    public void An_empty_list_resolves_to_nothing_without_reading_the_store()
    {
        var resolved = DependencyResolution.ResolveAll([], new UnreadableStore(), "plan-a", NoTags);

        Assert.Empty(resolved);
    }

    [Fact]
    public void An_empty_store_leaves_every_value_as_written()
    {
        string[] values = ["review-plan", PlanAStep.ToString()];

        var fromIndex = DependencyResolution.ResolveAll(values, new DependencyResolution.CandidateIndex([]), "plan-a", NoTags);

        Assert.Equal(values, fromIndex);
        Assert.Equal(DependencyResolution.ResolveAll(values, [], "plan-a", NoTags), fromIndex);
    }

    /// <summary>A store that fails the moment anything reads it.</summary>
    private sealed class UnreadableStore : IReadOnlyCollection<DependencyResolution.Candidate>
    {
        public int Count => throw new InvalidOperationException("The store was read.");

        public IEnumerator<DependencyResolution.Candidate> GetEnumerator() =>
            throw new InvalidOperationException("The store was read.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
