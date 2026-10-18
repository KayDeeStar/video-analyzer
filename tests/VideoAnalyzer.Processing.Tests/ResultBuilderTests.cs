using VideoAnalyzer.Shared;

namespace VideoAnalyzer.Processing.Tests;

public class ResultBuilderTests
{
    private static readonly BoundingBox Box = new(0.1, 0.1, 0.2, 0.4);

    private static LabelSample Person(long ms, int count = 1) =>
        new(ms, "Person", 99, ["Person Description"], Enumerable.Repeat(Box, count).ToList());

    [Fact]
    public void Merges_close_person_detections_into_appearances()
    {
        var ranges = ResultBuilder.MergeIntoRanges([0, 500, 1000, 1500, 9000, 9500, 30_000], maxGapMs: 2000);

        Assert.Equal(
            [new TimeRange(0, 1500), new TimeRange(9000, 9500), new TimeRange(30_000, 30_000)],
            ranges);
    }

    [Fact]
    public void Merging_no_detections_gives_no_appearances()
    {
        Assert.Empty(ResultBuilder.MergeIntoRanges([], 2000));
    }

    [Fact]
    public void Counts_most_people_visible_at_once()
    {
        var result = Build(labels: [Person(0, 1), Person(500, 3), Person(1000, 2)]);

        Assert.Equal(3, result.People.MaxVisibleAtOnce);
        Assert.Single(result.People.Appearances);
    }

    [Fact]
    public void Summarizes_labels_by_name()
    {
        var labels = new[]
        {
            new LabelSample(0, "Car", 80, ["Vehicles and Automotive"], [Box]),
            new LabelSample(4000, "Car", 95, ["Vehicles and Automotive"], [Box, Box]),
            new LabelSample(2000, "Backpack", 85, ["Accessories"], []),
        };

        var summary = ResultBuilder.SummarizeLabels(labels);

        var car = Assert.Single(summary, l => l.Name == "Car");
        Assert.Equal((2, 95.0, 2, 0L, 4000L), (car.Detections, car.MaxConfidence, car.MaxInstances, car.FirstSeenMs, car.LastSeenMs));
        Assert.Equal("Car", summary[0].Name); // most detections first
    }

    [Fact]
    public void Drops_rare_low_confidence_labels()
    {
        var labels = new[]
        {
            new LabelSample(1000, "Gun", 69, ["Weapons and Military"], []),          // one unsure hit: noise
            new LabelSample(0, "Smoke", 70, ["Public Safety"], []),
            new LabelSample(500, "Smoke", 72, ["Public Safety"], []),
            new LabelSample(1000, "Smoke", 75, ["Public Safety"], []),               // unsure but repeated: kept
            new LabelSample(1500, "Poster", 86, ["Furniture and Furnishings"], []), // confident: kept
        };

        var names = ResultBuilder.SummarizeLabels(labels).Select(l => l.Name);

        Assert.Equal(["Smoke", "Poster"], names);
    }

    [Fact]
    public void Includes_the_ai_report_and_model()
    {
        var report = new AiReport("A person walks by.", null, [], null, [], [], [], [], null);

        var result = ResultBuilder.Build("20261018-000000-00000000", DateTimeOffset.UnixEpoch, Video, [], report, "us.amazon.nova-lite-v1:0");

        Assert.Same(report, result.Report);
        Assert.Equal("us.amazon.nova-lite-v1:0", result.ReportModel);
    }

    [Fact]
    public void Handles_video_without_any_detections()
    {
        var result = Build();

        Assert.Equal(0, result.People.MaxVisibleAtOnce);
        Assert.Empty(result.Labels);
        Assert.Null(result.Report);
    }

    private static readonly VideoInfo Video = new(60_000, 25, 1920, 1080, "h264", "QuickTime / MOV");

    private static AnalysisResult Build(LabelSample[]? labels = null) =>
        ResultBuilder.Build("20261018-000000-00000000", DateTimeOffset.UnixEpoch, Video, labels ?? [], null, null);
}
