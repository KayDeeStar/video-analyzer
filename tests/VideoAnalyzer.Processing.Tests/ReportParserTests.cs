using VideoAnalyzer.Shared;

namespace VideoAnalyzer.Processing.Tests;

public class ReportParserTests
{
    [Fact]
    public void Parses_a_well_formed_report()
    {
        var report = ReportParser.Parse(
            """
            {
              "summary": "A young woman sprays paint onto the camera.",
              "setting": "Outdoor parking lot, daylight",
              "visibleText": ["RESERVED PARKING"],
              "onScreenDateTime": null,
              "people": [{
                "label": "Person 1", "firstSeenSeconds": 0, "lastSeenSeconds": 8.5,
                "apparentSex": "female", "apparentAge": "16-22", "hair": "long, black",
                "upperClothing": "black hoodie", "carriedItems": "spray paint can", "faceVisibility": "partial"
              }],
              "vehicles": [],
              "timeline": [{ "seconds": 4, "description": "Sprays paint at the lens" }, { "seconds": 0, "description": "Walks in" }],
              "notableEvents": [{ "seconds": 4, "description": "Camera covered with paint", "severity": "high" }],
              "qualityNotes": "View obscured after 4 s"
            }
            """);

        Assert.Equal("A young woman sprays paint onto the camera.", report.Summary);
        var person = Assert.Single(report.People);
        Assert.Equal(("female", "16-22", "spray paint can", 8.5), (person.ApparentSex, person.ApparentAge, person.CarriedItems, person.LastSeenSeconds));
        Assert.Equal([0.0, 4.0], report.Timeline.Select(t => t.Seconds)); // sorted
        Assert.Equal("high", Assert.Single(report.NotableEvents).Severity);
        Assert.Equal(["RESERVED PARKING"], report.VisibleText);
    }

    [Fact]
    public void Reads_the_written_briefing()
    {
        var report = ReportParser.Parse(
            """
            { "summary": "x",
              "narrative": [
                { "title": "Individual appearance", "text": "A woman with long dark hair with lighter brown highlights." },
                { "heading": "Action and behavior", "content": "At about 0:04 she sprays paint onto the camera." },
                { "title": "Empty", "text": "" }
              ] }
            """);

        Assert.Equal(
            [
                new ReportSection("Individual appearance", "A woman with long dark hair with lighter brown highlights."),
                new ReportSection("Action and behavior", "At about 0:04 she sprays paint onto the camera."),
            ],
            report.Narrative);
    }

    [Theory]
    [InlineData("""{ "summary": "x", "narrative": "She tapes a camera to a pole." }""", "Analysis")]
    [InlineData("""{ "summary": "x", "narrative": { "Action and behavior": "She tapes a camera to a pole." } }""", "Action and behavior")]
    public void Accepts_a_briefing_in_other_shapes(string json, string expectedTitle)
    {
        var section = Assert.Single(ReportParser.Parse(json).Narrative);

        Assert.Equal((expectedTitle, "She tapes a camera to a pole."), (section.Title, section.Text));
    }

    [Fact]
    public void Tolerates_markdown_fences_and_chatter()
    {
        var report = ReportParser.Parse("Here is the report:\n```json\n{ \"summary\": \"Nothing happens.\" }\n```\nLet me know!");

        Assert.Equal("Nothing happens.", report.Summary);
        Assert.Empty(report.People);
        Assert.Empty(report.Vehicles);
        Assert.Empty(report.Timeline);
        Assert.Empty(report.VisibleText);
    }

    [Fact]
    public void Accepts_times_written_as_text()
    {
        var report = ReportParser.Parse(
            """
            { "summary": "x",
              "people": [{ "firstSeenSeconds": "0:04", "lastSeenSeconds": "12s" }],
              "timeline": [{ "seconds": "1:02:03", "description": "late" }, { "seconds": "soon", "description": "unknown time" }] }
            """);

        var person = Assert.Single(report.People);
        Assert.Equal((4.0, 12.0), (person.FirstSeenSeconds, person.LastSeenSeconds));
        Assert.Equal([0.0, 3723.0], report.Timeline.Select(t => t.Seconds));
    }

    [Fact]
    public void Joins_lists_given_for_single_values_and_labels_unnamed_people()
    {
        var report = ReportParser.Parse(
            """
            { "summary": "x", "visibleText": "EXIT",
              "people": [{ "accessories": ["gloves", "face mask"], "height": 170, "facialHair": "null" }, { "label": "" }] }
            """);

        Assert.Equal("gloves, face mask", report.People[0].Accessories);
        Assert.Equal("170", report.People[0].Height);
        Assert.Null(report.People[0].FacialHair);
        Assert.Equal(["Person 1", "Person 2"], report.People.Select(p => p.Label));
        Assert.Equal(["EXIT"], report.VisibleText);
    }

    [Theory]
    [InlineData("I can't help with that.")]
    [InlineData("{ \"summary\": ")]
    public void Rejects_answers_without_a_usable_report(string text)
    {
        Assert.Throws<ReportFormatException>(() => ReportParser.Parse(text));
    }

    [Theory]
    [InlineData("4", 4.0)]
    [InlineData("4.5s", 4.5)]
    [InlineData("0:04", 4.0)]
    [InlineData("1:02:03", 3723.0)]
    [InlineData("about four", null)]
    public void Parses_seconds(string text, double? expected)
    {
        Assert.Equal(expected, ReportParser.ParseSeconds(text));
    }
}
