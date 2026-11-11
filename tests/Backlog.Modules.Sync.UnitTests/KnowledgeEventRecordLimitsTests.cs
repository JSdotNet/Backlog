using System.Text.Json;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Sync.UnitTests;

/// <summary>
/// The one rule both ends of the knowledge event exchange run: what a change must be for the
/// service to store it. The desktop leaves out of its push whatever this refuses, and the
/// endpoint refuses a batch carrying one. Nothing here reads a field of the event itself —
/// Sync stores the document and reads none of it.
/// </summary>
public class KnowledgeEventRecordLimitsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Id = Guid.Parse("0f4b1c3e-2a7d-7e11-9a3b-6c2d4e5f6a7b");

    [Theory]
    [InlineData("knowledge-event")]
    [InlineData("event-series")]
    public void An_event_or_a_series_with_an_object_document_is_stored(string kind)
    {
        Assert.Null(KnowledgeEventRecordLimits.WhyRefused(new KnowledgeEventChange(Id, kind, Noon, Json("""{"title":"NDC Oslo 2026"}"""))));
    }

    [Fact]
    public void A_field_the_service_has_never_heard_of_is_no_reason_to_refuse()
    {
        Assert.Null(KnowledgeEventRecordLimits.WhyRefused(
            new KnowledgeEventChange(Id, "knowledge-event", Noon, Json("""{"somethingNew":[1,2,3]}"""))));
    }

    [Fact]
    public void A_change_without_an_id_is_refused()
    {
        Assert.NotNull(KnowledgeEventRecordLimits.WhyRefused(new KnowledgeEventChange(Guid.Empty, "knowledge-event", Noon, Json("{}"))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Knowledge-Event")]
    [InlineData("note")]
    public void A_kind_the_feed_does_not_carry_is_refused(string kind)
    {
        Assert.NotNull(KnowledgeEventRecordLimits.WhyRefused(new KnowledgeEventChange(Id, kind, Noon, Json("{}"))));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"an event\"")]
    [InlineData("null")]
    public void A_document_that_is_not_an_object_is_refused(string document)
    {
        Assert.NotNull(KnowledgeEventRecordLimits.WhyRefused(new KnowledgeEventChange(Id, "knowledge-event", Noon, Json(document))));
    }

    [Fact]
    public void A_document_without_a_body_is_refused()
    {
        Assert.NotNull(KnowledgeEventRecordLimits.WhyRefused(new KnowledgeEventChange(Id, "knowledge-event", Noon, default)));
    }

    [Fact]
    public void A_document_past_128_kilobytes_is_refused_and_one_at_it_is_not()
    {
        // {"notes":"…"} is twelve characters around the notes.
        var atBound = Json($$"""{"notes":"{{new string('x', (128 * 1024) - 12)}}"}""");
        var pastBound = Json($$"""{"notes":"{{new string('x', (128 * 1024) - 11)}}"}""");

        Assert.Null(KnowledgeEventRecordLimits.WhyRefused(new KnowledgeEventChange(Id, "knowledge-event", Noon, atBound)));
        Assert.NotNull(KnowledgeEventRecordLimits.WhyRefused(new KnowledgeEventChange(Id, "knowledge-event", Noon, pastBound)));
    }

    /// <summary>The reason goes back in the 400 and into the desktop's warning, so it never
    /// echoes what the caller sent.</summary>
    [Fact]
    public void A_reason_never_repeats_the_callers_kind()
    {
        var reason = KnowledgeEventRecordLimits.WhyRefused(new KnowledgeEventChange(Id, "<script>", Noon, Json("{}")));

        Assert.NotNull(reason);
        Assert.DoesNotContain("<script>", reason, StringComparison.Ordinal);
    }

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
